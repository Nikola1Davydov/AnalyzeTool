using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Modules;
using ModularPipelines.Options;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Check the project boundaries: references between projects, headless AnalyseTool.Hosting, no
///     ActiveUIDocument in the add-in's Features/ and Services/, no add-in types in the Launcher (build/Check-Boundaries.ps1).
/// </summary>
/// <remarks>Runs on every pipeline run, in CI as well; a violation fails the pipeline.</remarks>
public sealed class CheckBoundariesModule : Module
{
    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var script = Path.Combine(Projects.Build.DirectoryName!, "Check-Boundaries.ps1");

        await context.Shell.Command.ExecuteCommandLineTool(
            new GenericCommandLineToolOptions("pwsh")
            {
                Arguments = ["-NoProfile", "-NonInteractive", "-File", script, "-Root", context.Git().RootDirectory.Path]
            }, cancellationToken: cancellationToken);
    }
}
