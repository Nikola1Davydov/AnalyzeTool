using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace AnalyseTool.Tests.Ribbon;

/// <summary>
///     The plugin for Claude Code and Codex in <c>source/AnalyseTool/Plugin</c>: its two MCP entries run exactly
///     <c>scripts/headers.ps1</c> (encoded, so no shell or folder can break it) and point at the one address of every Revit.
/// </summary>
public sealed class AiPluginTests
{
    private static readonly string Plugin = Path.Combine(RepositoryRoot(), "source", "AnalyseTool", "Plugin");

    [Test]
    public async Task McpEntries_RunTheHeadersScript_OnTheOneAddress()
    {
        var script = File.ReadAllText(Path.Combine(Plugin, "analysetool", "scripts", "headers.ps1")).Replace("\r\n", "\n");
        var claude = Server("analysetool/.mcp.json");
        var codex = Server("analysetool/codex.mcp.json");

        using (Assert.Multiple())
        {
            await Assert.That(claude["type"]!.GetValue<string>()).IsEqualTo("http");
            await Assert.That(claude["url"]!.GetValue<string>()).IsEqualTo("http://localhost:48075/mcp");
            await Assert.That(codex["url"]!.GetValue<string>()).IsEqualTo("http://localhost:48075/mcp");
            // Regenerate both after a change of the script: [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script)) with LF line ends
            await Assert.That(Decode(claude["headersHelper"]!.GetValue<string>())).IsEqualTo(script);
            await Assert.That(Decode(codex["http_headers_helper"]!.GetValue<string>())).IsEqualTo(script);
        }
    }

    [Test]
    public async Task Manifests_AndMarketplaces_NameTheSamePlugin()
    {
        var claudeMarket = Read(".claude-plugin/marketplace.json");
        var codexMarket = Read(".agents/plugins/marketplace.json");
        using (Assert.Multiple())
        {
            await Assert.That(Read("analysetool/.claude-plugin/plugin.json")["name"]!.GetValue<string>()).IsEqualTo("analysetool");
            await Assert.That(Read("analysetool/.codex-plugin/plugin.json")["name"]!.GetValue<string>()).IsEqualTo("analysetool");
            await Assert.That(Read("analysetool/.codex-plugin/plugin.json")["mcpServers"]!.GetValue<string>()).IsEqualTo("./codex.mcp.json");
            await Assert.That(claudeMarket["plugins"]![0]!["source"]!.GetValue<string>()).IsEqualTo("./analysetool");
            await Assert.That(codexMarket["plugins"]![0]!["source"]!["path"]!.GetValue<string>()).IsEqualTo("./analysetool");
            await Assert.That(Directory.Exists(Path.Combine(Plugin, "analysetool", "skills", "revit"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(Plugin, "analysetool", "skills", "model-tests", "SKILL.md"))).IsTrue();
        }
    }

    private static JsonObject Server(string file) => Read(file)["mcpServers"]!["analysetool"]!.AsObject();

    private static JsonObject Read(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(Plugin, file)))!.AsObject();

    private static string Decode(string command)
    {
        const string marker = "-EncodedCommand ";
        var at = command.IndexOf(marker, StringComparison.Ordinal);
        return Encoding.Unicode.GetString(Convert.FromBase64String(command[(at + marker.Length)..].Trim()));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AnalyseTool.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (AnalyseTool.slnx) was not found above the test folder.");
    }
}
