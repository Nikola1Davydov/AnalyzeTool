using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Newtonsoft.Json;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Why an extension is not doing what its author expected. The information already existed —
    /// <see cref="ExtensionDiagnostics"/> has held per-extension load errors all along, and
    /// <c>GetInstalledExtensions</c> surfaces them to the Settings page — but nothing exposed it in a
    /// form an author could ask for, so the answer to "my button disappeared" was a human reading a log.
    ///
    /// The two failures it separates are the ones that look identical from outside:
    /// a script that did not COMPILE (error text, fixable in the source) and a DLL extension with no
    /// build for the running Revit year (compatible=false, nothing wrong with the code).
    ///
    /// Not gated behind the C#-execution toggle, unlike the authoring commands: reading why something
    /// failed to load changes nothing, and it is most needed exactly when authoring is switched off and
    /// a user is trying to work out what is wrong.
    /// </summary>
    [RevitCommand(
        Description = "Reports why extensions are or are not loaded: per extension its kind, zone, " +
                      "enabled/compatible state and the COMPILE ERROR if it has one. Use after " +
                      "SaveAsCommand or ReloadExtensions to find out why a command did not appear — a " +
                      "script that failed to compile and a DLL with no build for this Revit year look " +
                      "the same from outside, and this tells them apart. Read-only and cheap: it reads " +
                      "the extension registry, not the Revit model.",
        ReadOnly = true,
        OutputType = typeof(ExtensionDiagnosticsResult))]
    internal sealed class GetExtensionDiagnostics : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string revitVersion = CoreServices.RevitVersion;

            List<ExtensionDiagnostic> diagnostics = ExtensionCatalog.EnumerateAll(revitVersion)
                .Select(descriptor => new ExtensionDiagnostic(
                    descriptor.Manifest.Id,
                    descriptor.DeclaresDll ? "dll" : descriptor.HasScript ? "script" : "js",
                    descriptor.Zone == ExtensionZone.Dev ? "dev" : "managed",
                    ExtensionStateStore.IsEnabled(descriptor.Manifest.Id),
                    descriptor.IsCompatibleWithHost,
                    descriptor.HasCommands,
                    ExtensionDiagnostics.GetError(descriptor.Manifest.Id),
                    descriptor.Directory))
                .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult<object?>(new ExtensionDiagnosticsResult(
                revitVersion,
                diagnostics.Count,
                diagnostics.Count(d => d.Error != null),
                diagnostics));
        }
    }

    /// <summary>One extension's load state. <see cref="Error"/> is the compile/load failure text, null
    /// when there was none — which is not the same as the extension working: a disabled or incompatible
    /// one has no error either, and says so through its own fields.</summary>
    internal sealed record ExtensionDiagnostic(
        [property: JsonProperty("id")] string Id,
        [property: JsonProperty("kind")] string Kind,
        [property: JsonProperty("zone")] string Zone,
        [property: JsonProperty("enabled")] bool Enabled,
        [property: JsonProperty("compatible")] bool Compatible,
        [property: JsonProperty("hasCommands")] bool HasCommands,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("directory")] string Directory);

    /// <summary><see cref="Failing"/> up front so a caller can tell "nothing is wrong" from
    /// "something is" without walking the list.</summary>
    internal sealed record ExtensionDiagnosticsResult(
        [property: JsonProperty("hostRevit")] string HostRevit,
        [property: JsonProperty("count")] int Count,
        [property: JsonProperty("failing")] int Failing,
        [property: JsonProperty("extensions")] IReadOnlyList<ExtensionDiagnostic> Extensions);
}
