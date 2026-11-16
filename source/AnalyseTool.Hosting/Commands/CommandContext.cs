using System.Text.Json;

namespace AnalyseTool.Hosting.Commands;

/// <summary>
///     One call of a command, as a transport hands it to the pipeline.
/// </summary>
/// <param name="Command">The command name; case-insensitive.</param>
/// <param name="Payload">The raw JSON input; <c>default</c> when the command takes none.</param>
/// <param name="Source">Who calls: <see cref="CommandSources" />.</param>
[PublicAPI]
public sealed record CommandRequest(string Command, JsonElement Payload, string Source)
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    /// <summary>
    ///     Gets the values that travel with the call outside the payload, as HTTP headers. The module does not read them; the
    ///     add-in's middleware does — for example the document the caller's data came from.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = NoHeaders;

    /// <summary>Gets the progress sink bound to the caller, if any.</summary>
    public IProgress<ProgressInfo>? Progress { get; init; }

    /// <summary>Gets the cancellation of the call.</summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
///     The well-known values of <see cref="CommandRequest.Source" />.
/// </summary>
[PublicAPI]
public static class CommandSources
{
    /// <summary>A ribbon button through <c>ICommandSender</c>; failures are shown with <c>IUserNotifier</c>.</summary>
    public const string Ribbon = "ribbon";

    /// <summary>A WebView2 window or dock pane.</summary>
    public const string WebView = "webview2";

    /// <summary>An AI client through MCP; reserved, no transport yet.</summary>
    public const string Mcp = "mcp";
}

/// <summary>
///     Progress of a long command, reported through <see cref="CommandContext.Progress" />.
/// </summary>
/// <param name="Fraction">From 0 to 1.</param>
/// <param name="Message">An optional text for the user.</param>
[PublicAPI]
public sealed record ProgressInfo(double Fraction, string? Message = null);

/// <summary>
///     What a command and the middleware see about the current call.
/// </summary>
[PublicAPI]
public sealed class CommandContext
{
    private static readonly IProgress<ProgressInfo> NoProgress = new Progress<ProgressInfo>();

    internal CommandContext(CommandDescriptor? descriptor, CommandRequest request, IServiceProvider services)
    {
        Descriptor = descriptor;
        Request = request;
        Services = services;
        Progress = request.Progress ?? NoProgress;
        CancellationToken = request.CancellationToken;
    }

    /// <summary>Gets the call as the transport passed it.</summary>
    public CommandRequest Request { get; }

    /// <summary>Gets the registered command, or <see langword="null" /> when no command has this name.</summary>
    public CommandDescriptor? Descriptor { get; }

    /// <summary>Gets the command name.</summary>
    public string CommandName => Descriptor?.Name ?? Request.Command;

    /// <summary>Gets who calls: <see cref="CommandSources" />.</summary>
    public string Source => Request.Source;

    /// <summary>Gets the progress sink of the caller.</summary>
    public IProgress<ProgressInfo> Progress { get; }

    /// <summary>Gets the cancellation of the call.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets the raw JSON input, for middleware; the command itself receives the deserialized request.</summary>
    public JsonElement RawPayload => Request.Payload;

    /// <summary>Gets the services of the call's scope.</summary>
    public IServiceProvider Services { get; }
}

/// <summary>
///     The outcome of a call: a value or an error, never an exception.
/// </summary>
/// <param name="Value">The command result; <c>null</c> for <see cref="Empty" /> or on error.</param>
/// <param name="Error">The error, or <see langword="null" /> on success.</param>
[PublicAPI]
public sealed record CommandResult(object? Value, CommandError? Error)
{
    /// <summary>Gets a value indicating whether the command succeeded.</summary>
    public bool Succeeded => Error is null;

    internal static CommandResult Success(object? value)
    {
        return new CommandResult(value is Empty ? null : value, null);
    }

    internal static CommandResult Failure(string code, string message)
    {
        return new CommandResult(null, new CommandError(code, message));
    }
}

/// <summary>
///     A command error for the caller.
/// </summary>
/// <param name="Code">One of <see cref="CommandErrorCodes" />.</param>
/// <param name="Message">A message for the user.</param>
[PublicAPI]
public sealed record CommandError(string Code, string Message);
