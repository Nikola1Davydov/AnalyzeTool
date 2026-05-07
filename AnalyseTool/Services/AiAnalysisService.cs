using AnalyseTool.RevitCommands.Model;
using Microsoft.Extensions.AI;
using OllamaSharp;
using System.Text.Json;

namespace AnalyseTool.Services
{
    internal class AiAnalysisService
    {
        private readonly IChatClient _chat;
        public AiAnalysisService()
        {
            _chat = new OllamaApiClient(
                new Uri("http://localhost:11434"),
                "gemma4:latest");
        }
        public async Task<AiResponce> AnalyzeAsync(List<ParameterData> elements, string userPrompt)
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
                    - Output ONLY the raw JSON array. No markdown, no ```, no explanation before or after.
                    - First character of your response must be '[', last must be ']'.
                    """),

                new(ChatRole.User, $"""
                    Request: {userPrompt}
                    Elements: {JsonSerializer.Serialize(simplified)}
                    """)
            };

            string raw = "";
            await foreach (ChatResponseUpdate item in _chat.GetStreamingResponseAsync(chatHistory))
            {
                raw += item.Text;
            }

            int start = raw.IndexOf('[');
            int end = raw.LastIndexOf(']');
            if (start == -1 || end == -1 || end <= start)
                return new AiResponce(raw, []);

            string json = raw[start..(end + 1)];
            List<ParameterAiEdit> edits = JsonSerializer.Deserialize<List<ParameterAiEdit>>(json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                }) ?? [];

            return new AiResponce(raw, edits);
        }

        public record AiResponce(string Raw, List<ParameterAiEdit> Edits);
        public record ParameterAiEdit(long ElementId, string Parameter, string OldValue, string NewValue, string Reason);
    }
}
