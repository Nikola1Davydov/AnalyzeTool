using AnalyseTool.Common;
using AnalyseTool.Common.Bootstrap;
using AnalyseTool.Common.Extensions;
using AnalyseTool.Sdk;
using System.Diagnostics;
using System.IO;

namespace AnalyseTool.Features.Extensions
{
    /// <summary>Lists every installed extension (compatible or not) for the Settings page.</summary>
    [RevitCommand("GetInstalledExtensions")]
    internal sealed class GetInstalledExtensions : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string hostRevit = HostTag();
            string root = PathProvider.ExtensionsDirectory;

            var extensions = ExtensionCatalog.EnumerateAll(root)
                .Select(d => new
                {
                    id = d.Manifest.Id,
                    displayName = d.Manifest.DisplayName,
                    version = d.Manifest.Version,
                    targetRevit = d.Manifest.TargetRevit,
                    sdkVersion = d.Manifest.SdkVersion,
                    hasCommands = d.HasCommands,
                    hasUi = d.HasUi,
                    compatible = string.Equals(d.Manifest.TargetRevit, hostRevit, StringComparison.OrdinalIgnoreCase),
                    directory = d.Directory,
                })
                .OrderBy(e => e.displayName)
                .ToList();

            return Task.FromResult<object?>(new
            {
                hostRevit,
                extensionsRoot = root,
                extensions,
            });
        }

        internal static string HostTag()
        {
            string version = AnalyseTool.Context.UiApplication.Application.VersionNumber;
            return version.Length >= 4 ? $"R{version.Substring(2)}" : version;
        }
    }

    /// <summary>Opens the extensions folder in Explorer.</summary>
    [RevitCommand("OpenExtensionsFolder")]
    internal sealed class OpenExtensionsFolder : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string folder = PathProvider.ExtensionsDirectory;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            return Task.FromResult<object?>(null);
        }
    }

    /// <summary>Reloads extension command DLLs (collectible ALC) and refreshes the ribbon buttons,
    /// all without restarting Revit.</summary>
    [RevitCommand("ReloadExtensions")]
    internal sealed class ReloadExtensionsCommand : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            AnalyseToolBootstrap.ReloadExtensions();
            RibbonEventHub.Run(uiApp => RibbonHost.RefreshExtensionButtons(GetInstalledExtensions.HostTag()));
            return Task.FromResult<object?>(null);
        }
    }
}
