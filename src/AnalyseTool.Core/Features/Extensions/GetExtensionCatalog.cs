using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// The "where do I get extensions" list: the repositories shipped with the plugin plus the
    /// user's own <c>catalog.json</c>, each marked with whether it is already installed. Local
    /// only — the sources are read from disk, nothing is fetched until the user installs.
    /// </summary>
    [RevitCommand(
        Description = "Lists the recommended extension repositories (shipped catalog plus the user's " +
                      "catalog.json) with their links and whether each one is already installed.",
        ReadOnly = true,
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class GetExtensionCatalog : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Dictionary<string, ExtensionDescriptor> installed = ExtensionCatalog
                .EnumerateAll(CoreServices.RevitVersion)
                .GroupBy(d => d.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            ExtensionCatalogResult catalog = ExtensionSourceCatalog.Load();

            object[] entries = catalog.Entries.Select(e =>
            {
                installed.TryGetValue(e.Id, out ExtensionDescriptor? d);
                return (object)new
                {
                    id = e.Id,
                    name = e.Name,
                    publisher = e.Publisher,
                    description = e.Description,
                    source = e.Source,
                    website = e.Website,
                    license = e.License,
                    tags = e.Tags ?? new List<string>(),
                    userSupplied = e.UserSupplied,
                    installed = d is not null,
                    installedVersion = d?.Manifest.Version,
                    // A dev-zone hit is the author's own working copy: offering "install" there would
                    // drop a packaged copy next to it and leave two extensions with one id.
                    zone = d is null ? null : d.Zone.ToString().ToLowerInvariant(),
                };
            }).ToArray();

            return Task.FromResult<object?>(new
            {
                entries,
                userCatalogPath = ExtensionSourceCatalog.UserCatalogPath,
                // A catalog file that failed to parse is reported BESIDE the entries that did,
                // not as a failed command: one broken file must not cost the whole page.
                error = catalog.Error,
            });
        }
    }
}
