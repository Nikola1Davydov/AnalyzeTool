using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Ai
{
    [RevitCommand("AiEditParameters",
        Description = "Asks the AI to propose parameter edits for the given items and returns the edits " +
                      "(apply them via SetDataToParameters). Payload: { model, prompt, items }.",
        Destructive = true)]
    internal sealed class AiEditParameters : RevitTask<AnalyzeParameterWithAiRequest>
    {
        public override async Task<object?> ExecuteAsync(AnalyzeParameterWithAiRequest request, IRevitContext ctx, CancellationToken ct)
        {
            if (request == null) return null;

            try
            {
                AiAnalysisService ai = new AiAnalysisService(request.Model);
                AiAnalysisService.AiResponce result = await ai.AnalyzeAndEditAsync(request.Items, request.Prompt);

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
