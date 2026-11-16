using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnalyseTool.Hosting.Mcp.Transport;

/// <summary>
///     <c>mcp.json</c> in the user's local application data: the address of the server, its token and a ready command
///     for Claude Code. The token stays the same across Revit sessions, so a client configured once keeps working.
/// </summary>
/// <param name="Url">The address of the MCP endpoint.</param>
/// <param name="Token">The bearer token every request must carry.</param>
/// <param name="Name">A server name for the client configuration: the add-in, for every Revit version.</param>
/// <param name="ClaudeCode">The command that adds this server to Claude Code.</param>
internal sealed record McpConnectionFile(string Url, string Token, string Name, string ClaudeCode)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    ///     Writes the file for <paramref name="url" />, keeping the token of an earlier file.
    /// </summary>
    public static McpConnectionFile Write(string path, string url, string name)
    {
        var token = ReadToken(path) is { Length: >= 32 } existing ? existing : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var claudeCode = $"claude mcp add --transport http {name} {url} --header \"Authorization: Bearer {token}\"";
        var file = new McpConnectionFile(url, token, name, claudeCode);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
        return file;
    }

    /// <summary>The token of the file; <see langword="null" /> when there is none.</summary>
    public static string? ReadToken(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<McpConnectionFile>(File.ReadAllText(path), Options)?.Token : null;
        }
        catch (JsonException)
        {
            // A broken file gets a new token; the client has to be configured again
            return null;
        }
    }
}
