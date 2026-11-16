using ModularPipelines.Context;
using ModularPipelines.Node.Models;
using ModularPipelines.Options;

namespace Build.Modules;

/// <summary>
///     The npm steps shared by the frontend modules.
/// </summary>
internal static class NpmExtensions
{
    /// <summary>
    ///     Installs the packages of the npm project in <paramref name="project" />'s working directory.
    /// </summary>
    /// <remarks>
    ///     <c>npm ci</c> in CI — a clean install from the lock file; <c>npm install</c> locally, because <c>npm ci</c> deletes
    ///     <c>node_modules</c>, which a running Vite dev server keeps open.
    /// </remarks>
    public static async Task NpmInstallAsync(this IModuleContext context, CommandExecutionOptions project, CancellationToken cancellationToken)
    {
        var isCi = bool.TryParse(Environment.GetEnvironmentVariable("CI"), out var ci) && ci;
        CommandLineToolOptions options = isCi ? new NpmCiOptions() : new NpmInstallOptions();

        await context.Shell.Command.ExecuteCommandLineTool(options, project, cancellationToken);
    }
}
