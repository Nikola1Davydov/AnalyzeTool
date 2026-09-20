using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>
    /// The organization policy file (<c>policy.json</c>) as the host reads it. Every section is
    /// optional; a file with only <c>{ "version": 1 }</c> is valid and changes nothing. Unknown keys
    /// are kept in <see cref="Unknown"/> so the loader can name them as warnings instead of dropping
    /// a newer policy silently on an older plugin.
    /// <para>
    /// Design: <c>docs/enterprise-deployment-design.md</c> §2. Phase 1 reads the machine layer only
    /// (<c>%ProgramData%\AnalyseTool\policy.json</c>); the pointer form (<see cref="PolicyUrl"/>) is
    /// recognized and reported, but resolved only from phase 3a on.
    /// </para>
    /// </summary>
    internal sealed class PolicyDocument
    {
        /// <summary>Format version. Only 1 exists.</summary>
        [JsonProperty("version")] public int Version { get; set; } = 1;

        /// <summary>Pointer form: the real policy lives at this URL or path (phase 3a).</summary>
        [JsonProperty("policyUrl")] public string? PolicyUrl { get; set; }

        /// <summary>Pointer form: the user may not leave the organization the pointer names.</summary>
        [JsonProperty("enforced")] public bool? Enforced { get; set; }

        /// <summary>Pointer form: the organization's policy signing key (base64 SubjectPublicKeyInfo,
        /// ECDSA P-256). When known, the policy at <see cref="PolicyUrl"/> must carry a valid
        /// detached signature (<c>policy.json.sig</c>) or it is refused (design §3c).</summary>
        [JsonProperty("signingKey")] public string? SigningKey { get; set; }

        /// <summary>Named locations referenced as <c>source:&lt;name&gt;/&lt;relative&gt;</c> wherever a
        /// path or URL is expected — the one mechanism that makes a SharePoint library addressable
        /// although its local path differs on every seat (design §9).</summary>
        [JsonProperty("sources")] public Dictionary<string, PolicySource>? Sources { get; set; }

        [JsonProperty("organization")] public PolicyOrganization? Organization { get; set; }

        /// <summary>Seats below this plugin version are shown a non-blocking banner (phase 3b).</summary>
        [JsonProperty("minimumVersion")] public string? MinimumVersion { get; set; }

        /// <summary>Where a seat below <see cref="MinimumVersion"/> gets the installer (and its hash).</summary>
        [JsonProperty("update")] public PolicyUpdate? Update { get; set; }

        /// <summary>Dotted setting paths (<see cref="PolicySettings"/>) the user may not change. A value
        /// that is present in the policy but NOT locked is a default: it applies until the user makes
        /// their own choice.</summary>
        [JsonProperty("locked")] public List<string> Locked { get; set; } = new();

        [JsonProperty("codeExecution")] public CodeExecutionPolicy? CodeExecution { get; set; }
        [JsonProperty("extensions")] public ExtensionsPolicy? Extensions { get; set; }
        [JsonProperty("mcp")] public McpPolicy? Mcp { get; set; }

        /// <summary>Keys this plugin version does not know. Reported, never fatal.</summary>
        [JsonExtensionData] public IDictionary<string, JToken>? Unknown { get; set; }

        /// <summary>True for the pointer form: nothing but the pointer is meaningful in the file.</summary>
        public bool IsPointer => !string.IsNullOrWhiteSpace(PolicyUrl);
    }

    /// <summary>One named source. Exactly one of <see cref="Sharepoint"/>, <see cref="Path"/>,
    /// <see cref="Url"/> is expected; <see cref="MarkerId"/> and <see cref="SyncUrl"/> help resolve
    /// and connect a SharePoint library.</summary>
    internal sealed class PolicySource
    {
        /// <summary>Library or folder URL: <c>https://&lt;tenant&gt;.sharepoint.com/sites/&lt;site&gt;/&lt;library&gt;/&lt;folder&gt;</c>.</summary>
        [JsonProperty("sharepoint")] public string? Sharepoint { get; set; }

        /// <summary>UNC share or local folder; <c>%ENV%</c> expanded.</summary>
        [JsonProperty("path")] public string? Path { get; set; }

        /// <summary>https base URL.</summary>
        [JsonProperty("url")] public string? Url { get; set; }

        /// <summary>Fallback: the id inside <c>analysetool-source.json</c> at the folder root.</summary>
        [JsonProperty("markerId")] public string? MarkerId { get; set; }

        /// <summary>The <c>odopen://</c> link SharePoint's Sync button produces — offered when the
        /// library is not synced on this seat.</summary>
        [JsonProperty("syncUrl")] public string? SyncUrl { get; set; }
    }

    internal sealed class PolicyOrganization
    {
        [JsonProperty("name")] public string? Name { get; set; }
        [JsonProperty("contact")] public string? Contact { get; set; }
    }

    internal sealed class PolicyUpdate
    {
        [JsonProperty("downloadUrl")] public string? DownloadUrl { get; set; }
        [JsonProperty("sha256")] public string? Sha256 { get; set; }
    }

    internal sealed class CodeExecutionPolicy
    {
        /// <summary>Whether ad-hoc C# execution (<c>ExecuteRevitCode</c>) is allowed.</summary>
        [JsonProperty("enabled")] public bool? Enabled { get; set; }
    }

    internal sealed class ExtensionsPolicy
    {
        /// <summary>Additional extension source roots (dev zone, not removable). <c>%ENV%</c> expanded;
        /// <c>source:&lt;name&gt;/…</c> references (phase 3a) resolve a named source's per-machine
        /// mount point. Synced folders are fine as roots: assemblies load from a byte copy.</summary>
        [JsonProperty("roots")] public List<string>? Roots { get; set; }

        /// <summary>Company catalog, merged after the shipped one and before the user's (phase 2).</summary>
        [JsonProperty("catalogUrl")] public string? CatalogUrl { get; set; }

        /// <summary>Prefix whitelist for install sources and update feeds, in the normalized form
        /// <c>github:owner/</c> or <c>https://host/path/</c>. Absent = every source is allowed.</summary>
        [JsonProperty("allowedFeeds")] public List<string>? AllowedFeeds { get; set; }

        /// <summary>Extensions every seat must have (phase 2).</summary>
        [JsonProperty("required")] public List<RequiredExtension>? Required { get; set; }

        /// <summary>False refuses the free-form "Install from repository…" paste; catalog entries and
        /// declared update feeds still go through <see cref="AllowedFeeds"/>.</summary>
        [JsonProperty("allowInstallFromRepository")] public bool? AllowInstallFromRepository { get; set; }
    }

    internal sealed class RequiredExtension
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("source")] public string? Source { get; set; }

        /// <summary>SHA-256 of the package zip; when present, a download that does not match is refused.</summary>
        [JsonProperty("sha256")] public string? Sha256 { get; set; }
    }

    internal sealed class McpPolicy
    {
        /// <summary>Whether the in-Revit MCP bridge may run.</summary>
        [JsonProperty("enabled")] public bool? Enabled { get; set; }
    }

    /// <summary>The dotted setting paths a policy can lock. One constant per setting so the policy
    /// file, the stores and the UI agree on the spelling.</summary>
    internal static class PolicySettings
    {
        public const string CodeExecutionEnabled = "codeExecution.enabled";
        public const string ExtensionsRoots = "extensions.roots";
        public const string McpEnabled = "mcp.enabled";

        public static readonly IReadOnlyList<string> All = new[]
        {
            CodeExecutionEnabled, ExtensionsRoots, McpEnabled,
        };
    }
}
