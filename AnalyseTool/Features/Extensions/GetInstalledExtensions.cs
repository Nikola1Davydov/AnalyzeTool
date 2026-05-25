using AnalyseTool.Common;
using AnalyseTool.Common.Extensions;
using AnalyseTool.Common.Utils;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Extensions
{
    /// <summary>Lists every installed extension (compatible or not) for the Settings page.</summary>
    [RevitCommand(
        Description = "Lists every installed extension (compatible or not) with id, version, target Revit and capabilities.",
        ReadOnly = true)]
    internal sealed class GetInstalledExtensions : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string hostRevit = CommonUtils.HostRevitTag(Context.UiApplication.Application.VersionNumber);
            string root = PathProvider.ExtensionsDirectory;

            // Host environment info, surfaced in Settings so authors know what to build against.
            string hostSdkVersion = typeof(IRevitTask).Assembly.GetName().Version?.ToString() ?? "?";
            string pluginVersion = typeof(GetInstalledExtensions).Assembly.GetName().Version?.ToString() ?? "?";

            var extensions = ExtensionCatalog.EnumerateAll(root)
                .Select(d => new
                {
                    id = d.Manifest.Id,
                    name = string.IsNullOrWhiteSpace(d.Manifest.Ui?.Button?.Name) ? d.Manifest.Id : d.Manifest.Ui!.Button!.Name,
                    version = d.Manifest.Version,
                    targetRevit = d.Manifest.TargetRevit,
                    hasCommands = d.HasCommands,
                    hasUi = d.HasUi,
                    compatible = string.Equals(d.Manifest.TargetRevit, hostRevit, StringComparison.OrdinalIgnoreCase),
                    directory = d.Directory,
                })
                .OrderBy(e => e.name)
                .ToList();

            return Task.FromResult<object?>(new
            {
                hostRevit,
                hostSdkVersion,
                pluginVersion,
                extensionsRoot = root,
                extensions,
            });
        }
    }
}
