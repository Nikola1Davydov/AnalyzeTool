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
                      "Payload: { items: [{ elementId, id (parameter id), value }], mode: \"Overwrite\" | \"OnlyIfEmpty\" | \"SkipIfEqual\" }.",
        Destructive = true,
        InputType = typeof(SetDataToParameters.SetDataToParametersDto))]
    internal sealed class SetDataToParameters : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            SetDataToParametersDto? list = ctx.Payload.As<SetDataToParametersDto>();
            if (list == null) return Task.FromResult<object?>(null);

            return ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;

                using Transaction transaction = new Transaction(doc, "Set data to parameters");
                transaction.Start();

                foreach (SetParamItem parameterData in list.Items)
                {
                    if (parameterData == null) continue;
                    SetData(doc, parameterData, list.Mode);
                }

                transaction.Commit();
                return null;
            });
        }

        private void SetData(Document doc, SetParamItem parameterData, SetDataMode mode)
        {
            Element revitElement = doc.GetElement(new ElementId(parameterData.ElementId));
            if (revitElement == null) return;

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

            if (parameter == null || parameter.IsReadOnly) return;

            SetData(parameter, parameterData.Value, mode);
        }

        private void SetData(Parameter parameter, string value, SetDataMode mode)
        {
            switch (mode)
            {
                case SetDataMode.Overwrite:
                    break;
                case SetDataMode.OnlyIfEmpty when parameter.HasValue:
                    return;
                case SetDataMode.SkipIfEqual when parameter.GetParameterValue() == value:
                    return;
            }
            parameter.SetParameterValue(value);
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
