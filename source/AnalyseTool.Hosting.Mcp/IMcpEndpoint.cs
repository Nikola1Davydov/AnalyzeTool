namespace AnalyseTool.Hosting.Mcp;

/// <summary>
///     The state of the MCP server, for a page that helps the user connect a client.
/// </summary>
[PublicAPI]
public interface IMcpEndpoint
{
    /// <summary>Returns where the server listens, how to connect to it and what tools it offers.</summary>
    McpEndpointInfo GetInfo();

    /// <summary>
    ///     Starts or stops the server now, in this Revit, without waiting for the next start: the switch of the settings page.
    ///     The setting itself (<c>Mcp:Enabled</c>) is the caller's to save.
    /// </summary>
    /// <returns>Where the server is after the change: listening, off, or the port busy.</returns>
    Task<McpEndpointInfo> SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}

/// <summary>
///     Whether the MCP server listens.
/// </summary>
[PublicAPI]
public enum McpEndpointState
{
    /// <summary>Turned off by <c>Mcp:Enabled</c>, or the host has not started yet.</summary>
    Off,

    /// <summary>Listens on <see cref="McpEndpointInfo.Url" />.</summary>
    Listening,

    /// <summary>The port is taken — usually by another Revit of the same version.</summary>
    PortBusy
}

/// <summary>
///     What a client needs to connect to the MCP server.
/// </summary>
/// <param name="State">Whether the server listens.</param>
/// <param name="Url">The address of the endpoint; where it would be when the server does not listen.</param>
/// <param name="Name">A server name for the client configuration, unique per add-in and Revit version.</param>
/// <param name="Token">The bearer token every request must carry; <see langword="null" /> unless the server listens.</param>
/// <param name="ClaudeCode">The command that adds the server to Claude Code; <see langword="null" /> unless the server listens.</param>
/// <param name="ConnectionFile">The path of <c>mcp.json</c> with the same details.</param>
/// <param name="Tools">The tools: the commands marked with <see cref="McpToolAttribute" />.</param>
[PublicAPI]
public sealed record McpEndpointInfo(
    McpEndpointState State,
    Uri Url,
    string Name,
    string? Token,
    string? ClaudeCode,
    string ConnectionFile,
    IReadOnlyList<McpToolInfo> Tools);

/// <summary>
///     A tool of the MCP server.
/// </summary>
/// <param name="Name">The tool name — the command name.</param>
/// <param name="Description">What the tool does.</param>
/// <param name="ReadOnly">Whether the tool only reads the model.</param>
[PublicAPI]
public sealed record McpToolInfo(string Name, string? Description, bool ReadOnly);
