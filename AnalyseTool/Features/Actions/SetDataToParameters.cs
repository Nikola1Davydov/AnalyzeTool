using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ParameterUtils = Autodesk.Revit.DB.ParameterUtils;

namespace AnalyseTool.Features.Actions
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

                foreach (ParameterData parameterData in list.Items)
                {
                    if (parameterData == null) continue;
                    SetData(doc, parameterData, list.Mode);
                }

                transaction.Commit();
                return null;
            });
        }

        private void SetData(Document doc, ParameterData parameterData, SetDataMode mode)
        {
            Element revitElement = doc.GetElement(new ElementId(parameterData.ElementId));
            if (revitElement == null) return;

            Parameter parameter = null;

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
                    ParameterElement parameterElement = doc.GetElement(new ElementId(parameterData.Id)) as ParameterElement;
                    Definition definition = parameterElement?.GetDefinition();

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
            public List<ParameterData> Items { get; set; } = new();

            [JsonProperty("mode")]
            [JsonConverter(typeof(StringEnumConverter))]
            public SetDataMode Mode { get; set; }
        }

        internal enum SetDataMode
        {
            Overwrite,
            OnlyIfEmpty,
            SkipIfEqual
        }
    }
}
