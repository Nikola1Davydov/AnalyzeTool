namespace AnalyseTool.Hosting.Mcp;

/// <summary>
///     A picture as the result of a command: over MCP it becomes an image the AI client looks at, not JSON.
/// </summary>
/// <param name="Data">The image file, for example a PNG.</param>
/// <param name="MimeType">The media type of <paramref name="Data" />, for example <c>image/png</c>.</param>
/// <param name="Caption">A text that goes with the image: what it shows.</param>
/// <remarks>Other callers — a page — get it as JSON, with <paramref name="Data" /> in base64.</remarks>
[PublicAPI]
public sealed record McpImage(byte[] Data, string MimeType, string? Caption = null);
