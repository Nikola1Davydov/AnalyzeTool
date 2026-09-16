using AnalyseTool.Core.Common.Policy;
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

        /// <summary>Set by the loader: <c>shipped</c>, <c>policy</c> (the organization's catalog) or <c>user</c>.</summary>
        [JsonIgnore] public string Origin { get; init; } = "shipped";
    }

    /// <summary>What <see cref="ExtensionSourceCatalog.Load"/> found, plus whatever it could
    /// not read. A broken catalog is a note beside a working page, never the page itself:
    /// the shipped file failing is our bug, the user's file failing is a typo, and in both
    /// cases the entries that DID parse are still worth showing.</summary>
    internal sealed record ExtensionCatalogResult(
        IReadOnlyList<ExtensionCatalogEntry> Entries,
        string? Error)
    {
        /// <summary>Where the organization's catalog comes from (<c>extensions.catalogUrl</c>), if any.</summary>
        public string? PolicyCatalogSource { get; init; }

        /// <summary>The policy catalog shown is the cached copy (offline start, or fetch failed).</summary>
        public bool PolicyCatalogFromCache { get; init; }
    }

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

        /// <summary>Disk only — safe on the startup path. The organization's catalog is read from its
        /// cached copy (or the file itself for a path source); <see cref="RefreshPolicyCatalogAsync"/>
        /// is what fetches it.</summary>
        public static ExtensionCatalogResult Load()
        {
            List<string> problems = new();
            IReadOnlyList<ExtensionCatalogEntry> shipped = Read(ReadShipped, "the shipped catalog", problems).ToList();
            IReadOnlyList<ExtensionCatalogEntry> user = Read(ReadUser, UserCatalogPath, problems).ToList();

            string? policySource = PolicyCatalogSource;
            bool fromCache = false;
            IReadOnlyList<ExtensionCatalogEntry> policy = Array.Empty<ExtensionCatalogEntry>();
            if (policySource is not null)
            {
                PolicySourceContent? cached = null;
                try { cached = PolicySourceReader.ReadCached(policySource); }
                catch (Exception ex) { problems.Add($"Could not read the organization catalog: {ex.Message}"); }

                if (cached is not null)
                {
                    fromCache = cached.FromCache;
                    policy = Read(() => Parse(cached.Text, cached.Location).ToList(), "the organization catalog", problems).ToList();
                }
                else if (_lastRefreshProblem is string p)
                    problems.Add(p);
            }

            return new ExtensionCatalogResult(Merge(shipped, policy, user), problems.Count == 0 ? null : string.Join(" ", problems))
            {
                PolicyCatalogSource = policySource,
                PolicyCatalogFromCache = fromCache,
            };
        }

        /// <summary>Merge order shipped → policy → user. A user entry replaces a shipped one with the
        /// same id (as before), but never an organization entry: the user file can add, not remove
        /// or redirect what the company lists. Pure — the unit the tests exercise.</summary>
        internal static IReadOnlyList<ExtensionCatalogEntry> Merge(
            IEnumerable<ExtensionCatalogEntry> shipped,
            IEnumerable<ExtensionCatalogEntry> policy,
            IEnumerable<ExtensionCatalogEntry> user)
        {
            Dictionary<string, ExtensionCatalogEntry> byId = new(StringComparer.OrdinalIgnoreCase);
            foreach (ExtensionCatalogEntry e in shipped) byId[e.Id] = e with { Origin = "shipped" };
            foreach (ExtensionCatalogEntry e in policy) byId[e.Id] = e with { Origin = "policy" };
            foreach (ExtensionCatalogEntry e in user)
            {
                if (byId.TryGetValue(e.Id, out ExtensionCatalogEntry? existing) && existing.Origin == "policy") continue;
                byId[e.Id] = e with { Origin = "user", UserSupplied = true };
            }

            return byId.Values
                .OrderBy(e => e.Origin switch { "shipped" => 0, "policy" => 1, _ => 2 })
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string? _lastRefreshProblem;

        /// <summary><c>extensions.catalogUrl</c> when the policy declares one and its form is acceptable.</summary>
        public static string? PolicyCatalogSource
        {
            get
            {
                string? source = PolicyStore.Current.IsPresent ? PolicyStore.Current.Document.Extensions?.CatalogUrl : null;
                return string.IsNullOrWhiteSpace(source) || PolicySourceReader.Validate(source!) is not null ? null : source!.Trim();
            }
        }

        /// <summary>Fetches the organization's catalog into the cache (conditional GET). Background only.
        /// Never throws: a failure is remembered and shown beside the entries that did load.</summary>
        public static async Task RefreshPolicyCatalogAsync(CancellationToken ct)
        {
            string? source = PolicyCatalogSource;
            if (source is null) { _lastRefreshProblem = null; return; }

            (PolicySourceContent? _, string? problem) = await PolicySourceReader.ReadAsync(source, ct);
            _lastRefreshProblem = problem is null ? null : $"Organization catalog: {problem}";
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
