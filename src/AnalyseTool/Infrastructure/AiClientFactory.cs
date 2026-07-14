using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AnalyseTool.Infrastructure
{
    /// <summary>
    /// Turns (providerId, model) into a ready <see cref="IChatClient"/>. The single place that knows
    /// which concrete client backs which provider type — a deliberate factory instead of a DI
    /// container (the host has no composition root and doesn't need one for a two-case switch).
    /// </summary>
    internal static class AiClientFactory
    {
        /// <summary>Null/empty providerId = the built-in local Ollama (back-compat with all pre-provider payloads).</summary>
        public static (IChatClient Client, AiProvider Provider) Create(string? providerId, string model)
        {
            AiProvider provider = AiProviderRegistry.Get(providerId)
                ?? throw new InvalidOperationException($"Unknown AI provider '{providerId}'. Check Settings → AI providers.");

            IChatClient client = provider.Type == AiProviderType.Ollama
                ? new OllamaApiClient(new Uri(provider.BaseUrl), model)
                : new OpenAiCompatibleChatClient(
                    provider.BaseUrl,
                    AiProviderRegistry.GetApiKey(provider),
                    model,
                    TimeSpan.FromSeconds(provider.TimeoutSeconds + 5)); // outer CTS in BuildAnswer fires first

            return (client, provider);
        }
    }
}
