using System.Text.Json;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Scripting.Execution;

namespace AnalyseTool.Hosting.Scripting;

/// <summary>
///     Compiles C# with Roslyn and runs it once inside Revit — the scratchpad of an AI client, and of the Dev page.
/// </summary>
/// <remarks>
///     Registered only while scripting is on (<c>AddScripting</c>): off, the command does not exist, so neither MCP nor a page
///     can reach it. Arbitrary code runs with the user's rights and may change the model, hence <c>Destructive</c>; its
///     changes are one undo step, a dry run rolls them back, and every run goes to the script journal.
/// </remarks>
[McpTool(Destructive = true, Title = "Execute C# in Revit")]
[Command(Description =
    "Compiles a C# method body with Roslyn and runs it once on the Revit thread. " +
    "In scope: uiapp (UIApplication), uidoc (UIDocument, null without an open document), doc (Document, may be null) " +
    "and cancellationToken; Autodesk.Revit.DB, Autodesk.Revit.UI, System.Linq and System.Collections.Generic are imported, " +
    "other `using` directives may come on the first lines. `return` plain data — numbers, strings, lists, anonymous objects — " +
    "not Revit elements: return ids and names instead. To change the model open a Transaction yourself. " +
    "All changes of one call are one undo step for the user; if the code throws, they are rolled back. " +
    "dryRun true runs the code and reports what it would change, then rolls everything back — use it before a change you are not sure of. " +
    "Returns value (what the code returned) and changes (counts and ids of added, modified, deleted elements). " +
    "Check unfamiliar types with DescribeApi first: the API differs between Revit versions. " +
    "Revit is frozen while the code runs: keep it short. Errors come back as compile_failed or script_failed with the line of the code.")]
internal sealed class ExecuteCode(ScriptRunner runner) : ICommand<ExecuteCodeRequest, ExecuteCodeResult>
{
    /// <inheritdoc />
    public async Task<ExecuteCodeResult> ExecuteAsync(ExecuteCodeRequest request, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "There is no code to run.");
        }

        var result = await runner.RunAsync(request.Code, request.DryRun, context.CancellationToken);
        return new ExecuteCodeResult(result.Value, result.Changes, result.RolledBack);
    }
}

/// <summary>The input of <see cref="ExecuteCode" />.</summary>
/// <param name="Code">The C# method body; it may <c>return</c> a value.</param>
/// <param name="DryRun">Run the code, report its changes and roll them back.</param>
internal sealed record ExecuteCodeRequest(string Code, bool DryRun = false);

/// <summary>The result of <see cref="ExecuteCode" />.</summary>
/// <param name="Value">What the code returned.</param>
/// <param name="Changes">What it changed in the active document.</param>
/// <param name="RolledBack">Whether the changes were rolled back — a dry run.</param>
internal sealed record ExecuteCodeResult(JsonElement Value, ScriptChanges Changes, bool RolledBack);
