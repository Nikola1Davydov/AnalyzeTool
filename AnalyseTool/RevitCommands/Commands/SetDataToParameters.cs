using AnalyseTool.Extensions;
using AnalyseTool.RevitCommands.Commands.Base;
using AnalyseTool.RevitCommands.Model;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using ParameterUtils = Autodesk.Revit.DB.ParameterUtils;

namespace AnalyseTool.RevitCommands.Commands
{
    internal class SetDataToParameters : IRevitTask
    {
        public void Execute(JToken payload, WebView2 webView)
        {
            SetDataToParametersDto list = payload.ToObject<SetDataToParametersDto>();
            if (list == null) return;

            ExternalEventHub.RevitExternalEvent.action = () =>
            {
                RevitTransactions.Run("Set data to parameters", () =>
                {
                    foreach (ParameterData parameterData in list.Items)
                    {
                        if (parameterData == null) continue;

                        SetData(parameterData, list.Mode);
                    }
                });
            };

            ExternalEventHub.RevitEvent.Raise();
        }

        private void SetData(ParameterData parameterData, SetDataMode mode)
        {
            Element revitElement = Context.Document.GetElement(new ElementId(parameterData.ElementId));
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
                    ParameterElement parameterElement = Context.Document.GetElement(new ElementId(parameterData.Id)) as ParameterElement;
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

        private sealed record SetDataToParametersDto()
        {
            [JsonProperty("items")]
            public List<ParameterData> Items { get; set; } = new();

            [JsonProperty("mode")]
            [JsonConverter(typeof(StringEnumConverter))]
            public SetDataMode Mode { get; set; }
        }
        private enum SetDataMode
        {
            Overwrite,
            OnlyIfEmpty,
            SkipIfEqual
        }
    }
}
