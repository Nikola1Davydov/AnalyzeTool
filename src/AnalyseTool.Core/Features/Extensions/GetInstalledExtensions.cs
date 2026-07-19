using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>Lists every installed extension (compatible or not) for the Settings page.</summary>
    [RevitCommand(
        Description = "Lists every installed extension (compatible or not) with id, version, target Revit and capabilities.",
        ReadOnly = true)]
    internal sealed class GetInstalledExtensions : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string revitVersion = CoreServices.RevitVersion; // year, e.g. "2025"
            string versionDir = ExtensionSources.DefaultVersionDir(revitVersion);

            // Host environment info, surfaced in Settings so authors know what to build against.
            string hostSdkVersion = typeof(IRevitTask).Assembly.GetName().Version?.ToString() ?? "?";
            string pluginVersion = typeof(GetInstalledExtensions).Assembly.GetName().Version?.ToString() ?? "?";

            var extensions = ExtensionCatalog.EnumerateAll(ExtensionSources.ScanDirs(revitVersion))
                .Select(d => new
                {
                    id = d.Manifest.Id,
                    name = string.IsNullOrWhiteSpace(d.Manifest.Ui?.Button?.Name) ? d.Manifest.Id : d.Manifest.Ui!.Button!.Name,
                    version = d.Manifest.Version,
                    hasCommands = d.HasCommands,
                    hasUi = d.HasUi,
                    // "dll" = prebuilt assembly, "script" = Roslyn-compiled .cs, "js" = UI-only.
                    kind = d.HasDll ? "dll" : d.HasScript ? "script" : "js",
                    compileError = ExtensionDiagnostics.GetError(d.Manifest.Id),
                    directory = d.Directory,
                })
                .OrderBy(e => e.name)
                .ToList();

            return Task.FromResult<object?>(new
            {
                hostRevit = revitVersion,
                hostSdkVersion,
                pluginVersion,
                extensionsRoot = versionDir,
                extensions,
            });
        }
    }
}
