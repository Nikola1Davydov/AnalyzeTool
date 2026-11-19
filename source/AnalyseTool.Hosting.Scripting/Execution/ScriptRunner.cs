using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.UI;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Scripting.Compilation;
using AnalyseTool.Hosting.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnalyseTool.Hosting.Scripting.Execution;

/// <summary>
///     The error codes of a script run, besides the pipeline's own.
/// </summary>
[PublicAPI]
public static class ScriptErrorCodes
{
    /// <summary>The code does not compile; the message lists the errors with their lines.</summary>
    public const string CompileFailed = "compile_failed";

    /// <summary>The code threw; the message names the exception and the line of the script.</summary>
    public const string ScriptFailed = "script_failed";
}

/// <summary>
///     The outcome of a script run.
/// </summary>
/// <param name="Value">What the script returned, as JSON.</param>
/// <param name="Changes">What it changed in the active document.</param>
/// <param name="RolledBack">Whether the changes were rolled back — a dry run.</param>
internal sealed record ScriptResult(JsonElement Value, ScriptChanges Changes, bool RolledBack);

/// <summary>
///     Compiles a script on the thread pool, runs it once on the Revit thread and returns its result as JSON.
/// </summary>
/// <remarks>
///     <para>
///         With an open document the script runs inside one <see cref="TransactionGroup" />: its transactions become one undo
///         step, a dry run rolls them back, and a script that throws leaves the model as it was. The changes are recorded
///         from <c>DocumentChanged</c> meanwhile.
///     </para>
///     <para>
///         The result is serialized on the Revit thread too, before the group ends: reading a property of a Revit object
///         anywhere else is not allowed, and a rolled-back element is gone. What cannot be serialized — a Revit object with a
///         property that throws — comes back as its <c>ToString()</c>.
///     </para>
///     <para>Every run goes to the <see cref="ScriptJournal" />.</para>
/// </remarks>
internal sealed partial class ScriptRunner(
    IRevitDispatcher dispatcher,
    ScriptJournal journal,
    IOptions<JsonSerializerOptions> serializerOptions,
    ILogger<ScriptRunner> logger)
{
    /// <summary>The name of the undo step of a script.</summary>
    public const string UndoName = "ExecuteCode";

    /// <summary>The add-in's load context in Revit; the default one in tests.</summary>
    private static readonly AssemblyLoadContext Owner = AssemblyLoadContext.GetLoadContext(typeof(ScriptRunner).Assembly) ?? AssemblyLoadContext.Default;

    // The references are collected once, at the first run: a few hundred files
    private readonly Lazy<ScriptCompiler> _compiler = new(() => new ScriptCompiler(ScriptReferences.Collect(Owner)));

    private JsonSerializerOptions? _resultOptions;

    public async Task<ScriptResult> RunAsync(string code, bool dryRun, CancellationToken cancellationToken)
    {
        var where = new RunPlace();
        try
        {
            var result = await CompileAndRunAsync(code, dryRun, where, cancellationToken).ConfigureAwait(false);
            Journal(new ScriptJournalEntry(DateTimeOffset.Now, where.Revit, where.Document, dryRun, "succeeded", result.Changes, null, code));
            return result;
        }
        catch (CommandException exception)
        {
            Journal(new ScriptJournalEntry(DateTimeOffset.Now, where.Revit, where.Document, dryRun, exception.Code, null, exception.Message, code));
            throw;
        }
    }

    private async Task<ScriptResult> CompileAndRunAsync(string code, bool dryRun, RunPlace where, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var compilation = _compiler.Value.Compile(code);
        if (!compilation.Succeeded)
        {
            throw new CommandException(ScriptErrorCodes.CompileFailed, string.Join(Environment.NewLine, compilation.Errors));
        }

        LogCompiled(logger, code.Length, stopwatch.ElapsedMilliseconds);

        var context = new ScriptLoadContext(Owner);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(compilation.Assembly!), new MemoryStream(compilation.Pdb!));
            var execute = assembly.GetType(ScriptCompiler.TypeName)!.GetMethod(ScriptCompiler.MethodName)!;
            var run = execute.CreateDelegate<Func<UIApplication, CancellationToken, object?>>();

            return await dispatcher.InvokeAsync(application => Run(run, application, dryRun, where, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.Unload();
        }
    }

    private ScriptResult Run(
        Func<UIApplication, CancellationToken, object?> run,
        UIApplication application,
        bool dryRun,
        RunPlace where,
        CancellationToken cancellationToken)
    {
        var document = application.ActiveUIDocument?.Document;
        where.Revit = application.Application.VersionNumber;
        where.Document = document?.Title;

        // A read-only document, or one with a transaction already open, cannot start a group: the script runs as is
        var canGroup = document is { IsReadOnly: false, IsModifiable: false };
        using var recorder = canGroup ? new ChangeRecorder(application.Application, document!) : null;
        using var group = canGroup ? new TransactionGroup(document!, UndoName) : null;
        group?.Start();

        try
        {
            var value = ToJson(run(application, cancellationToken));
            var changes = recorder?.Stop() ?? ScriptChanges.None;

            if (group is not null)
            {
                if (dryRun)
                {
                    group.RollBack();
                }
                else
                {
                    group.Assimilate();
                }
            }

            return new ScriptResult(value, changes, dryRun && group is not null);
        }
        catch (Exception exception) when (exception is not CommandException)
        {
            recorder?.Stop();
            var rolledBack = RollBack(group);
            throw new CommandException(ScriptErrorCodes.ScriptFailed, Describe(exception) + (rolledBack ? " The changes of the script were rolled back." : ""));
        }
    }

    /// <summary>Rolls back a group a failed script left open; <see langword="false" /> when there was nothing to roll back.</summary>
    private static bool RollBack(TransactionGroup? group)
    {
        if (group?.HasStarted() != true || group.HasEnded())
        {
            return false;
        }

        group.RollBack();
        return true;
    }

    private void Journal(ScriptJournalEntry entry)
    {
        try
        {
            journal.Write(entry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogJournalFailed(logger, exception, journal.Directory);
        }
    }

    private JsonElement ToJson(object? value)
    {
        _resultOptions ??= new JsonSerializerOptions(serializerOptions.Value) { ReferenceHandler = ReferenceHandler.IgnoreCycles, MaxDepth = 16 };

        try
        {
            return JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object), _resultOptions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotSerializable(logger, exception, value!.GetType().FullName);
            return JsonSerializer.SerializeToElement(value.ToString());
        }
    }

    /// <summary>The exception and the line of the script it came from — the PDB maps the frames to <c>script.cs</c>.</summary>
    private static string Describe(Exception exception)
    {
        var frame = exception.StackTrace?
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains(ScriptCompiler.FileName, StringComparison.Ordinal));

        var where = frame is null ? "" : $" ({frame[(frame.LastIndexOf(ScriptCompiler.FileName, StringComparison.Ordinal))..]})";
        return $"{exception.GetType().Name}: {exception.Message}{where}";
    }

    [LoggerMessage(LogLevel.Debug, "Script of {Length} characters compiled in {ElapsedMilliseconds} ms")]
    private static partial void LogCompiled(ILogger logger, int length, long elapsedMilliseconds);

    [LoggerMessage(LogLevel.Debug, "The result of a script ({Type}) cannot be serialized; it is returned as text")]
    private static partial void LogNotSerializable(ILogger logger, Exception exception, string? type);

    [LoggerMessage(LogLevel.Warning, "The script journal in {Directory} cannot be written")]
    private static partial void LogJournalFailed(ILogger logger, Exception exception, string directory);

    /// <summary>Where a run happened, filled on the Revit thread and read for the journal also after a failure.</summary>
    private sealed class RunPlace
    {
        public string? Revit { get; set; }

        public string? Document { get; set; }
    }
}
