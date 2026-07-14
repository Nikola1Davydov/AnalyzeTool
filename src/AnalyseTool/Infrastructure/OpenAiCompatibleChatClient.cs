using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;

namespace AnalyseTool.Infrastructure
{
    /// <summary>
    /// Minimal <see cref="IChatClient"/> over the OpenAI chat-completions protocol — the lingua franca
    /// of OpenAI, OpenRouter, Groq, Mistral, DeepSeek AND local servers (LM Studio, vLLM, llama.cpp).
    /// Hand-rolled instead of Microsoft.Extensions.AI.OpenAI to avoid coupling the pinned
    /// Microsoft.Extensions.AI version to the OpenAI SDK's release cadence; the surface we need is one
    /// endpoint. Non-streaming on purpose: every caller (AiAnalysisService.BuildAnswer) accumulates the
    /// full answer anyway, so streaming would add SSE parsing for zero UX gain. The streaming interface
    /// member simply yields the complete answer once.
    /// </summary>
    internal sealed class OpenAiCompatibleChatClient : IChatClient
    {
        private readonly HttpClient _http;
        private readonly string _model;

        /// <param name="baseUrl">API base INCLUDING version segment, e.g. "https://api.openai.com/v1".</param>
        public OpenAiCompatibleChatClient(string baseUrl, string? apiKey, string model, TimeSpan timeout)
        {
            _model = model;
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = timeout,
            };
            if (!string.IsNullOrEmpty(apiKey))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var body = new JObject
            {
                ["model"] = _model,
                ["stream"] = false,
                ["messages"] = new JArray(BuildMessages(messages, options).Select(m => new JObject
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content,
                })),
            };
            if (options?.ResponseFormat is ChatResponseFormatJson)
                body["response_format"] = new JObject { ["type"] = "json_object" };

            using var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.PostAsync("chat/completions", content, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(FriendlyError((int)response.StatusCode, json));

            string text = JObject.Parse(json)["choices"]?[0]?["message"]?["content"]?.Value<string>() ?? string.Empty;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        private static List<(string Role, string Content)> BuildMessages(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            List<(string, string)> result = new();
            // ChatOptions.Instructions is an extra system directive — protocol has no field for it,
            // so it travels as an additional system message (same effect).
            if (!string.IsNullOrWhiteSpace(options?.Instructions))
                result.Add(("system", options!.Instructions!));
            foreach (ChatMessage m in messages)
            {
                string role = m.Role == ChatRole.System ? "system"
                    : m.Role == ChatRole.Assistant ? "assistant"
                    : "user";
                result.Add((role, m.Text));
            }
            return result;
        }

        /// <summary>Maps the usual endpoint failures to actionable messages (these surface directly in
        /// the UI toast); anything else keeps the raw status + provider text.</summary>
        private static string FriendlyError(int status, string json)
        {
            string detail = ErrorMessage(json);
            return status switch
            {
                429 => "The provider rate-limited the request (429) — free models are heavily shared. " +
                       "Try a paid model, or retry in a moment. " + WithDetail(detail),
                401 or 403 => "The provider rejected the API key (" + status + "). " +
                       "Check the key in Settings → AI providers. " + WithDetail(detail),
                402 => "The provider reports no credits (402) — top up the account. " + WithDetail(detail),
                404 => "Model or endpoint not found (404) — check the model name and that the base URL " +
                       "includes /v1. " + WithDetail(detail),
                _ => $"AI endpoint returned {status}: {detail}",
            };

            static string WithDetail(string d) => string.IsNullOrWhiteSpace(d) ? string.Empty : $"({d})";
        }

        private static string ErrorMessage(string json)
        {
            try
            {
                return JObject.Parse(json)["error"]?["message"]?.Value<string>() ?? json;
            }
            catch
            {
                return json.Length > 300 ? json[..300] : json;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() => _http.Dispose();
    }
}
