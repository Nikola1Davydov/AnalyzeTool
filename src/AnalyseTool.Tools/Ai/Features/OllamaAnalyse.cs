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
    internal sealed class OllamaAnalyse : IRevitTask, IProgressAware
    {
        /// <summary>Set by the host when the caller is listening. This command is the one AI command whose
        /// answer is PROSE meant to be read, so it is the one where streaming is worth the plumbing: the
        /// others return JSON, and half a JSON array on screen is noise, not progress.</summary>
        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            AnalyzeParameterWithAiRequest? request = ctx.Payload.As<AnalyzeParameterWithAiRequest>();
            if (request == null)
                return new AiAnalysisResult(null, "Empty payload.");
            // Same guard the naming commands already carry. It matters more now that the request type has
            // no default model to fall back on — and it never should have had one that does not exist.
            if (string.IsNullOrWhiteSpace(request.Model))
                return new AiAnalysisResult(null, "No AI model selected.");

            try
            {
                AiAnalysisService ai = new AiAnalysisService(request.Provider, request.Model);

                // Fraction stays 0 and the text rides in Message. Token generation has no measurable
                // total — there is no honest fraction to report — and a caller that wants to SHOW the
                // answer arriving needs the text, not a bar. Deltas, not the running total: the transport
                // delivers them in order, and resending everything each time is quadratic for no gain.
                // If a second command ever needs this, that is the moment to give ProgressInfo a field of
                // its own instead of lending it this one.
                IProgress<ProgressInfo>? progress = Progress;
                Action<string>? onDelta = progress is null
                    ? null
                    : delta => progress.Report(new ProgressInfo(0, delta));

                string analysis = await ai.AnalyzeAsync(request.Items, request.Prompt, ct, onDelta);
                return new AiAnalysisResult(analysis, null);
            }
            // Returned rather than thrown, like the other AI commands: a model that timed out or an
            // endpoint that rejected the key is something to show the user, not a crash. The declared
            // schema is what makes this safe to rely on — the error field is always there to be read.
            // The two cancellations are told apart by whose token fired.
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new AiAnalysisResult(null, "AI timeout: the model did not answer in time.");
            }
            catch (OperationCanceledException)
            {
                return new AiAnalysisResult(null, "Cancelled.");
            }
            catch (Exception ex)
            {
                return new AiAnalysisResult(null, ex.Message);
            }
        }
    }
}
