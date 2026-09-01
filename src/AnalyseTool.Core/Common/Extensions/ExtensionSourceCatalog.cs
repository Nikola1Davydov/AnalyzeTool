using Newtonsoft.Json;
using Serilog;
using System.IO;
using System.Reflection;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>One recommended extension: where it lives, and where its packages come from.</summary>
    internal sealed record ExtensionCatalogEntry
    {
        [JsonProperty("id")] public string Id { get; init; } = string.Empty;
        [JsonProperty("name")] public string Name { get; init; } = string.Empty;
        [JsonProperty("publisher")] public string? Publisher { get; init; }
        [JsonProperty("description")] public string? Description { get; init; }

        /// <summary>Install source in <see cref="ExtensionUpdateFeed"/> form: <c>github:owner/repo</c>
        /// or an https feed. Absent for a listing that is only a pointer for the reader.</summary>
        [JsonProperty("source")] public string? Source { get; init; }

        /// <summary>The human page — the repository itself. This is what makes the catalog useful
        /// even when nothing can be installed automatically.</summary>
        [JsonProperty("website")] public string? Website { get; init; }

        [JsonProperty("license")] public string? License { get; init; }
        [JsonProperty("tags")] public List<string>? Tags { get; init; }

        /// <summary>Set by the loader, not by the file: this entry came from the user's own catalog.</summary>
        [JsonIgnore] public bool UserSupplied { get; init; }
    }

    /// <summary>What <see cref="ExtensionSourceCatalog.Load"/> found, plus whatever it could
    /// not read. A broken catalog is a note beside a working page, never the page itself:
    /// the shipped file failing is our bug, the user's file failing is a typo, and in both
    /// cases the entries that DID parse are still worth showing.</summary>
    internal sealed record ExtensionCatalogResult(
        IReadOnlyList<ExtensionCatalogEntry> Entries,
        string? Error);

    /// <summary>
    /// The curated list of extension repositories offered in Settings. Two sources, in order:
    /// the list shipped inside the plugin, then <c>%LOCALAPPDATA%\AnalyseTool\catalog.json</c> —
    /// a company can point its own repositories at its people without waiting for a plugin release,
    /// and an entry there with an existing id replaces the shipped one.
    /// <para>
    /// The catalog is a directory, not a store: it carries names and links, and installs go through
    /// the publisher's own release (#48 — AnalyseTool is the courier, never the reviewer).
    /// </para>
    /// </summary>
    internal static class ExtensionSourceCatalog
    {
        private const string ResourceName = "AnalyseTool.Core.Catalog.catalog.json";

        /// <summary>Where a user or company puts their own entries. Reported to the UI so the page
        /// can name the file even when it does not exist yet.</summary>
        public static string UserCatalogPath => Path.Combine(PathProvider.ProfilePath, "catalog.json");

        public static ExtensionCatalogResult Load()
        {
            Dictionary<string, ExtensionCatalogEntry> byId = new(StringComparer.OrdinalIgnoreCase);
            List<string> problems = new();

            foreach (ExtensionCatalogEntry e in Read(ReadShipped, "the shipped catalog", problems))
                byId[e.Id] = e;

            foreach (ExtensionCatalogEntry e in Read(ReadUser, UserCatalogPath, problems))
                byId[e.Id] = e with { UserSupplied = true };

            return new ExtensionCatalogResult(
                byId.Values
                    .OrderBy(e => e.UserSupplied) // shipped first, then the local additions
                    .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                problems.Count == 0 ? null : string.Join(" ", problems));
        }

        private static IEnumerable<ExtensionCatalogEntry> Read(
            Func<IEnumerable<ExtensionCatalogEntry>> read, string what, List<string> problems)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read {What}", what);
                problems.Add($"Could not read {what}: {ex.Message}");
                return Array.Empty<ExtensionCatalogEntry>();
            }
        }

        private static IEnumerable<ExtensionCatalogEntry> ReadShipped()
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
                throw new InvalidOperationException(
                    $"the catalog resource {ResourceName} is missing from the assembly");

            using StreamReader reader = new(stream);
            return Parse(reader.ReadToEnd(), ResourceName).ToList(); // materialise INSIDE the try
        }

        private static IEnumerable<ExtensionCatalogEntry> ReadUser()
        {
            string path = UserCatalogPath;
            if (!File.Exists(path)) return Array.Empty<ExtensionCatalogEntry>();
            return Parse(File.ReadAllText(path), path).ToList();
        }

        private static IEnumerable<ExtensionCatalogEntry> Parse(string json, string origin)
        {
            CatalogFile? file = JsonConvert.DeserializeObject<CatalogFile>(json);
            List<ExtensionCatalogEntry> kept = new();

            foreach (ExtensionCatalogEntry entry in file?.Entries ?? new List<ExtensionCatalogEntry>())
            {
                // An entry without an id cannot be matched against what is installed, and one without
                // a name has nothing to show — both are file bugs, named rather than swallowed.
                if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    Log.Warning("Skipping a catalog entry without id or name in {Origin}.", origin);
                    continue;
                }
                kept.Add(entry);
            }

            return kept;
        }

        private sealed record CatalogFile
        {
            [JsonProperty("entries")] public List<ExtensionCatalogEntry>? Entries { get; init; }
        }
    }
}
