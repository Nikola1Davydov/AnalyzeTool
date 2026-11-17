using System.Runtime.Loader;
using System.Text.Json;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp.Tools;
using AnalyseTool.Hosting.Scripting;
using AnalyseTool.Hosting.Scripting.Compilation;
using AnalyseTool.Hosting.Scripting.Execution;
using AnalyseTool.Hosting.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AnalyseTool.Hosting.Tests;

/// <summary>
///     The script compiler and the registration of AnalyseTool.Hosting.Scripting. Running a script needs Revit: the wrapper
///     takes a <c>UIApplication</c>, so only compilation is tested here.
/// </summary>
public sealed class ScriptingTests
{
    private static readonly Lazy<ScriptCompiler> Compiler = new(() =>
    {
        // The reference assemblies of the Revit API, copied next to the tests by the project
        var revit = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "revit-ref"), "*.dll");
        return new ScriptCompiler(ScriptReferences.Collect(AssemblyLoadContext.Default, revit));
    });

    [Test]
    public async Task Compile_BodyWithRevitApi_Succeeds()
    {
        var compilation = Compiler.Value.Compile("""
            var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount();
            return new { title = doc.Title, walls };
            """);

        await Assert.That(compilation.Errors).IsEmpty();
        await Assert.That(compilation.Succeeded).IsTrue();
    }

    [Test]
    public async Task Compile_BodyWithoutReturn_Succeeds()
    {
        var compilation = Compiler.Value.Compile("TaskDialog.Show(\"Script\", uidoc?.Document.Title ?? \"no document\");");

        await Assert.That(compilation.Succeeded).IsTrue();
    }

    [Test]
    public async Task Compile_Error_PointsAtTheLineOfTheScript()
    {
        var compilation = Compiler.Value.Compile("""
            var count = 1;

            return count + missing;
            """);

        await Assert.That(compilation.Succeeded).IsFalse();
        await Assert.That(compilation.Errors.Single()).StartsWith("script.cs(3,");
    }

    [Test]
    public async Task Compile_LeadingUsings_AreLiftedWithoutShiftingLines()
    {
        var compilation = Compiler.Value.Compile("""
            using System.Text;
            // a comment between the directives
            using Json = System.Text.Json.JsonSerializer;
            var text = new StringBuilder("a").ToString();
            return Json.Serialize(text) + missing;
            """);

        await Assert.That(compilation.Errors.Single()).StartsWith("script.cs(5,");
    }

    [Test]
    public async Task LiftUsings_UsingStatement_StaysCode()
    {
        var (directives, statements) = ScriptCompiler.LiftUsings("""
            using System.IO;
            using (var stream = new MemoryStream())
            {
            }
            """);

        await Assert.That(directives).IsEquivalentTo(["using System.IO;"]);
        await Assert.That(statements).Contains("using (var stream = new MemoryStream())");
        await Assert.That(statements.Split('\n')[0]).IsEmpty();
    }

    [Test]
    public async Task AddScripting_On_RegistersExecuteCodeAsADestructiveMcpTool()
    {
        using var host = BuildHost(enabled: true);
        await host.Services.GetRequiredService<ScriptingSwitch>().StartAsync(CancellationToken.None);

        var registry = host.Services.GetRequiredService<CommandRegistry>();
        var tool = host.Services.GetRequiredService<McpToolCatalog>().Find("ExecuteCode")?.Tool;

        await Assert.That(registry.Find("ExecuteCode")).IsNotNull();
        await Assert.That(tool?.Annotations?.DestructiveHint ?? false).IsTrue();
        await Assert.That(tool!.InputSchema.GetProperty("properties").TryGetProperty("code", out _)).IsTrue();
        await Assert.That(tool.InputSchema.GetProperty("properties").TryGetProperty("dryRun", out _)).IsTrue();
    }

    [Test]
    public async Task Journal_WritesOneJsonLinePerRun()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"scripts-{Guid.NewGuid():N}");
        var journal = new ScriptJournal(directory);
        var time = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

        journal.Write(new ScriptJournalEntry(time, "2025", "Model", DryRun: true, "succeeded", ScriptChanges.None, null, "return 1;"));
        journal.Write(new ScriptJournalEntry(time, null, null, DryRun: false, "compile_failed", null, "script.cs(1,1): error", "retrun 1;"));

        var lines = await File.ReadAllLinesAsync(Path.Combine(directory, $"{time.LocalDateTime:yyyy-MM-dd}.jsonl"));
        await Assert.That(lines.Length).IsEqualTo(2);

        using var first = JsonDocument.Parse(lines[0]);
        await Assert.That(first.RootElement.GetProperty("code").GetString()).IsEqualTo("return 1;");
        await Assert.That(first.RootElement.GetProperty("dryRun").GetBoolean()).IsTrue();

        using var second = JsonDocument.Parse(lines[1]);
        await Assert.That(second.RootElement.GetProperty("outcome").GetString()).IsEqualTo("compile_failed");
        await Assert.That(second.RootElement.TryGetProperty("revit", out _)).IsFalse();
    }

    [Test]
    public async Task AddScripting_Off_RegistersNothing()
    {
        using var host = BuildHost(enabled: false);
        await host.Services.GetRequiredService<ScriptingSwitch>().StartAsync(CancellationToken.None);

        await Assert.That(host.Services.GetRequiredService<CommandRegistry>().Find("ExecuteCode")).IsNull();
    }

    [Test]
    public async Task Switch_TurnsTheCodeToolsOnAndOffWithoutARestart()
    {
        using var host = BuildHost(enabled: false);
        var scripting = host.Services.GetRequiredService<ScriptingSwitch>();
        await scripting.StartAsync(CancellationToken.None);
        var registry = host.Services.GetRequiredService<CommandRegistry>();
        var catalog = host.Services.GetRequiredService<McpToolCatalog>();
        var changed = 0;
        catalog.Changed += (_, _) => changed++;

        scripting.SetEnabled(true);
        await Assert.That(scripting.Enabled).IsTrue();
        await Assert.That(registry.Find("ExecuteCode")).IsNotNull();
        await Assert.That(catalog.Find("DescribeApi")?.Tool.Annotations?.ReadOnlyHint ?? false).IsTrue();
        await Assert.That(catalog.Find("ExecuteCode")?.Tool.Title).IsEqualTo("Execute C# in Revit");

        scripting.SetEnabled(false);
        await Assert.That(scripting.Enabled).IsFalse();
        await Assert.That(registry.Find("ExecuteCode")).IsNull();
        await Assert.That(catalog.Find("ExecuteCode")).IsNull();
        // AI clients hear about both changes (tools/list_changed)
        await Assert.That(changed).IsEqualTo(2);
    }

    private static IHost BuildHost(bool enabled)
    {
        var builder = TestHost.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [ScriptingRegistration.EnabledKey] = enabled.ToString() });
        // The runner needs a dispatcher to be created; these tests never create it. A class implementing IRevitDispatcher
        // would load RevitAPIUI, which has no runtime assembly here — hence a factory.
        builder.Services.AddSingleton(typeof(IRevitDispatcher), _ => throw new NotSupportedException("No Revit in the tests."));
        builder.AddRevitCommands(commands => commands.AddScripting(builder));
        builder.AddMcp(new Mcp.Transport.McpHostSettings("2026", McpTestServer.FreePort(), McpTestServer.NewConnectionFile()));
        return builder.Build();
    }
}
