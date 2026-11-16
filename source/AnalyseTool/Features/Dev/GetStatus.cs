#if DEBUG
using System.Runtime.Loader;
using System.Text;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Lifetime;
using AnalyseTool.Hosting.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Features.Dev;

/// <summary>
///     Debug-only, the «Status» button: where the add-in runs — the load context and the versions of its key dependencies.
///     Used to check the isolation next to other add-ins (docs/manual-tests.md).
/// </summary>
[Command(Description = "Shows the Revit version, the environment and the load context of the add-in and its dependencies.")]
public sealed partial class GetStatus(IHostEnvironment environment, IRevitLifetime revitLifetime, IUserSettings userSettings, ExtensionHost extensions, ILogger<GetStatus> logger)
    : ICommand<string>
{
    /// <inheritdoc />
    public Task<string> ExecuteAsync(CommandContext context)
    {
        var report = new StringBuilder()
            .AppendLine($"Revit: {extensions.RevitVersion}")
            .AppendLine($"Environment: {environment.EnvironmentName}")
            .AppendLine($"Revit initialized: {revitLifetime.IsInitialized}")
            .AppendLine($"User settings: {userSettings.FilePath}")
            .AppendLine($"Load context: {DescribeContext(typeof(GetStatus))}")
            .AppendLine($"Microsoft.Extensions.DependencyInjection: {DescribeAssembly(typeof(ServiceProvider))}")
            .AppendLine($"Microsoft.Extensions.Hosting: {DescribeAssembly(typeof(HostApplicationBuilder))}")
            .AppendLine($"System.Text.Json: {DescribeAssembly(typeof(System.Text.Json.JsonSerializer))}")
            .ToString();
        LogStatus(logger, report);
        return Task.FromResult(report);
    }

    private static string DescribeContext(Type type)
    {
        var context = AssemblyLoadContext.GetLoadContext(type.Assembly);
        return context == AssemblyLoadContext.Default ? "Default (NOT isolated)" : context?.Name ?? "unknown";
    }

    private static string DescribeAssembly(Type type)
    {
        return $"{type.Assembly.GetName().Version} in {DescribeContext(type)}";
    }

    [LoggerMessage(LogLevel.Information, "Status requested:\n{Report}")]
    private static partial void LogStatus(ILogger logger, string report);
}
#endif
