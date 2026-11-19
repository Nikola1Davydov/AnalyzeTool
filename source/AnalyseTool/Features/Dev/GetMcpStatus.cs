#if DEBUG
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Features.Dev;

/// <summary>
///     Debug-only command of the «Dev» page: the state of the MCP server and what a client needs to connect — the address,
///     the token and the command for Claude Code.
/// </summary>
[Command(Description = "Returns the state of the MCP server, its connection details and its tools (Debug only).")]
public sealed class GetMcpStatus(IMcpEndpoint endpoint) : ICommand<McpEndpointInfo>
{
    /// <inheritdoc />
    public Task<McpEndpointInfo> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(endpoint.GetInfo());
    }
}
#endif
