using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using System.ComponentModel;

namespace AnalyseTool.Tools.Ai
{
    /// <summary>
    /// Reverse-engineers a naming-rule TEMPLATE from one example name plus a sample element's data
    /// (the rule builder's "create from example"). The AI authors an editable rule — applying it stays
    /// deterministic in the frontend engine. Returns { template, abbreviations: [{ full, abbr }], error };
    /// does not touch the Revit model.
    /// </summary>
    [RevitCommand(
        Description = "Infers a naming template from one example name and a sample element's data. " +
                      "Payload: { model, example, name, family, category, parameters: { <name>: <value> } }. " +
                      "Returns { template, abbreviations: [{ full, abbr }], error }.",
        ReadOnly = true,
        InputType = typeof(OllamaSuggestTemplate.Request),
        HiddenFromMcp = true)]
    internal sealed class OllamaSuggestTemplate : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (req is null || string.IsNullOrWhiteSpace(req.Model))
                return new { template = (string?)null, abbreviations = Array.Empty<object>(), error = "No AI model selected." };
            if (string.IsNullOrWhiteSpace(req.Example))
                return new { template = (string?)null, abbreviations = Array.Empty<object>(), error = "No example name given." };

            try
            {
                AiAnalysisService ai = new AiAnalysisService(req.Provider, req.Model);
                var result = await ai.SuggestTemplateAsync(
                    req.Example, req.Name ?? string.Empty, req.Family ?? string.Empty,
                    req.Category ?? string.Empty, req.Parameters ?? new Dictionary<string, string>());

                if (result is null || string.IsNullOrWhiteSpace(result.Template))
                    return new { template = (string?)null, abbreviations = Array.Empty<object>(), error = "The model returned no usable template." };

                return new
                {
                    template = result.Template.Trim(),
                    abbreviations = (result.Abbreviations ?? [])
                        .Where(a => !string.IsNullOrWhiteSpace(a.Full) && !string.IsNullOrWhiteSpace(a.Abbr))
                        .Select(a => new { full = a.Full.Trim(), abbr = a.Abbr.Trim() })
                        .ToArray(),
                    error = (string?)null
                };
            }
            catch (OperationCanceledException)
            {
                return new { template = (string?)null, abbreviations = Array.Empty<object>(), error = "AI timeout: the model did not answer in time." };
            }
            catch (Exception ex)
            {
                return new { template = (string?)null, abbreviations = Array.Empty<object>(), error = ex.Message };
            }
        }

        public sealed class Request
        {
            [Description("Model name to use.")]
            public string? Model { get; set; }

            [Description("AI provider id (AiGetProviders); omit for the built-in local Ollama.")]
            public string? Provider { get; set; }

            [Description("The desired example name, e.g. 'Möb_Alu_1000x2000'.")]
            public string? Example { get; set; }

            [Description("The sample element's current name.")]
            public string? Name { get; set; }

            [Description("The sample element's family name.")]
            public string? Family { get; set; }

            [Description("The sample element's category.")]
            public string? Category { get; set; }

            [Description("The sample element's parameter display values, keyed by parameter name.")]
            public Dictionary<string, string>? Parameters { get; set; }
        }
    }
}
