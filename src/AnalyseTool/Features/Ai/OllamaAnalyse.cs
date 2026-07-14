using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Ai
{
    [RevitCommand(
        Description = "Runs an AI analysis over the given parameter items using the named model and prompt; " +
                      "returns the model's analysis. Does not modify the model. Payload: { model, prompt, items }.",
        InputType = typeof(AnalyzeParameterWithAiRequest),
        HiddenFromMcp = true)] // expects UI-collected items; a raw AI call can't build them
    internal sealed class OllamaAnalyse : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            AnalyzeParameterWithAiRequest? request = ctx.Payload.As<AnalyzeParameterWithAiRequest>();
            if (request == null) return null;

            try
            {
                AiAnalysisService ai = new AiAnalysisService(request.Provider, request.Model);
                return await ai.AnalyzeAsync(request.Items, request.Prompt);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("AI Timeout: The model did not respond in time.");
            }
        }
    }
}
