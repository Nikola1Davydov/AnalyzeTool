using AnalyseTool.Mcp.Bridge;
using AnalyseTool.Sdk;

namespace AnalyseTool.Mcp.Bridge.Features
{
    /// <summary>Returns the MCP bridge status (running/port/enabled + the server exe path) for the
    /// Settings page.</summary>
    [RevitCommand(
        Description = "Returns the MCP bridge status: running, enabled, port, the server exe path and whether it exists.",
        ReadOnly = true,
        HiddenFromMcp = true)] // plugin self-management, not for the AI
    internal sealed class GetMcpStatus : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
            => Task.FromResult<object?>(McpServerController.Status());
    }
}
