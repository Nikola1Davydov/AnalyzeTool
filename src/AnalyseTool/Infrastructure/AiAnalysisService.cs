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

        /// <summary>
        /// Suggests new names for a WHOLE batch of families/types in one model round-trip. One call (not
        /// N) matters twice: local models are slow per request, and the model can only apply a CONSISTENT
        /// naming scheme when it sees the full list at once. Returns (id, name) pairs; ids not present in
        /// the answer simply keep their current name (the caller treats missing as unchanged).
        /// </summary>
        public async Task<List<NameSuggestion>> SuggestNamesAsync(List<NameItem> items, string userPrompt)
        {
            List<ChatMessage> chatHistory = new List<ChatMessage>
            {
                new(ChatRole.System, """
                    You rename Revit families and family types in bulk.
                    INPUT: a JSON array where each item has { "Id", "CurrentName", "Context" } and an instruction.
                    OUTPUT: respond with ONLY a raw JSON array (no wrapper object, no markdown).
                    Each element of the array must have exactly these fields:
                      "Id"    — integer, copy from input unchanged
                      "Name"  — string, the new name
                    RULES:
                    - Every input element must appear exactly once, in the same order.
                    - Apply ONE consistent naming scheme across the whole list.
                    - Keep names valid for Revit: avoid the characters \ : { } [ ] | ; < > ? ` ~
                    - Names must be unique within the output.
                    - If the instruction does not clearly change an element's name, return its current name.
                    """),
                new(ChatRole.User, $"""
                    Instruction: {userPrompt}
                    Elements: {JsonSerializer.Serialize(items)}
                    """)
            };

            ChatOptions jsonOptions = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.Json,
                Instructions = "Include every input element in the output (same count)."
            };
            string raw = await BuildAnswer(chatHistory, jsonOptions);

            return ParseArray<NameSuggestion>(raw, s => s is { Id: > 0, Name: not null })
                .Select(s => s with { Name = CleanName(s.Name) })
                .Where(s => s.Name.Length > 0)
                .ToList();
        }

        public record NameItem(long Id, string CurrentName, string Context);
        public record NameSuggestion(long Id, string Name);

        /// <summary>
        /// Reverse-engineers a naming TEMPLATE from one example name plus a sample element's real data.
        /// The AI authors the rule (an editable artifact previewed live in the builder); applying it
        /// stays deterministic. Returns the template plus any abbreviation-dictionary entries the
        /// example implies (e.g. "Alu" for parameter value "Aluminium").
        /// </summary>
        public async Task<TemplateSuggestion?> SuggestTemplateAsync(
            string example, string name, string family, string category, Dictionary<string, string> parameters)
        {
            List<ChatMessage> chatHistory = new List<ChatMessage>
            {
                new(ChatRole.System, """
                    You reverse-engineer naming templates for Revit families and family types.
                    Template syntax — literal text plus tokens in braces:
                      {name} current name · {family} family name · {category} category ·
                      {param:<Parameter Name>} a parameter's value.
                    Modifiers appended with |: abbr (looks the value up in an abbreviation dictionary),
                    upper, lower, clean, nospace. Example: {category|abbr}_{param:Material|abbr}_{param:Width}x{param:Height}

                    Given ONE example of the DESIRED name plus the element's actual data, infer the
                    template that produces the example from the data:
                    - Match example fragments to parameter VALUES. Numbers usually come from dimension
                      parameters; pick the parameter whose value equals the fragment.
                    - Short codes (Möb, Alu, FEN…) are usually abbreviations of the category or a text
                      parameter value: use |abbr in the template and list the mapping in "abbreviations".
                    - Prefer {param:X} tokens over hardcoded literals whenever a fragment matches data.
                    - Keep the example's literal separators (_ x - .) in the template.
                    - Use parameter names EXACTLY as given in the data.

                    OUTPUT: ONLY a raw JSON object, no markdown:
                    { "template": "...", "abbreviations": [ { "full": "<full value>", "abbr": "<short>" } ] }
                    """),
                new(ChatRole.User, $"""
                    Desired example name: {example}
                    Element data: {JsonSerializer.Serialize(new { name, family, category, parameters })}
                    """)
            };

            ChatOptions jsonOptions = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
            string raw = await BuildAnswer(chatHistory, jsonOptions);
            return ParseObject<TemplateSuggestion>(raw);
        }

        /// <summary>Direct object parse, falling back to the first balanced {...} block (models wrap
        /// answers in markdown fences or prose despite instructions).</summary>
        private static T? ParseObject<T>(string json) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, _jsonOptions);
            }
            catch { }

            int objStart = json.IndexOf('{');
            while (objStart >= 0)
            {
                int depth = 0;
                int objEnd = -1;
                for (int j = objStart; j < json.Length; j++)
                {
                    if (json[j] == '{') depth++;
                    else if (json[j] == '}') { depth--; if (depth == 0) { objEnd = j; break; } }
                }
                if (objEnd < 0) return null;
                try
                {
                    T? item = JsonSerializer.Deserialize<T>(json[objStart..(objEnd + 1)], _jsonOptions);
                    if (item is not null) return item;
                }
                catch { }
                objStart = json.IndexOf('{', objStart + 1);
            }
            return null;
        }

        public record AbbreviationEntry(string Full, string Abbr);
        public record TemplateSuggestion(string Template, List<AbbreviationEntry>? Abbreviations);

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
        private static List<ParameterAiEdit> ParseEdits(string json) =>
            ParseArray<ParameterAiEdit>(json, e => e is { ElementId: > 0, NewValue: not null, Reason: not null });

        /// <summary>
        /// Robustly extracts a JSON array of <typeparamref name="T"/> from a model answer: direct array
        /// parse → unwrap {"output":[...]}-style wrappers → per-object scan (keeping items that pass
        /// <paramref name="keep"/>), so partial valid data is never lost.
        /// </summary>
        private static List<T> ParseArray<T>(string json, Func<T, bool> keep)
        {
            // Try direct array parse
            try
            {
                return JsonSerializer.Deserialize<List<T>>(json, _jsonOptions) ?? [];
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
                        return JsonSerializer.Deserialize<List<T>>(extracted, _jsonOptions) ?? [];
                    }
                }
            }
            catch { }

            // Fallback: scan for individual {...} objects and parse each independently
            List<T> results = new List<T>();
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
                    T? item = JsonSerializer.Deserialize<T>(json[objStart..(objEnd + 1)], _jsonOptions);
                    if (item is not null && keep(item))
                        results.Add(item);
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
