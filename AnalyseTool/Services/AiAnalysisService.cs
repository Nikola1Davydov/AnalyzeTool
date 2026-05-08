using AnalyseTool.RevitCommands.Model;
using Microsoft.Extensions.AI;
using OllamaSharp;
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
            string raw = "";
            await foreach (ChatResponseUpdate item in _chat.GetStreamingResponseAsync(chatHistory))
            {
                raw += item.Text;
            }
            return raw;
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
                    OUTPUT: a JSON array where each item has exactly these fields:
                      "ElementId"   — copy from input, do not change
                      "Parameter"   — copy ParameterName from input, do not change
                      "OldValue"    — copy CurrentValue from input, do not change
                      "NewValue"    — your suggested new value (string)
                      "Reason"      — one sentence why you changed it (string)
                    STRICT RULES:
                    - Include every input element in the output (same count).
                    """),

                new(ChatRole.User, $"""
                    Request: {userPrompt}
                    Elements: {JsonSerializer.Serialize(simplified)}
                    """)
            };

            ChatOptions jsonOptions = new ChatOptions 
            { 
                ResponseFormat = ChatResponseFormat.Json, 
                Instructions= "Include every input element in the output (same count)." 
            };

            string raw = "";
            await foreach (ChatResponseUpdate item in _chat.GetStreamingResponseAsync(chatHistory, jsonOptions))
            {
                raw += item.Text;
            }

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
            try
            {
                return JsonSerializer.Deserialize<List<ParameterAiEdit>>(json, _jsonOptions) ?? [];
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
