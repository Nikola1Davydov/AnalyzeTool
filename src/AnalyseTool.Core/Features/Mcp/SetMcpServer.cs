using AnalyseTool.Core.Common.Transport;
using AnalyseTool.Sdk;

namespace AnalyseTool.Core.Features.Mcp
{
    /// <summary>Enables/disables the MCP bridge (and sets its port), persisting the choice. Starts or
    /// stops the localhost WebSocket listener live.</summary>
    [RevitCommand(
        Description = "Enables/disables the MCP bridge and sets its port (persisted). Starts/stops the " +
                      "localhost listener live. Payload: { enabled: bool, port?: number }.",
        InputType = typeof(SetMcpServer.Args),
        HiddenFromMcp = true)] // plugin self-management, not for the AI
    internal sealed class SetMcpServer : IRevitTask
    {
        internal sealed class Args
        {
            /// <summary>Turn the MCP bridge on (true) or off (false).</summary>
            public bool Enabled { get; set; }

            /// <summary>TCP port for the localhost WebSocket bridge. Optional; keeps the current port if omitted.</summary>
            public int? Port { get; set; }
        }

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Args args = ctx.Payload.As<Args>() ?? new Args();
            return Task.FromResult<object?>(McpServerController.Apply(args.Enabled, args.Port));
        }
    }
}
