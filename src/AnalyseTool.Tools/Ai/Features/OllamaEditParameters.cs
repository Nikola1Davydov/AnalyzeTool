using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;

namespace AnalyseTool.Tools.Ai
{
    [RevitCommand(
        Description = "Asks the AI to propose parameter edits for the given items and returns the edits " +
                      "(apply them via SetDataToParameters). Payload: { model, prompt, items }. " +
                      "Returns { edits: [{ elementId, parameter, oldValue, newValue, reason }], raw, error }.",
        Destructive = true,
        InputType = typeof(AnalyzeParameterWithAiRequest),
        HiddenFromMcp = true, // expects UI-collected items; a raw AI call can't build them
        OutputType = typeof(AiEditsResult))]
    internal sealed class OllamaEditParameters : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            AnalyzeParameterWithAiRequest? request = ctx.Payload.As<AnalyzeParameterWithAiRequest>();
            if (request == null)
                return new AiEditsResult(Array.Empty<AiParameterEdit>(), null, "Empty payload.");

            try
            {
                AiAnalysisService ai = new AiAnalysisService(request.Provider, request.Model);
                AiAnalysisService.AiResponse result = await ai.AnalyzeAndEditAsync(request.Items, request.Prompt);

                // Mapped, not handed over: ParameterAiEdit is what the PROMPT asks the model to produce.
                // Copying it onto the wire type keeps the published schema out of the prompt's reach.
                return new AiEditsResult(
                    result.Edits
                        .Select(e => new AiParameterEdit(
                            e.ElementId, e.Parameter ?? string.Empty, e.OldValue ?? string.Empty,
                            e.NewValue ?? string.Empty, e.Reason ?? string.Empty))
                        .ToArray(),
                    result.Raw,
                    null);
            }
            // Returned rather than thrown, like the other AI commands. The raw answer is null here on
            // purpose: a call that timed out has none, and an empty string would read as "the model
            // replied with nothing", which is a different failure.
            catch (OperationCanceledException)
            {
                return new AiEditsResult(
                    Array.Empty<AiParameterEdit>(), null, "AI timeout: the model did not answer in time.");
            }
            catch (Exception ex)
            {
                return new AiEditsResult(Array.Empty<AiParameterEdit>(), null, ex.Message);
            }
        }
    }
}
