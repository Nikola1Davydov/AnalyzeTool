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
                "phi3:mini");
        }
        public async Task<List<ParameterEdit>> AnalyzeAsync(List<ParameterData> elements, string userPrompt)
        {
            List<ChatMessage> chatHistory = new List<ChatMessage>
            {
                new(ChatRole.System, """
                    You are a BIM data quality assistant.
                    Return ONLY a valid JSON array, no extra text.
                    Schema: [{ "elementId": int, "parameter": string,
                               "oldValue": string, "newValue": string,
                               "reason": string }]
                    """),

                new(ChatRole.User, $"""
                    Request: {userPrompt}
                    Elements: {JsonSerializer.Serialize(elements)}
                    """)
            };
            string response = "";

            await foreach (ChatResponseUpdate item in
                _chat.GetStreamingResponseAsync(chatHistory))
            {
                response += item.Text;
            }

            // Clean markdown if model wraps in ```json
            response = response.Trim();
            if (response.StartsWith("```"))
                response = response.Replace("```json", "").Replace("```", "").Trim();

            return JsonSerializer.Deserialize<List<ParameterEdit>>(response) ?? [];
        }
        public record ParameterEdit(int ElementId, string Parameter, string OldValue, string NewValue, string Reason);
    }
}
