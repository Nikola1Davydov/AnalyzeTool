using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Ai
{
    internal sealed class AiAnalyse : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            AnalyzeParameterWithAiRequest? request = ctx.Payload.As<AnalyzeParameterWithAiRequest>();
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
