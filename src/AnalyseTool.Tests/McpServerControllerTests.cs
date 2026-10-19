using AnalyseTool.Mcp.Bridge;

namespace AnalyseTool.Tests;

/// <summary>Saving the MCP settings restarts the bridge only when something changed: a restart cancels
/// every running agent command, and the Settings page used to restart on every save.</summary>
public class McpServerControllerTests
{
    [Test]
    [Arguments(true, true, 17890, 17890, false)]   // saved unchanged while running
    [Arguments(false, false, 17890, 17890, false)] // off and staying off
    [Arguments(false, false, 17890, 18000, false)] // port edited while off: nothing runs to move
    [Arguments(true, true, 17890, 18000, true)]    // port moved while running
    [Arguments(true, false, 17890, 17890, true)]   // switched on, or a failed start retried
    [Arguments(false, true, 17890, 17890, true)]   // switched off
    public async Task Restarts_only_when_needed(bool enabled, bool running, int currentPort, int requestedPort, bool expected)
    {
        await Assert.That(McpServerController.NeedsRestart(enabled, running, currentPort, requestedPort)).IsEqualTo(expected);
    }
}
