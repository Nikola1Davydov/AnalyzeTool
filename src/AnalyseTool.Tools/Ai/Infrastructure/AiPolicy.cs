using AnalyseTool.Sdk;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Ai
{
    /// <summary>The <c>ai</c> section of the organization policy, read through <see cref="HostPolicy"/>
    /// (Tools references only the Sdk). Providers listed here are managed: shown, usable, not
    /// editable or deletable on the seat; their key comes from an environment variable the IT
    /// department sets, or from a gateway that holds the real key.</summary>
    internal sealed class AiPolicy
    {
        [JsonProperty("providers")] public List<AiPolicyProvider>? Providers { get; set; }

        /// <summary>False hides the user's own providers and refuses new ones. Nothing is deleted:
        /// leaving the organization brings them back.</summary>
        [JsonProperty("allowUserProviders")] public bool? AllowUserProviders { get; set; }

        public static AiPolicy? Current => HostPolicy.GetSection<AiPolicy>("ai");

        public static bool UserProvidersAllowed => Current?.AllowUserProviders != false;

        /// <summary>Environment variables a policy may name for an API key must start with this.</summary>
        public const string KeyEnvPrefix = "ANALYSETOOL_";

        /// <summary>https anywhere, or plain http on the local machine only.</summary>
        internal static bool IsAcceptableEndpoint(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? u)) return false;
            if (string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(u.Scheme, "http", StringComparison.OrdinalIgnoreCase) && u.IsLoopback;
        }

        public static IReadOnlyList<AiProvider> ManagedProviders()
        {
            List<AiProvider> result = new();
            foreach (AiPolicyProvider p in Current?.Providers ?? new List<AiPolicyProvider>())
            {
                if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.BaseUrl)) continue;
                // A managed provider receives a bearer token read from the environment: the endpoint must
                // be https (or the local machine), and the variable must be one meant for this plugin —
                // a policy must not be able to name OPENAI_API_KEY and ship it to an arbitrary host.
                if (!IsAcceptableEndpoint(p.BaseUrl)) continue;
                if (!string.IsNullOrWhiteSpace(p.ApiKeyEnv) && !p.ApiKeyEnv.Trim().StartsWith(KeyEnvPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new AiProvider
                {
                    Id = p.Id.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(p.Name) ? p.Id.Trim() : p.Name.Trim(),
                    Type = string.Equals(p.Type, "ollama", StringComparison.OrdinalIgnoreCase) ? AiProviderType.Ollama : AiProviderType.OpenAiCompatible,
                    BaseUrl = p.BaseUrl.Trim().TrimEnd('/'),
                    TimeoutSeconds = p.TimeoutSeconds is > 0 and <= 3600 ? p.TimeoutSeconds.Value : AiProviderRegistry.DefaultTimeoutSeconds,
                    ApiKeyEnv = string.IsNullOrWhiteSpace(p.ApiKeyEnv) ? null : p.ApiKeyEnv.Trim(),
                    Managed = true,
                });
            }
            return result;
        }
    }

    internal sealed class AiPolicyProvider
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("name")] public string? Name { get; set; }
        /// <summary><c>openaiCompatible</c> (default) or <c>ollama</c>.</summary>
        [JsonProperty("type")] public string? Type { get; set; }
        [JsonProperty("baseUrl")] public string BaseUrl { get; set; } = string.Empty;
        /// <summary>Environment variable holding the API key (set by GPO / login script). Omit when
        /// <see cref="BaseUrl"/> is a gateway that holds the key itself.</summary>
        [JsonProperty("apiKeyEnv")] public string? ApiKeyEnv { get; set; }
        [JsonProperty("timeoutSeconds")] public int? TimeoutSeconds { get; set; }
    }
}
