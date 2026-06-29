using AnalyseTool.Infrastructure.Model;
using Microsoft.Extensions.AI;
using OllamaSharp;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AnalyseTool.Infrastructure
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
                    You are a BIM data quality assistant specializing in Revit parameter analysis.

                    You receive a list of Revit elements, each with the following fields:
                    - elementId: unique Revit element ID
                    - name: parameter name
                    - value: current parameter value (may be empty or null)
                    - level: the building level the element is placed on
                    - storageType: data type (String, Integer, Double, ElementId)
                    - isTypeParameter: true if it's a type parameter, false if instance
                    - isReadOnly: true if the parameter cannot be edited
                    - origin: parameter origin (BuiltIn, Shared, Family)

                    Analyze the data based on the user's request.
                    Be concise and specific. Focus on actionable insights.
                    If values are missing or inconsistent, highlight them clearly.
                    Respond in the same language as the user's request.
                    """),
                new(ChatRole.User, $"""
                    Request: {userPrompt}
                    Elements: {JsonSerializer.Serialize(elements)}
                    """)
            };

            string raw = await BuildAnswer(chatHistory);

            return raw;
        }
        /// <summary>
        /// Suggests a single new name for a Revit family or family type from its current name plus a
        /// free-text instruction. Returns just the cleaned name (one line, no quotes/markdown). Used by
        /// Family Control's Rename dialog AI mode.
        /// </summary>
        public async Task<string> SuggestNameAsync(string currentName, string context, string userPrompt)
        {
            List<ChatMessage> chatHistory = new List<ChatMessage>
            {
                new(ChatRole.System, """
                    You rename Revit families and family types.
                    Given the current name and an instruction, return ONLY the new name.
                    - Output a single line containing just the name — no quotes, no markdown, no
                      explanation, no trailing punctuation.
                    - Keep it a valid Revit name: avoid the characters \ : { } [ ] | ; < > ? ` ~
                    - If the instruction does not clearly change the name, return the current name unchanged.
                    """),
                new(ChatRole.User, $"""
                    Current name: {currentName}
                    Context: {context}
                    Instruction: {userPrompt}
                    """)
            };

            string raw = await BuildAnswer(chatHistory);
            return CleanName(raw);
        }

        /// <summary>Takes the model's first non-empty line and strips wrapping quotes/backticks/asterisks.</summary>
        private static string CleanName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string line = raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            return line.Trim().Trim('"', '\'', '`', '*').Trim();
        }

        public async Task<AiResponse> AnalyzeAndEditAsync(List<ParameterData> elements, string userPrompt)
        {
            var simplified = elements.Select(e => new
            {
                e.ElementId,
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
            string raw = await BuildAnswer(chatHistory, jsonOptions);

            return new AiResponse(raw, ParseEdits(raw));
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };
        private async Task<string> BuildAnswer(List<ChatMessage> chatHistory, ChatOptions chatOptions = default)
        {
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(AiTimeoutSeconds));
            StringBuilder stringBuilder = new StringBuilder();
            try
            {
                await foreach (ChatResponseUpdate item in _chat.GetStreamingResponseAsync(chatHistory, chatOptions, cts.Token))
                {
                    if (!string.IsNullOrEmpty(item.Text))
                        stringBuilder.Append(item.Text);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException("Cloud model requires Ollama login. Run 'ollama login' in terminal.");
            }
            return stringBuilder.ToString();
        }

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

        public record AiResponse(string Raw, List<ParameterAiEdit> Edits);
        public record ParameterAiEdit(long ElementId, string Parameter, string OldValue, string NewValue, string Reason);
    }
}
