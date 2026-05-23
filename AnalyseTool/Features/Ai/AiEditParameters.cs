using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Ai
{
    internal sealed class AiEditParameters : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            AnalyzeParameterWithAiRequest? request = ctx.Payload.As<AnalyzeParameterWithAiRequest>();
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
