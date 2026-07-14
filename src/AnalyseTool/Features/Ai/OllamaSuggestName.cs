using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Features.Ai
{
    /// <summary>
    /// Suggests a new name for a family or family type from its current name plus a free-text instruction
    /// (Family Control's Rename dialog AI mode). Reuses the same Ollama plumbing as the other AI commands.
    /// Returns { name, error }; does not touch the Revit model — the caller applies the chosen name via
    /// RenameFamily / RenameFamilyType.
    /// </summary>
    [RevitCommand(
        Description = "Suggests a new family/type name from the current name and a free-text instruction. " +
                      "Payload: { model, prompt, currentName, context }. Returns { name, error }.",
        ReadOnly = true,
        InputType = typeof(OllamaSuggestName.Request),
        HiddenFromMcp = true)]
    internal sealed class OllamaSuggestName : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (req is null || string.IsNullOrWhiteSpace(req.Model))
                return new { name = (string?)null, error = "No AI model selected." };

            try
            {
                AiAnalysisService ai = new AiAnalysisService(req.Provider, req.Model);
                string name = await ai.SuggestNameAsync(
                    req.CurrentName ?? string.Empty, req.Context ?? string.Empty, req.Prompt ?? string.Empty);
                return new { name, error = (string?)null };
            }
            catch (OperationCanceledException)
            {
                return new { name = (string?)null, error = "AI timeout: the model did not answer in time." };
            }
            catch (Exception ex)
            {
                return new { name = (string?)null, error = ex.Message };
            }
        }

        public sealed class Request
        {
            [Description("Model name to use.")]
            public string? Model { get; set; }

            [Description("AI provider id (AiGetProviders); omit for the built-in local Ollama.")]
            public string? Provider { get; set; }

            [Description("Free-text instruction, e.g. 'translate to English' or 'add prefix WALL_'.")]
            public string? Prompt { get; set; }

            [Description("The current family/type name.")]
            public string? CurrentName { get; set; }

            [Description("Optional context (e.g. category) to help the model.")]
            public string? Context { get; set; }
        }
    }
}
