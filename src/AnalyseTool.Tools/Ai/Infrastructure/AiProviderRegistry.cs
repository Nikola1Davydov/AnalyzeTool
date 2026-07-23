using Newtonsoft.Json;
using Serilog;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AnalyseTool.Tools.Ai
{
    public enum AiProviderType
    {
        Ollama,
        OpenAiCompatible,
    }

    /// <summary>One configured AI endpoint. For OpenAI-compatible providers <see cref="BaseUrl"/> is the
    /// API base INCLUDING the version segment (e.g. "https://api.openai.com/v1",
    /// "https://openrouter.ai/api/v1", "http://localhost:1234/v1") — chat = BaseUrl/chat/completions.</summary>
    public sealed class AiProvider
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("displayName")] public string DisplayName { get; set; } = string.Empty;
        [JsonProperty("type")] public AiProviderType Type { get; set; }
        [JsonProperty("baseUrl")] public string BaseUrl { get; set; } = string.Empty;
        /// <summary>DPAPI-encrypted (CurrentUser) API key, base64. Never leaves the host machine/user.</summary>
        [JsonProperty("apiKeyEnc")] public string? ApiKeyEnc { get; set; }
        [JsonProperty("timeoutSeconds")] public int TimeoutSeconds { get; set; } = 120;
    }

    /// <summary>
    /// The configured AI providers: the built-in local Ollama plus user-added OpenAI-compatible
    /// endpoints (OpenAI, OpenRouter, Groq, Mistral, LM Studio, vLLM…). Persisted in
    /// ai-providers.json under the profile folder (same pattern as mcp.json). API keys are stored
    /// DPAPI-encrypted per Windows user and are NEVER handed to the frontend — commands expose only a
    /// hasKey flag. Cloud use is therefore an explicit per-provider opt-in with the user's own key.
    /// </summary>
    internal static class AiProviderRegistry
    {
        public const string OllamaId = "ollama";
        public const int DefaultTimeoutSeconds = 120;

        private static readonly object _gate = new();
        private static List<AiProvider>? _custom;

        // Local (not Core's PathProvider): Tools references only the Sdk, and this settings file is
        // a private concern of the AI feature — same %LOCALAPPDATA%\<plugin> root by construction.
        private static string ProfilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SharedData.ToolData.PLUGIN_NAME);

        private static string SettingsFile => Path.Combine(ProfilePath, "ai-providers.json");

        /// <summary>The always-present local provider. Not persisted; not deletable.</summary>
        public static AiProvider Ollama { get; } = new()
        {
            Id = OllamaId,
            DisplayName = "Ollama (local)",
            Type = AiProviderType.Ollama,
            BaseUrl = "http://localhost:11434",
            TimeoutSeconds = DefaultTimeoutSeconds,
        };

        public static IReadOnlyList<AiProvider> All()
        {
            lock (_gate) return [Ollama, .. Custom()];
        }

        /// <summary>Null/empty id falls back to the built-in Ollama — every pre-provider payload keeps working.</summary>
        public static AiProvider? Get(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || id == OllamaId) return Ollama;
            lock (_gate) return Custom().FirstOrDefault(p => p.Id == id);
        }

        /// <summary>Adds or updates a custom provider. A null apiKey KEEPS the stored key (so the
        /// frontend can edit name/url without ever seeing the key); an empty string clears it.</summary>
        public static AiProvider Save(string? id, string displayName, string baseUrl, string? apiKey, int? timeoutSeconds)
        {
            lock (_gate)
            {
                List<AiProvider> list = Custom();
                AiProvider? existing = list.FirstOrDefault(p => p.Id == id);
                AiProvider p = existing ?? new AiProvider { Id = $"ai-{Guid.NewGuid():N}"[..11] };

                p.Type = AiProviderType.OpenAiCompatible;
                p.DisplayName = displayName.Trim();
                p.BaseUrl = baseUrl.Trim().TrimEnd('/');
                if (timeoutSeconds is > 0 and <= 3600) p.TimeoutSeconds = timeoutSeconds.Value;
                if (apiKey is not null)
                    p.ApiKeyEnc = apiKey.Length == 0 ? null : Protect(apiKey);

                if (existing is null) list.Add(p);
                Persist(list);
                return p;
            }
        }

        public static bool Delete(string id)
        {
            if (id == OllamaId) return false;
            lock (_gate)
            {
                List<AiProvider> list = Custom();
                int removed = list.RemoveAll(p => p.Id == id);
                if (removed > 0) Persist(list);
                return removed > 0;
            }
        }

        /// <summary>Decrypted API key for host-side use only. Never expose through a command result.</summary>
        public static string? GetApiKey(AiProvider provider)
        {
            if (string.IsNullOrEmpty(provider.ApiKeyEnc)) return null;
            try
            {
                byte[] enc = Convert.FromBase64String(provider.ApiKeyEnc);
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex)
            {
                // Wrong user/machine or corrupted entry — treat as "no key" instead of failing the call.
                Log.Warning(ex, "Could not decrypt the API key of AI provider {Provider}", provider.Id);
                return null;
            }
        }

        private static string Protect(string apiKey) =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser));

        private static List<AiProvider> Custom()
        {
            if (_custom is not null) return _custom;
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var loaded = JsonConvert.DeserializeObject<List<AiProvider>>(File.ReadAllText(SettingsFile));
                    _custom = (loaded ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Id) && p.Id != OllamaId).ToList();
                    return _custom;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load ai-providers.json — starting with no custom AI providers");
            }
            _custom = [];
            return _custom;
        }

        private static void Persist(List<AiProvider> list)
        {
            try
            {
                Directory.CreateDirectory(ProfilePath);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(list, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to persist ai-providers.json");
            }
        }
    }
}
