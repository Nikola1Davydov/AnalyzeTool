using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;

namespace AnalyseTool.Tools.Ai
{
    [RevitCommand(
        Description = "Runs an AI analysis over the given parameter items using the named model and prompt; " +
                      "returns the model's analysis as prose. Does not modify the model. " +
                      "Payload: { model, prompt, items }. Returns { analysis, error }.",
        ReadOnly = true,
        InputType = typeof(AnalyzeParameterWithAiRequest),
        HiddenFromMcp = true, // expects UI-collected items; a raw AI call can't build them
        OutputType = typeof(AiAnalysisResult))]
    internal sealed class OllamaAnalyse : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            AnalyzeParameterWithAiRequest? request = ctx.Payload.As<AnalyzeParameterWithAiRequest>();
            if (request == null)
                return new AiAnalysisResult(null, "Empty payload.");

            try
            {
                AiAnalysisService ai = new AiAnalysisService(request.Provider, request.Model);
                return new AiAnalysisResult(await ai.AnalyzeAsync(request.Items, request.Prompt), null);
            }
            // Returned rather than thrown, like the other AI commands: a model that timed out or an
            // endpoint that rejected the key is something to show the user, not a crash. The declared
            // schema is what makes this safe to rely on — the error field is always there to be read.
            catch (OperationCanceledException)
            {
                return new AiAnalysisResult(null, "AI timeout: the model did not answer in time.");
            }
            catch (Exception ex)
            {
                return new AiAnalysisResult(null, ex.Message);
            }
        }
    }
}
