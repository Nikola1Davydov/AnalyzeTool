using AnalyseTool.Sdk;
using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace AnalyseTool.Tools.Ai
{
    [RevitCommand(
        Description = "Returns { running, models } for the local Ollama service (model names for the AI " +
                      "features). running=false means Ollama is unreachable. Does not touch the Revit model.",
        ReadOnly = true,
        HiddenFromMcp = true)]
    internal sealed class OllamaGetModels : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
                string json = await http.GetStringAsync("http://localhost:11434/api/tags", ct);

                JObject obj = JObject.Parse(json);
                List<string> models = obj["models"]!
                    .Select(m => m["name"]!.Value<string>()!)
                    .ToList();

                // running with whatever models are installed (the list may be empty).
                return new { running = true, models };
            }
            catch
            {
                // Ollama not reachable — distinct from "running with zero models".
                return new { running = false, models = (List<string>?)null };
            }
        }
    }
}
