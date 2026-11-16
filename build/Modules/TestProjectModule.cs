using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Modules;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Runs the tests without Revit — AnalyseTool.Hosting.Tests and AnalyseTool.Tests — for each supported Revit
///     configuration. The tests inside Revit (AnalyseTool.RevitTests) need a licensed Revit and run by hand.
/// </summary>
[DependsOn<ResolveConfigurationsModule>]
[DependsOn<CompileProjectModule>]
public sealed class TestProjectModule : Module
{
    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var configurationsResult = await context.GetModule<ResolveConfigurationsModule>();
        var configurations = configurationsResult.ValueOrDefault!;

        foreach (var configuration in configurations)
        {
            foreach (var project in new[] { Projects.AnalyseTool_Hosting_Tests, Projects.AnalyseTool_Tests })
            {
                await context.SubModule($"{project.Name} {configuration}", async () => await context.DotNet().Test(new DotNetTestOptions
                {
                    Project = project.FullName,
                    Configuration = configuration
                }, cancellationToken: cancellationToken));
            }
        }
    }
}
