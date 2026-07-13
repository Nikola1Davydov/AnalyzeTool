using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Features.Ai
{
    /// <summary>
    /// Suggests new names for a BATCH of families/types in one model round-trip (the manager's
    /// multi-select "Rename with AI"). One call instead of N: local models are slow per request, and a
    /// consistent naming scheme requires the model to see the whole list. Returns { suggestions:
    /// [{ id, name }], error }; does not touch the Revit model — the caller reviews and applies each
    /// rename via RenameFamily / RenameFamilyType.
    /// </summary>
    [RevitCommand(
        Description = "Suggests new names for a batch of families/types from a free-text instruction. " +
                      "Payload: { model, prompt, items: [{ id, currentName, context }] }. " +
                      "Returns { suggestions: [{ id, name }], error }.",
        ReadOnly = true,
        InputType = typeof(OllamaSuggestNames.Request),
        HiddenFromMcp = true)]
    internal sealed class OllamaSuggestNames : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (req is null || string.IsNullOrWhiteSpace(req.Model))
                return new { suggestions = Array.Empty<object>(), error = "No AI model selected." };
            if (req.Items is not { Count: > 0 })
                return new { suggestions = Array.Empty<object>(), error = "Nothing to rename." };

            try
            {
                AiAnalysisService ai = new AiAnalysisService(req.Model);
                var items = req.Items
                    .Select(i => new AiAnalysisService.NameItem(i.Id, i.CurrentName ?? string.Empty, i.Context ?? string.Empty))
                    .ToList();
                var result = await ai.SuggestNamesAsync(items, req.Prompt ?? string.Empty);
                return new
                {
                    suggestions = result.Select(s => new { id = s.Id, name = s.Name }).ToArray(),
                    error = (string?)null
                };
            }
            catch (OperationCanceledException)
            {
                return new { suggestions = Array.Empty<object>(), error = "AI timeout: the model did not answer in time." };
            }
            catch (Exception ex)
            {
                return new { suggestions = Array.Empty<object>(), error = ex.Message };
            }
        }

        public sealed class Request
        {
            [Description("Ollama model name to use.")]
            public string? Model { get; set; }

            [Description("Free-text instruction applied consistently to the whole batch.")]
            public string? Prompt { get; set; }

            [Description("The elements to rename.")]
            public List<Item>? Items { get; set; }
        }

        public sealed class Item
        {
            [Description("Caller's element id — echoed back to match suggestions to rows.")]
            public long Id { get; set; }

            [Description("The current name.")]
            public string? CurrentName { get; set; }

            [Description("Optional context (e.g. category) to help the model.")]
            public string? Context { get; set; }
        }
    }
}
