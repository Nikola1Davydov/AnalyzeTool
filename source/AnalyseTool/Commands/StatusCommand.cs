using System.Runtime.Loader;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using AnalyseTool.Hosting.Lifetime;
using AnalyseTool.Hosting.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool.Commands;

/// <summary>
///     Shows where the add-in runs: the load context and the versions of its key dependencies.
///     Used to check the isolation next to other add-ins (docs/manual-tests.md).
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public partial class StatusCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        var logger = Host.GetService<ILogger<StatusCommand>>();
        var environment = Host.GetService<IHostEnvironment>();
        var revitLifetime = Host.GetService<IRevitLifetime>();
        var userSettings = Host.GetService<IUserSettings>();

        var report = new StringBuilder()
            .AppendLine($"Revit: {Application.Application.VersionNumber} ({Application.Application.VersionBuild})")
            .AppendLine($"Environment: {environment.EnvironmentName}")
            .AppendLine($"Revit initialized: {revitLifetime.IsInitialized}")
            .AppendLine($"User settings: {userSettings.FilePath}")
            .AppendLine($"Load context: {DescribeContext(typeof(StatusCommand))}")
            .AppendLine($"Microsoft.Extensions.DependencyInjection: {DescribeAssembly(typeof(ServiceProvider))}")
            .AppendLine($"Microsoft.Extensions.Hosting: {DescribeAssembly(typeof(HostApplicationBuilder))}")
            .AppendLine($"System.Text.Json: {DescribeAssembly(typeof(System.Text.Json.JsonSerializer))}")
            .AppendLine($"Nice3point.Revit.Toolkit: {DescribeAssembly(typeof(ExternalCommand))}")
            .ToString();

        LogStatus(logger, report);
        TaskDialog.Show("AnalyseTool", report);
    }

    private static string DescribeContext(Type type)
    {
        var context = AssemblyLoadContext.GetLoadContext(type.Assembly);
        return context == AssemblyLoadContext.Default ? "Default (NOT isolated)" : context?.Name ?? "unknown";
    }

    private static string DescribeAssembly(Type type)
    {
        var assembly = type.Assembly;
        return $"{assembly.GetName().Version} in {DescribeContext(type)}";
    }

    [LoggerMessage(LogLevel.Information, "Status requested:\n{Report}")]
    private static partial void LogStatus(ILogger logger, string report);
}
