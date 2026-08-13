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
                      "enabled/compatible state, the COMPILE ERROR if it has one, and shadowedBy when " +
                      "another folder claimed the same id first so this copy never runs. Use after " +
                      "SaveAsCommand or ReloadExtensions to find out why a command did not appear, or " +
                      "why an edit had no effect — a script that failed to compile, a DLL with no build " +
                      "for this Revit year and a duplicate id look the same from outside, and this " +
                      "tells them apart. Read-only and cheap: it reads the extension registry, not the " +
                      "Revit model.",
        ReadOnly = true,
        OutputType = typeof(ExtensionDiagnosticsResult))]
    internal sealed class GetExtensionDiagnostics : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string revitVersion = CoreServices.RevitVersion;

            IReadOnlyList<ExtensionDescriptor> found = ExtensionCatalog.EnumerateAll(revitVersion);
            Dictionary<string, string> claimants = Claimants(found);

            List<ExtensionDiagnostic> diagnostics = found
                .Select(descriptor => new ExtensionDiagnostic(
                    descriptor.Manifest.Id,
                    descriptor.DeclaresDll ? "dll" : descriptor.HasScript ? "script" : "js",
                    descriptor.Zone == ExtensionZone.Dev ? "dev" : "managed",
                    ExtensionStateStore.IsEnabled(descriptor.Manifest.Id),
                    descriptor.IsCompatibleWithHost,
                    descriptor.HasCommands,
                    ExtensionDiagnostics.GetError(descriptor.Manifest.Id),
                    descriptor.Directory,
                    ShadowedBy(descriptor, claimants)))
                .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult<object?>(new ExtensionDiagnosticsResult(
                revitVersion,
                diagnostics.Count,
                diagnostics.Count(d => d.Error != null),
                diagnostics.Count(d => d.ShadowedBy != null),
                diagnostics));
        }

        /// <summary>
        /// Which folder actually owns each id: the FIRST one the loader would load, in the order it
        /// walks the roots (managed, the default dev root, then user-added roots as they were added).
        ///
        /// Commands are registered first-wins, so a second folder carrying the same id registers
        /// nothing and its code simply never runs. That is invisible from outside — no load error, no
        /// missing extension, just a command that ignores the edit someone made — and until now it was
        /// visible only as one line in the log.
        /// </summary>
        private static Dictionary<string, string> Claimants(IEnumerable<ExtensionDescriptor> found)
        {
            Dictionary<string, string> claimants = new(StringComparer.OrdinalIgnoreCase);
            foreach (ExtensionDescriptor descriptor in found)
                if (WouldLoad(descriptor) && !claimants.ContainsKey(descriptor.Manifest.Id))
                    claimants[descriptor.Manifest.Id] = descriptor.Directory;
            return claimants;
        }

        /// <summary>The folder that took this id first, or null when this one has it to itself. Only a
        /// folder that would otherwise have loaded can be shadowed: a disabled or incompatible one is
        /// not running for a reason it already reports.</summary>
        private static string? ShadowedBy(ExtensionDescriptor descriptor, Dictionary<string, string> claimants)
        {
            if (!WouldLoad(descriptor)) return null;

            return claimants.TryGetValue(descriptor.Manifest.Id, out string? owner)
                   && !string.Equals(owner, descriptor.Directory, StringComparison.OrdinalIgnoreCase)
                ? owner
                : null;
        }

        /// <summary>Mirrors what <c>ExtensionLoader.LoadAll</c> skips before registering anything.</summary>
        private static bool WouldLoad(ExtensionDescriptor descriptor) =>
            ExtensionStateStore.IsEnabled(descriptor.Manifest.Id)
            && descriptor.IsCompatibleWithHost
            && descriptor.HasCommands;
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
        [property: JsonProperty("directory")] string Directory,
        // Another folder already registered this id, so nothing here runs. Null normally.
        [property: JsonProperty("shadowedBy")] string? ShadowedBy);

    /// <summary><see cref="Failing"/> and <see cref="Shadowed"/> up front so a caller can tell "nothing
    /// is wrong" from "something is" without walking the list — and can tell the two apart, because
    /// they need opposite fixes: one is bad code, the other is good code in the wrong copy.</summary>
    internal sealed record ExtensionDiagnosticsResult(
        [property: JsonProperty("hostRevit")] string HostRevit,
        [property: JsonProperty("count")] int Count,
        [property: JsonProperty("failing")] int Failing,
        [property: JsonProperty("shadowed")] int Shadowed,
        [property: JsonProperty("extensions")] IReadOnlyList<ExtensionDiagnostic> Extensions);
}
