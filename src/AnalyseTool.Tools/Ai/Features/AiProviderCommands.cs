using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Headers;

namespace AnalyseTool.Tools.Ai
{
    /// <summary>Shared helpers for the AI-provider commands: the wire shape (keys NEVER leave the host —
    /// only a hasKey flag) and the per-type model listing.</summary>
    internal static class AiProviderWire
    {
        public static AiProviderInfo ToWire(AiProvider p) => new(
            p.Id,
            p.DisplayName,
            p.Type == AiProviderType.Ollama ? "ollama" : "openaiCompatible",
            p.BaseUrl,
            !string.IsNullOrEmpty(p.ApiKeyEnc),
            p.TimeoutSeconds,
            p.Id == AiProviderRegistry.OllamaId);

        public static AiProviderInfo[] AllWire() => AiProviderRegistry.All().Select(ToWire).ToArray();

        /// <summary>Model names of one provider; running=false means the endpoint is unreachable (or the
        /// key is rejected) — distinct from "reachable with zero models". Doubles as "test connection".</summary>
        public static async Task<(bool Running, List<string>? Models, string? Error)> ListModels(
            AiProvider provider, CancellationToken ct)
        {
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
                if (provider.Type == AiProviderType.Ollama)
                {
                    string json = await http.GetStringAsync($"{provider.BaseUrl.TrimEnd('/')}/api/tags", ct);
                    List<string> models = JObject.Parse(json)["models"]!
                        .Select(m => m["name"]!.Value<string>()!)
                        .ToList();
                    return (true, models, null);
                }
                else
                {
                    string? key = AiProviderRegistry.GetApiKey(provider);
                    if (!string.IsNullOrEmpty(key))
                        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    string json = await http.GetStringAsync($"{provider.BaseUrl.TrimEnd('/')}/models", ct);
                    List<string> models = (JObject.Parse(json)["data"] as JArray ?? [])
                        .Select(m => m["id"]?.Value<string>())
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => id!)
                        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return (true, models, null);
                }
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }
    }

    [RevitCommand(
        Description = "Returns the configured AI providers (built-in Ollama + user-added OpenAI-compatible " +
                      "endpoints). API keys are never returned — only a hasKey flag. Does not touch the Revit model.",
        ReadOnly = true,
        HiddenFromMcp = true,
        OutputType = typeof(AiProvidersResult))]
    internal sealed class AiGetProviders : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            Task.FromResult<object?>(new AiProvidersResult(true, AiProviderWire.AllWire(), null));
    }

    [RevitCommand(
        Description = "Adds or updates an OpenAI-compatible AI provider. Payload: { id?, displayName, baseUrl " +
                      "(incl. /v1), apiKey? (null keeps the stored key, empty clears it), timeoutSeconds? }. " +
                      "The key is stored DPAPI-encrypted on this machine. Returns { ok, providers, error }.",
        ReadOnly = true,
        InputType = typeof(AiSaveProvider.Request),
        HiddenFromMcp = true,
        OutputType = typeof(AiProvidersResult))]
    internal sealed class AiSaveProvider : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (req is null || string.IsNullOrWhiteSpace(req.DisplayName) || string.IsNullOrWhiteSpace(req.BaseUrl))
                return Task.FromResult<object?>(new AiProvidersResult(false, AiProviderWire.AllWire(), "displayName and baseUrl are required."));
            if (!Uri.TryCreate(req.BaseUrl.Trim(), UriKind.Absolute, out Uri? uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                return Task.FromResult<object?>(new AiProvidersResult(false, AiProviderWire.AllWire(), "baseUrl must be an absolute http(s) URL."));

            AiProviderRegistry.Save(req.Id, req.DisplayName, req.BaseUrl, req.ApiKey, req.TimeoutSeconds);
            return Task.FromResult<object?>(new AiProvidersResult(true, AiProviderWire.AllWire(), null));
        }

        public sealed class Request
        {
            [Description("Existing provider id to update; omit to create a new provider.")]
            public string? Id { get; set; }

            [Description("Display name, e.g. 'OpenRouter'.")]
            public string? DisplayName { get; set; }

            [Description("API base URL including the version segment, e.g. https://openrouter.ai/api/v1.")]
            public string? BaseUrl { get; set; }

            [Description("API key. null = keep the stored key; empty string = remove it.")]
            public string? ApiKey { get; set; }

            [Description("Per-call timeout in seconds (default 120).")]
            public int? TimeoutSeconds { get; set; }
        }
    }

    [RevitCommand(
        Description = "Deletes a user-added AI provider by id (the built-in Ollama cannot be deleted). " +
                      "Payload: { id }. Returns { ok, providers, error }.",
        ReadOnly = true,
        InputType = typeof(AiDeleteProvider.Request),
        HiddenFromMcp = true,
        OutputType = typeof(AiProvidersResult))]
    internal sealed class AiDeleteProvider : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string? id = ctx.Payload.As<Request>()?.Id;
            if (string.IsNullOrWhiteSpace(id))
                return Task.FromResult<object?>(new AiProvidersResult(false, AiProviderWire.AllWire(), "id is required."));

            // Said out loud rather than left as a bare ok=false: the registry refuses for two different
            // reasons, and "no such provider" is a caller's bug where "that one is built in" is not.
            bool ok = AiProviderRegistry.Delete(id);
            string? error = ok
                ? null
                : id == AiProviderRegistry.OllamaId
                    ? "The built-in Ollama provider cannot be deleted."
                    : $"Unknown provider '{id}'.";
            return Task.FromResult<object?>(new AiProvidersResult(ok, AiProviderWire.AllWire(), error));
        }

        public sealed class Request
        {
            [Description("Provider id, as returned by AiGetProviders.")]
            public string? Id { get; set; }
        }
    }

    [RevitCommand(
        Description = "Lists the models of one AI provider (Ollama /api/tags or OpenAI-compatible /models). " +
                      "Payload: { providerId } (omit for the built-in Ollama). Returns { running, models, error } — " +
                      "running=false means unreachable/rejected, which also makes this the 'test connection' call.",
        ReadOnly = true,
        InputType = typeof(AiGetModels.Request),
        HiddenFromMcp = true,
        OutputType = typeof(AiModelsResult))]
    internal sealed class AiGetModels : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            AiProvider? provider = AiProviderRegistry.Get(req?.ProviderId);
            if (provider is null)
                return new AiModelsResult(false, null, $"Unknown provider '{req?.ProviderId}'.");

            (bool running, List<string>? models, string? error) = await AiProviderWire.ListModels(provider, ct);
            return new AiModelsResult(running, models, error);
        }

        public sealed class Request
        {
            [Description("Provider id from AiGetProviders; omit/null for the built-in local Ollama.")]
            public string? ProviderId { get; set; }
        }
    }
}
