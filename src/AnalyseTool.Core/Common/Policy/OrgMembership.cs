using Newtonsoft.Json;
using Serilog;
using System.IO;

namespace AnalyseTool.Core.Common.Policy
{
    /// <summary>
    /// The organization layer's user-side record (<c>%LOCALAPPDATA%\AnalyseTool\org.json</c>, design §8):
    /// which policy this seat joined, the last copy of it (so an offline start keeps the
    /// configuration), how it was verified, and whether the machine pointer forbids leaving.
    /// </summary>
    internal sealed class OrgMembership
    {
        /// <summary>The policy's https URL, path, or a <c>source:</c> reference (resolved at read time).</summary>
        [JsonProperty("policyUrl")] public string PolicyUrl { get; set; } = string.Empty;

        /// <summary>Set when the membership comes from the machine pointer with <c>enforced: true</c>.</summary>
        [JsonProperty("enforced")] public bool Enforced { get; set; }

        /// <summary>base64 SubjectPublicKeyInfo of the organization's signing key, when known.</summary>
        [JsonProperty("signingKey")] public string? SigningKey { get; set; }

        [JsonProperty("organizationName")] public string? OrganizationName { get; set; }
        [JsonProperty("fetchedAt")] public DateTimeOffset? FetchedAt { get; set; }

        /// <summary>The policy text as last fetched and accepted (verified when a key is known).</summary>
        [JsonProperty("cachedPolicy")] public string? CachedPolicy { get; set; }

        /// <summary>Where the last fetch actually came from (after redirects); a host change asks again.</summary>
        [JsonProperty("lastLocation")] public string? LastLocation { get; set; }

        [JsonProperty("lastSignature")] public string? LastSignature { get; set; }

        [JsonIgnore] public bool HasCachedPolicy => !string.IsNullOrWhiteSpace(CachedPolicy);
    }

    /// <summary>Reads and writes <c>org.json</c>. Local disk only.</summary>
    internal static class OrgMembershipStore
    {
        private static readonly object Gate = new();
        public static string FilePath => Path.Combine(PathProvider.ProfilePath, "org.json");

        public static OrgMembership? Load()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(FilePath)) return null;
                    OrgMembership? m = JsonConvert.DeserializeObject<OrgMembership>(File.ReadAllText(FilePath));
                    return string.IsNullOrWhiteSpace(m?.PolicyUrl) ? null : m;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not read {File}; treating this seat as not joined", FilePath);
                    return null;
                }
            }
        }

        public static void Save(OrgMembership membership)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(PathProvider.ProfilePath);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(membership, Formatting.Indented));
            }
        }

        public static void Delete()
        {
            lock (Gate)
            {
                try { if (File.Exists(FilePath)) File.Delete(FilePath); }
                catch (Exception ex) { Log.Warning(ex, "Could not delete {File}", FilePath); }
            }
        }
    }
}
