using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel;
using ParameterUtils = Autodesk.Revit.DB.ParameterUtils;

namespace AnalyseTool.Tools.Actions
{
    [RevitCommand(
        Description = "Writes values to element parameters (MODIFIES the model, inside a transaction). " +
                      "Payload: { items: [{ elementId, id (parameter id), value }], mode: \"Overwrite\" | \"OnlyIfEmpty\" | \"SkipIfEqual\" }. " +
                      "Returns { ok, written, skipped, warnings: [{ description, elementIds }] } — 'skipped' counts " +
                      "items whose element or parameter was not found, was read-only, or that the mode filtered out. " +
                      "Parameter ids come from GetCategoryParameters. Cost: one transaction over the given " +
                      "items.",
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

            return ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;

                using Transaction transaction = new Transaction(doc, "Set data to parameters");
                transaction.Start();
                // Without this a Revit warning raises a MODAL dialog on the Revit thread and the whole
                // platform waits for a click that, in a batch, nobody is there to give.
                CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(transaction);

                int written = 0, skipped = 0;
                foreach (SetParamItem parameterData in list.Items)
                {
                    if (parameterData == null) { skipped++; continue; }
                    if (SetData(doc, parameterData, list.Mode)) written++;
                    else skipped++;
                }

                transaction.Commit();

                // Counted and reported rather than silently dropped: an unattended caller has no other
                // way to learn that 40 of its 500 writes never landed.
                return new SetDataResult(true, written, skipped, null, failures.Warnings);
            });
        }

        /// <summary>Returns true when the value was actually written; false when the element or parameter
        /// was not found, the parameter is read-only, or <paramref name="mode"/> filtered the write out.</summary>
        private bool SetData(Document doc, SetParamItem parameterData, SetDataMode mode)
        {
            Element revitElement = doc.GetElement(new ElementId(parameterData.ElementId));
            if (revitElement == null) return false;

            Parameter? parameter = null;

            if (ParameterUtils.IsBuiltInParameter(new ElementId(parameterData.Id)))
            {
                BuiltInParameter builtInParameter = (BuiltInParameter)parameterData.Id;
                parameter = revitElement.get_Parameter(builtInParameter);
            }
            else
            {
                foreach (Parameter elementParameter in revitElement.Parameters)
                {
                    if (elementParameter?.Id != null && elementParameter.Id.Value == parameterData.Id)
                    {
                        parameter = elementParameter;
                        break;
                    }
                }

                if (parameter == null)
                {
                    ParameterElement? parameterElement = doc.GetElement(new ElementId(parameterData.Id)) as ParameterElement;
                    Definition? definition = parameterElement?.GetDefinition();

                    if (definition != null)
                    {
                        parameter = revitElement.get_Parameter(definition);
                    }
                }
            }

            if (parameter == null || parameter.IsReadOnly) return false;

            return SetData(parameter, parameterData.Value, mode);
        }

        /// <summary>Returns false when <paramref name="mode"/> filtered the write out — the caller counts
        /// that as skipped, not written. These modes are also what makes a node re-runnable: a pipeline
        /// run is not atomic, so a mutating step must tolerate the state having moved under it.</summary>
        private bool SetData(Parameter parameter, string value, SetDataMode mode)
        {
            switch (mode)
            {
                case SetDataMode.Overwrite:
                    break;
                case SetDataMode.OnlyIfEmpty when parameter.HasValue:
                    return false;
                case SetDataMode.SkipIfEqual when parameter.GetParameterValue() == value:
                    return false;
            }
            parameter.SetParameterValue(value);
            return true;
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
