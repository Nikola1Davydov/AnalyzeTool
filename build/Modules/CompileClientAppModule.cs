using ModularPipelines.Context;
using ModularPipelines.Modules;
using ModularPipelines.Node.Models;
using ModularPipelines.Options;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Build the pages of ClientApp: <c>npm ci</c> (<c>npm install</c> locally), then <c>npm run build</c> (TypeScript check and Vite).
/// </summary>
public sealed class CompileClientAppModule : Module
{
    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        // The npm commands of ModularPipelines.Node take no working directory, so they run through the shell context
        var clientApp = new CommandExecutionOptions
        {
            WorkingDirectory = Path.Combine(Projects.AnalyseTool.DirectoryName!, "ClientApp")
        };

        await context.NpmInstallAsync(clientApp, cancellationToken);
        await context.Shell.Command.ExecuteCommandLineTool(new NpmRunOptions("build"), clientApp, cancellationToken);
    }
}
