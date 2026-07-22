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
            // The single version source (also used by CheckUpdate/installer) — NOT the assembly version:
            // this class moved between assemblies during the slice restructuring, and an assembly without
            // its own AssemblyVersion silently reports 1.0.0.0.
            string pluginVersion = SharedData.ToolData.PLUGIN_VERSION;

            var extensions = ExtensionCatalog.EnumerateAll(revitVersion)
                .Select(d => new
                {
                    id = d.Manifest.Id,
                    name = string.IsNullOrWhiteSpace(d.Manifest.Ui?.Button?.Name) ? d.Manifest.Id : d.Manifest.Ui!.Button!.Name,
                    version = d.Manifest.Version,
                    // Manifest v2 vendor metadata (all optional).
                    description = d.Manifest.Description,
                    publisher = d.Manifest.Publisher,
                    website = d.Manifest.Website,
                    supportUrl = d.Manifest.SupportUrl,
                    updateFeed = d.Manifest.UpdateFeed,
                    enabled = ExtensionStateStore.IsEnabled(d.Manifest.Id),
                    hasCommands = d.HasCommands,
                    hasUi = d.HasUi,
                    // "dll" = prebuilt assembly (declared, even if no build for this year),
                    // "script" = Roslyn-compiled .cs, "js" = UI-only.
                    kind = d.DeclaresDll ? "dll" : d.HasScript ? "script" : "js",
                    // False = declared DLL has no build for the running Revit year (never loaded).
                    compatible = d.IsCompatibleWithHost,
                    zone = d.Zone == ExtensionZone.Dev ? "dev" : "managed",
                    legacyLayout = d.IsLegacyLayout,
                    // Which Revit years the extension ships binaries for (current layout only).
                    binaryYears = d.BinaryYears,
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
                extensionsRoot = versionDir, // legacy display value, superseded by managedRoot/devRoot
                managedRoot = ExtensionSources.DefaultRoot,
                devRoot = ExtensionSources.DefaultDevRoot,
                extensions,
            });
        }
    }
}
