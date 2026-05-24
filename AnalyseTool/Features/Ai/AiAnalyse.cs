using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Ai
{
    [RevitCommand("AiAnalyse",
        Description = "Runs an AI analysis over the given parameter items using the named model and prompt; " +
                      "returns the model's analysis. Does not modify the model. Payload: { model, prompt, items }.")]
    internal sealed class AiAnalyse : RevitTask<AnalyzeParameterWithAiRequest>
    {
        public override async Task<object?> ExecuteAsync(AnalyzeParameterWithAiRequest request, IRevitContext ctx, CancellationToken ct)
        {
            if (request == null) return null;

            try
            {
                AiAnalysisService ai = new AiAnalysisService(request.Model);
                return await ai.AnalyzeAsync(request.Items, request.Prompt);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("AI Timeout: The model did not respond in time.");
            }
        }
    }
}
