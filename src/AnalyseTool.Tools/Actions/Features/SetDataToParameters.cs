using AnalyseTool.Sdk;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Tools.Ai;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel;

namespace AnalyseTool.Tools.Actions
{
    [RevitCommand(
        Description = "Writes values to element parameters (MODIFIES the model, inside a transaction). " +
                      "Payload: { items: [{ elementId, id (parameter id), value }], mode: \"Overwrite\" | \"OnlyIfEmpty\" | \"SkipIfEqual\" }. " +
                      "Returns { ok, written, skipped, warnings: [{ description, elementIds }], problems: [{ elementId, " +
                      "parameterId, reason }] } — 'skipped' counts items whose element or parameter was not found, " +
                      "was read-only, could not take the value, or that the mode filtered out; 'problems' names the " +
                      "ones that failed with a reason. One bad item never fails the batch: the others are written " +
                      "and committed. Parameter ids come from GetCategoryParameters ('id'). Cost: one transaction " +
                      "over the given items.",
        Destructive = true,
        InputType = typeof(SetDataToParameters.SetDataToParametersDto),
        OutputType = typeof(SetDataResult))]
    internal sealed class SetDataToParameters : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            SetDataToParametersDto? list = ctx.Payload.As<SetDataToParametersDto>();
            if (list == null)
                return Task.FromResult<object?>(new SetDataResult(false, 0, 0, "Empty payload.", Array.Empty<TransactionWarning>()));

            // The command is the shell; the write itself is ParameterWriteService, a function of the
            // Document — testable inside Revit without a UIApplication.
            List<ParameterWriteService.Item?> items = list.Items
                .Select(i => i is null ? null : new ParameterWriteService.Item(i.ElementId, i.Id, i.Value))
                .ToList();
            ParameterWriteService.Mode mode = list.Mode switch
            {
                SetDataMode.OnlyIfEmpty => ParameterWriteService.Mode.OnlyIfEmpty,
                SetDataMode.SkipIfEqual => ParameterWriteService.Mode.SkipIfEqual,
                _ => ParameterWriteService.Mode.Overwrite,
            };

            return ctx.RunInRevitAsync<object?>(app =>
                new ParameterWriteService().Write(app.ActiveUIDocument.Document, items, mode));
        }

        internal sealed record SetDataToParametersDto()
        {
            [JsonProperty("items")]
            [Description("Parameter writes to apply.")]
            public List<SetParamItem> Items { get; set; } = new();

            [JsonProperty("mode")]
            [JsonConverter(typeof(StringEnumConverter))]
            [Description("How to apply: Overwrite (always), OnlyIfEmpty (skip if the parameter has a value), or SkipIfEqual.")]
            public SetDataMode Mode { get; set; }
        }

        /// <summary>Lean input for one parameter write (kept small on purpose so the MCP schema stays tight —
        /// not the rich ParameterData model, which would drag a Revit Parameter type into the schema).</summary>
        internal sealed record SetParamItem
        {
            [JsonProperty("elementId")]
            [Description("Revit ElementId of the element to modify.")]
            public long ElementId { get; set; }

            [JsonProperty("id")]
            [Description("Parameter id: a BuiltInParameter integer value, or a ParameterElement ElementId for shared/project params.")]
            public long Id { get; set; }

            [JsonProperty("value")]
            [Description("New value as a string (parsed according to the parameter's storage type).")]
            public string Value { get; set; } = string.Empty;
        }

        internal enum SetDataMode
        {
            Overwrite,
            OnlyIfEmpty,
            SkipIfEqual
        }
    }
}
