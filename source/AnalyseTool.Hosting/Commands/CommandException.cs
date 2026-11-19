namespace AnalyseTool.Hosting.Commands;

/// <summary>
///     An expected command failure with a machine-readable <see cref="Code" />; the caller gets it as <see cref="CommandError" />.
/// </summary>
/// <param name="code">A stable code, for example <c>bad_request</c>; the add-in adds its own codes.</param>
/// <param name="message">A message for the user.</param>
/// <param name="innerException">The underlying exception, if any.</param>
[PublicAPI]
public class CommandException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>Gets the stable error code.</summary>
    public string Code { get; } = code;
}

/// <summary>
///     The stable codes of <see cref="CommandError" /> that the module itself returns.
/// </summary>
[PublicAPI]
public static class CommandErrorCodes
{
    /// <summary>No command with this name is registered.</summary>
    public const string NotFound = "not_found";

    /// <summary>The payload cannot be read as the command request.</summary>
    public const string BadRequest = "bad_request";

    /// <summary>The same command is already running (<c>UseSingleFlight</c>).</summary>
    public const string AlreadyRunning = "already_running";

    /// <summary>The caller cancelled the command.</summary>
    public const string Canceled = "canceled";

    /// <summary>An unexpected exception.</summary>
    public const string Failed = "failed";
}
