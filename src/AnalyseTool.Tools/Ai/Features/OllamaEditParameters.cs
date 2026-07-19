using AnalyseTool.Sdk;
using AnalyseTool.Tools.Infrastructure;
using AnalyseTool.Tools.Infrastructure.Model;

namespace AnalyseTool.Tools.Features.Ai
{
    [RevitCommand(
        Description = "Asks the AI to propose parameter edits for the given items and returns the edits " +
                      "(apply them via SetDataToParameters). Payload: { model, prompt, items }.",
        Destructive = true,
        InputType = typeof(AnalyzeParameterWithAiRequest),
        HiddenFromMcp = true)] // expects UI-collected items; a raw AI call can't build them
    internal sealed class OllamaEditParameters : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            AnalyzeParameterWithAiRequest? request = ctx.Payload.As<AnalyzeParameterWithAiRequest>();
            if (request == null) return null;

            try
            {
                AiAnalysisService ai = new AiAnalysisService(request.Provider, request.Model);
                AiAnalysisService.AiResponse result = await ai.AnalyzeAndEditAsync(request.Items, request.Prompt);

                return new
                {
                    edits = result.Edits,
                    raw = result.Raw,
                    error = (string?)null
                };
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("KI-Zeitüberschreitung: Das Modell hat nicht rechtzeitig geantwortet.");
            }
        }
    }
}
