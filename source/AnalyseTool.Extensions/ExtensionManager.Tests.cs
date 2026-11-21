using System.IO;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Testing;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Extensions;

public sealed partial class ExtensionManager
{
    /// <summary>
    ///     Creates a folder of model tests: <c>plugin.json</c> with no buttons and one suite in <c>src\</c> that AnalyseTool
    ///     compiles like any button's code; its history starts and it loads at once.
    /// </summary>
    public async Task<ExtensionState> CreateTestSuiteAsync(string name, string? id, string? description, string? root, CancellationToken cancellationToken)
    {
        // «<id>.tests»: a folder of model tests says so on disk, as a tab says «.tab»; the id (plugin.json) has no ending
        var (directory, folderId, trimmed) = NewFolder(name, id, root, ExtensionCatalog.TestsSuffix);
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

    /// <summary>
    ///     Adds a suite of model tests to an existing folder of the user — a project holds as many suites as it likes: one more
    ///     C# file with the example tests, saved as a version, the folder reloaded.
    /// </summary>
    public async Task<ExtensionState> AddTestSuiteAsync(string id, string name, CancellationToken cancellationToken)
    {
        var descriptor = Own(id);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, "A name is required.");
        }

        if (descriptor.Kind != ExtensionKind.Source)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, $"'{id}' is built as a DLL by its author: add the suite to its project.");
        }

        if (!ModelTestCatalog.IsTestsFolder(descriptor.Directory))
        {
            throw new CommandException(ExtensionErrorCodes.Invalid,
                $"'{id}' is not a folder of model tests (its name ends in «{ExtensionCatalog.TestsSuffix}»): create one with CreateModelTestSuite.");
        }

        var trimmed = name.Trim();
        var folder = Path.Combine(descriptor.Directory, ExtensionCatalog.SourceFolder);
        var source = Path.Combine(folder, ExtensionScaffold.ToClassName(trimmed) + ".cs");
        if (File.Exists(source))
        {
            throw new CommandException(ExtensionErrorCodes.ExtensionExists, $"'{id}' has a file {Path.GetFileName(source)} already: choose another name.");
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(source, ExtensionScaffold.TestSuite(trimmed));
        SaveChange(descriptor, $"Added the model tests «{trimmed}»");
        return await host.ReloadAsync(id, cancellationToken).ConfigureAwait(false) ?? Get(id);
    }
}
