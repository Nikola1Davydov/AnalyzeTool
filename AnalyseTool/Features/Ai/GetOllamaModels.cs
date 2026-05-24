using AnalyseTool.Sdk;
using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace AnalyseTool.Features.Ai
{
    [RevitCommand(
        Description = "Returns the list of locally available Ollama model names (for the AI features). " +
                      "Does not touch the Revit model.",
        ReadOnly = true)]
    internal sealed class GetOllamaModels : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            List<string> models = new();
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
                string json = await http.GetStringAsync("http://localhost:11434/api/tags", ct);

                JObject obj = JObject.Parse(json);
                models = obj["models"]!
                    .Select(m => m["name"]!.Value<string>()!)
                    .ToList();
            }
            catch { /* Ollama is not available — returning an empty list */ }

            return models;
        }
    }
}
