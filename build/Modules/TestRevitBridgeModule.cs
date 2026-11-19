using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.Modules;
using ModularPipelines.Node.Models;
using ModularPipelines.Options;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Run the Vitest tests of revit-bridge, the page side of AnalyseTool.Hosting.WebView.
/// </summary>
/// <remarks>
///     After <see cref="CompileClientAppModule" />: ClientApp links revit-bridge into its node_modules, and two npm installs at
///     once over the same folder fail with EBUSY. Our tests only, not part of the template.
/// </remarks>
[DependsOn<CompileClientAppModule>]
public sealed class TestRevitBridgeModule : Module
{
    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        // The npm commands of ModularPipelines.Node take no working directory, so they run through the shell context
        var revitBridge = new CommandExecutionOptions
        {
            WorkingDirectory = Path.Combine(Projects.AnalyseTool_Hosting_WebView.DirectoryName!, "revit-bridge")
        };

        await context.NpmInstallAsync(revitBridge, cancellationToken);
        await context.Shell.Command.ExecuteCommandLineTool(new NpmRunOptions("test"), revitBridge, cancellationToken);
    }
}
