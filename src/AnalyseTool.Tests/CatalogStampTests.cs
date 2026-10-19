using AnalyseTool.Mcp.Bridge;

namespace AnalyseTool.Tests;

/// <summary>The catalog stamp tells the MCP exe its tool list is stale. A restarted Revit is a new bridge
/// and must not answer the stamp the old one did — it used to restart at "0:0", so a client that
/// outlived the restart never re-listed.</summary>
public class CatalogStampTests
{
    [Test]
    public async Task A_new_bridge_never_repeats_an_old_bridges_stamp()
    {
        // The queue is never touched by the stamp; a bridge needs one only to execute.
        McpBridgeServer before = new(null!, "token");
        McpBridgeServer after = new(null!, "token");

        using (Assert.Multiple())
        {
            await Assert.That(after.CatalogStamp).IsNotEqualTo(before.CatalogStamp);
            await Assert.That(before.CatalogStamp).IsEqualTo(before.CatalogStamp);
        }
    }
}
