using System.IO;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;

namespace AnalyseTool.Extensions;

public sealed partial class ExtensionManager
{
    /// <summary>
    ///     Creates a folder of model tests: <c>plugin.json</c> with no buttons and one suite in <c>src\</c> that AnalyseTool
    ///     compiles like any button's code; its history starts and it loads at once.
    /// </summary>
    public async Task<ExtensionState> CreateTestSuiteAsync(string name, string? id, string? description, string? root, CancellationToken cancellationToken)
    {
        var (directory, folderId, trimmed) = NewFolder(name, id, root);
        ExtensionScaffold.CreateFolder(directory, folderId, trimmed, description ?? $"Model tests: {trimmed}", null);
        var source = Path.Combine(directory, ExtensionCatalog.SourceFolder, ExtensionScaffold.ToClassName(trimmed) + ".cs");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, ExtensionScaffold.TestSuite(trimmed));
        if (history.IsAvailable)
        {
            history.Init(directory, Author, $"Created the model tests «{trimmed}»", ExtensionScaffold.SourceIgnore);
        }

        await host.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        return Get(folderId);
    }
}
