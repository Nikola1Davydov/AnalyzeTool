using System.IO;
using AnalyseTool.Extensions;
using AnalyseTool.Extensions.Building;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Extensions.Testing;
using AnalyseTool.Extensions.Versions;
using AnalyseTool.Hosting.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnalyseTool.Tests.Extensions;

/// <summary>A profile in a temp folder with the extension services over it — no Revit, no user profile touched.</summary>
internal sealed class ExtensionTestEnvironment : IDisposable
{
    public const string RevitVersion = "2025";

    static ExtensionTestEnvironment()
    {
        // The compiler needs the Revit API as metadata: here only the reference assemblies copied next to the tests exist
        ExtensionCompiler.ExtraReferenceFiles = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "revit-ref"), "*.dll");
        RevitStubs.Install();
    }

    public ExtensionTestEnvironment()
    {
        Root = Path.Combine(Path.GetTempPath(), "AnalyseTool.Tests", Guid.NewGuid().ToString("N"));
        Paths = new ExtensionPaths(Root);
        Directory.CreateDirectory(Paths.DevRoot);
        Catalog = new ExtensionCatalog(Paths, RevitVersion);
        Store = new ExtensionStateStore(Paths);
        History = new LibGit2ExtensionHistory();
        var (commands, _) = CommandScanner.Scan([], _ => false);
        Registry = new CommandRegistry(commands, []);
        Tests = new ModelTestCatalog(Paths);
        Host = new ExtensionHost(Catalog, Store, History, Registry, Tests, NullLogger<ExtensionHost>.Instance);
        Manager = new ExtensionManager(Host, Catalog, Paths, Store, History);
    }

    public string Root { get; }

    public ExtensionPaths Paths { get; }

    public ExtensionCatalog Catalog { get; }

    public ExtensionStateStore Store { get; }

    public IExtensionHistory History { get; }

    public CommandRegistry Registry { get; }

    public ModelTestCatalog Tests { get; }

    public ExtensionHost Host { get; }

    public ExtensionManager Manager { get; }

    /// <summary>A command that needs no Revit: returns what it got and a constant.</summary>
    public static string EchoCommand(string className = "Echo", string answer = "42")
    {
        return $$"""
                 using AnalyseTool.Sdk;

                 [RevitCommand(Description = "Echoes the payload.", ReadOnly = true)]
                 public sealed class {{className}} : IRevitTask
                 {
                     public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
                     {
                         return Task.FromResult<object?>(new { answer = {{answer}}, got = revitContext.Payload.RawJson });
                     }
                 }
                 """;
    }

    public void Dispose()
    {
        Host.Dispose();
        try
        {
            // git makes its objects read-only
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
