using AnalyseTool.RevitCommands.Model;
using Microsoft.Extensions.AI;
using OllamaSharp;
using System.Text;
using System.Text.Json;

namespace AnalyseTool.Services
{
    internal class AiAnalysisService
    {
        private readonly IChatClient _chat;
        public AiAnalysisService(string model)
        {
            _chat = new OllamaApiClient(new Uri("http://localhost:11434"), model);
        }
        private const int AiTimeoutSeconds = 120;

        public async Task<string> AnalyzeAsync(List<ParameterData> elements, string userPrompt)
        {
            List<ChatMessage> chatHistory = new List<ChatMessage>
            {
                new(ChatRole.System, """
                    You are a BIM data quality assistant.
                    """),
                new(ChatRole.User, $"""
                    Request: {userPrompt}
                    Elements: {JsonSerializer.Serialize(elements)}
                    """)
            };

            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(AiTimeoutSeconds));
            StringBuilder stringBuilder = new StringBuilder();
            await foreach (ChatResponseUpdate item in _chat.GetStreamingResponseAsync(chatHistory, cancellationToken: cts.Token))
            {
                if (!string.IsNullOrEmpty(item.Text))
                    stringBuilder.Append(item.Text);
            }
            return stringBuilder.ToString();
        }
        public async Task<AiResponce> AnalyzeAndEditAsync(List<ParameterData> elements, string userPrompt)
        {
            var simplified = elements.Select(e => new
            {
                ElementId = e.ElementId,
                ParameterName = e.Name,
                CurrentValue = e.Value
            }).ToList();

            List<ChatMessage> chatHistory = new List<ChatMessage>
            {
                new(ChatRole.System, """
                    You are a BIM data quality assistant.
                    INPUT: a JSON array where each item has { ElementId, ParameterName, CurrentValue }.
                    OUTPUT: respond with ONLY a raw JSON array (no wrapper object, no key, no markdown).
                    The array must start with '[' and end with ']'.
                    Each element of the array must have exactly these fields:
                      "ElementId"  — integer, copy from input unchanged
                      "Parameter"  — string, copy ParameterName from input unchanged
                      "OldValue"   — string, copy CurrentValue from input unchanged
                      "NewValue"   — string, your suggested new value
                      "Reason"     — string, one sentence explaining the change
                    RULES:
                    - Every input element must appear in the output (same count, same order).
                    - Do NOT wrap the array in any object like {"output":...} or {"result":...}.
                    - Do NOT add any text before or after the array.
                    """),

                new(ChatRole.User, $"""
                    Request: {userPrompt}
                    Elements: {JsonSerializer.Serialize(simplified)}
                    """)
            };

            ChatOptions jsonOptions = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.Json, 
                Instructions = "Include every input element in the output (same count)."
            };

            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(AiTimeoutSeconds));
            StringBuilder stringBuilder = new StringBuilder();
            await foreach (ChatResponseUpdate item in _chat.GetStreamingResponseAsync(chatHistory, jsonOptions, cts.Token))
            {
                if (!string.IsNullOrEmpty(item.Text))
                    stringBuilder.Append(item.Text);
            }
            string raw = stringBuilder.ToString();
            return new AiResponce(raw, ParseEdits(raw));
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        /// <summary>
        /// Tries full-array parse first; on failure falls back to object-by-object extraction
        /// so partial valid data is never lost.
        /// </summary>
        private static List<ParameterAiEdit> ParseEdits(string json)
        {
            // Try direct array parse
            try
            {
                return JsonSerializer.Deserialize<List<ParameterAiEdit>>(json, _jsonOptions) ?? [];
            }
            catch { }

            // Model sometimes wraps the array: {"output":[...]} / {"result":[...]} / {"data":[...]}
            // Find the first '[' inside the JSON and extract everything up to the matching ']'
            try
            {
                int arrayStart = json.IndexOf('[');
                if (arrayStart >= 0)
                {
                    int depth = 0;
                    int arrayEnd = -1;
                    for (int j = arrayStart; j < json.Length; j++)
                    {
                        if (json[j] == '[') depth++;
                        else if (json[j] == ']') { depth--; if (depth == 0) { arrayEnd = j; break; } }
                    }
                    if (arrayEnd > arrayStart)
                    {
                        string extracted = json[arrayStart..(arrayEnd + 1)];
                        return JsonSerializer.Deserialize<List<ParameterAiEdit>>(extracted, _jsonOptions) ?? [];
                    }
                }
            }
            catch { }

            // Fallback: scan for individual {...} objects and parse each independently
            List<ParameterAiEdit> results = new List<ParameterAiEdit>();
            int i = 0;
            while (i < json.Length)
            {
                int objStart = json.IndexOf('{', i);
                if (objStart == -1) break;

                // Walk forward to find matching closing brace
                int depth = 0;
                int objEnd = -1;
                for (int j = objStart; j < json.Length; j++)
                {
                    if (json[j] == '{') depth++;
                    else if (json[j] == '}')
                    {
                        depth--;
                        if (depth == 0) { objEnd = j; break; }
                    }
                }

                if (objEnd == -1) break;

                try
                {
                    ParameterAiEdit? edit = JsonSerializer.Deserialize<ParameterAiEdit>(
                        json[objStart..(objEnd + 1)], _jsonOptions);

                    // Only keep objects that look like real edits
                    if (edit is { ElementId: > 0, NewValue: not null, Reason: not null })
                        results.Add(edit);
                }
                catch { }

                i = objEnd + 1;
            }
            return results;
        }

        public record AiResponce(string Raw, List<ParameterAiEdit> Edits);
        public record ParameterAiEdit(long ElementId, string Parameter, string OldValue, string NewValue, string Reason);
    }
}
