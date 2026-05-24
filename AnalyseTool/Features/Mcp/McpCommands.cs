using AnalyseTool.Common.Transport;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Mcp
{
    /// <summary>Returns the MCP bridge status (running/port/enabled + the server exe path) for the
    /// Settings page.</summary>
    [RevitCommand("GetMcpStatus",
        Description = "Returns the MCP bridge status: running, enabled, port, the server exe path and whether it exists.",
        ReadOnly = true)]
    internal sealed class GetMcpStatus : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
            => Task.FromResult<object?>(McpServerController.Status());
    }

    /// <summary>Enables/disables the MCP bridge (and sets its port), persisting the choice. Starts or
    /// stops the localhost WebSocket listener live.</summary>
    [RevitCommand("SetMcpServer",
        Description = "Enables/disables the MCP bridge and sets its port (persisted). Starts/stops the " +
                      "localhost WebSocket listener live. Payload: { enabled: bool, port?: number }.")]
    internal sealed class SetMcpServer : RevitTask<SetMcpServer.Args>
    {
        public sealed class Args
        {
            /// <summary>Turn the MCP bridge on (true) or off (false).</summary>
            public bool Enabled { get; set; }

            /// <summary>TCP port for the localhost WebSocket bridge. Optional; keeps the current port if omitted.</summary>
            public int? Port { get; set; }
        }

        public override Task<object?> ExecuteAsync(Args args, IRevitContext ctx, CancellationToken ct)
            => Task.FromResult<object?>(McpServerController.Apply(args?.Enabled ?? false, args?.Port));
    }
}
