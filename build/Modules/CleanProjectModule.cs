using Build.Options;
using Microsoft.Extensions.Options;
using ModularPipelines.Attributes;
using ModularPipelines.Conditions;
using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Modules;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Clean projects and artifact directories.
/// </summary>
/// <remarks>
///     Unlike Nice3point, skips <c>node_modules</c>: npm packages keep <c>bin</c> folders of their own (<c>.bin</c>,
///     <c>typescript\bin</c>), and deleting them breaks the frontend build.
/// </remarks>
[SkipIf<IsCI>]
public sealed class CleanProjectModule(IOptions<BuildOptions> buildOptions) : SyncModule
{
    protected override void ExecuteModule(IModuleContext context, CancellationToken cancellationToken)
    {
        var rootDirectory = context.Git().RootDirectory;
        var outputDirectory = rootDirectory.GetFolder(buildOptions.Value.OutputDirectory);
        var buildOutputDirectories = rootDirectory
            .GetFolders(folder => folder.Name is "bin" or "obj")
            .Where(folder => folder.Parent != Projects.Build.Directory)
            .Where(folder => !folder.Path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"));

        foreach (var buildFolder in buildOutputDirectories)
        {
            buildFolder.Clean();
        }

        if (outputDirectory.Exists)
        {
            outputDirectory.Clean();
        }
    }
}
