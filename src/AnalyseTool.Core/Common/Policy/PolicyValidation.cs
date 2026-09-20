using AnalyseTool.Core.Common.Extensions;
using Newtonsoft.Json.Linq;
using System.IO;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>
    /// What <c>policy validate</c> checks before a file is rolled out to hundreds of seats: it parses,
    /// the loader's own problems, and the semantic mistakes a coordinator makes most — a lock naming
    /// nothing, a whitelist entry in the wrong form, a source declaring two kinds, a pin that is not a
    /// SHA-256. Pure: reads one file, touches no global state.
    /// </summary>
    internal static class PolicyValidation
    {
        public static IReadOnlyList<string> Validate(string path)
        {
            List<string> problems = new();
            if (!File.Exists(path)) return new[] { $"{path} was not found." };

            PolicyState state = PolicyStore.Load(path);
            problems.AddRange(state.Problems);
            if (state.Origin == PolicyOrigin.Invalid) return problems;
            PolicyDocument doc = state.Document;

            if (doc.IsPointer)
            {
                if (doc.CodeExecution is not null || doc.Extensions is not null || doc.Mcp is not null)
                    problems.Add("A pointer file (policyUrl) should carry nothing else; the settings in it are ignored on seats that follow the pointer.");
                if (doc.SigningKey is not null && PolicySignature.Fingerprint(doc.SigningKey) is null)
                    problems.Add("signingKey is not a base64 SubjectPublicKeyInfo.");
                return problems;
            }

            if (doc.Organization is not null && (string.IsNullOrWhiteSpace(doc.Organization.Name) || string.IsNullOrWhiteSpace(doc.Organization.Contact)))
                problems.Add("organization needs both name and contact (a joinable policy is refused without them).");

            if (doc.MinimumVersion is not null && !Version.TryParse(doc.MinimumVersion, out _))
                problems.Add($"minimumVersion '{doc.MinimumVersion}' is not a version number.");
            if (doc.Update?.Sha256 is not null && PackageHash.Normalize(doc.Update.Sha256) is null)
                problems.Add("update.sha256 is not a 64-character hex SHA-256.");

            ExtensionsPolicy? ext = doc.Extensions;
            if (ext is not null)
            {
                foreach (string feed in ext.AllowedFeeds ?? new List<string>())
                {
                    string n = ExtensionUpdateFeed.Normalize(feed);
                    bool ok = n.StartsWith("github:", StringComparison.OrdinalIgnoreCase)
                              || n.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                              || ExtensionUpdateFeed.IsFilePath(n)
                              || PolicySourceResolver.IsReference(n);
                    if (!ok) problems.Add($"allowedFeeds entry '{feed}' is neither 'github:owner/', an https prefix, a path nor a source: reference.");
                }
                foreach (RequiredExtension r in ext.Required ?? new List<RequiredExtension>())
                {
                    if (string.IsNullOrWhiteSpace(r.Id)) { problems.Add("A required extension has no id."); continue; }
                    if (string.IsNullOrWhiteSpace(r.Source)) problems.Add($"required '{r.Id}' has no source; it can only be updated if a copy with an updateFeed is already installed.");
                    if (r.Sha256 is not null && PackageHash.Normalize(r.Sha256) is null) problems.Add($"required '{r.Id}': sha256 is not a 64-character hex SHA-256.");
                    if (r.Source is not null && ext.AllowedFeeds is not null && !PolicyFeedRules.IsAllowed(ExtensionUpdateFeed.Normalize(r.Source), ext.AllowedFeeds))
                        problems.Add($"required '{r.Id}': its source is not covered by allowedFeeds — the seat would refuse to install it.");
                }
                foreach (string root in ext.Roots ?? new List<string>())
                {
                    if (PolicySourceResolver.IsReference(root))
                    {
                        string name = PolicySourceResolver.Parse(root)!.Value.Name;
                        if (doc.Sources is null || !doc.Sources.ContainsKey(name))
                            problems.Add($"extensions.roots entry '{root}' names a source that is not declared in sources.");
                    }
                    else if (root.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        problems.Add($"extensions.roots entry '{root}' is a URL; roots must be folders.");
                }
                if (ext.CatalogUrl is not null && PolicySourceReader.Validate(ext.CatalogUrl) is string bad)
                    problems.Add($"catalogUrl: {bad}");
            }

            foreach ((string name, PolicySource? source) in doc.Sources ?? new Dictionary<string, PolicySource>())
            {
                if (source is null) { problems.Add($"sources.{name} is empty."); continue; }
                int kinds = (source.Url is null ? 0 : 1) + (source.Path is null ? 0 : 1) + (source.Sharepoint is null ? 0 : 1);
                if (kinds != 1) problems.Add($"sources.{name} must declare exactly one of url, path, sharepoint (found {kinds}).");
                if (source.Url is not null && !source.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    problems.Add($"sources.{name}.url must be https.");
                if (source.Sharepoint is not null && !Uri.TryCreate(source.Sharepoint, UriKind.Absolute, out _))
                    problems.Add($"sources.{name}.sharepoint is not a URL.");
            }

            if (doc.Telemetry is not null)
            {
                string? sink = doc.Telemetry["sink"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(sink)) problems.Add("telemetry.sink is required when the telemetry section exists.");
                else if (sink.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) problems.Add("telemetry.sink must be https or a folder.");
            }

            return problems;
        }
    }
}
