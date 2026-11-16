using Microsoft.Extensions.Logging;

namespace AnalyseTool.Hosting.Mcp;

/// <summary>
///     The <c>Mcp</c> configuration section.
/// </summary>
[PublicAPI]
public sealed class McpOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Mcp";

    /// <summary>
    ///     Gets or sets a value indicating whether the server runs. By default on in the <c>Development</c> environment and off
    ///     otherwise: a user turns it on in <c>usersettings.json</c>.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    ///     Gets or sets the port; by default the base port of <c>AddMcp</c> plus the Revit version offset, so two Revit versions
    ///     run side by side.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    ///     Gets or sets the lowest level of the MCP SDK's own records that reach the log; Warning by default — at Information
    ///     the SDK writes several records per call.
    /// </summary>
    public LogLevel SdkLogLevel { get; set; } = LogLevel.Warning;
}
