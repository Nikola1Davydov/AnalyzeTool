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
                RevitTransactions.Run("Connect Parameters Execute", () =>
                {
                    SetData(list);
                });
            };

            ExternalEventHub.RevitEvent.Raise();
        }

        private void SetData(SetDataToParametersDto list)
        {
            foreach (ParameterData item in list.Items)
            {
                if (item == null) continue;

                Element revitElement = Context.Document.GetElement(new ElementId(item.ElementId));
                if (revitElement == null) continue;

                Parameter parameter = null;

                if (ParameterUtils.IsBuiltInParameter(new ElementId(item.Id)))
                {
                    BuiltInParameter builtInParameter = (BuiltInParameter)item.Id;
                    parameter = revitElement.get_Parameter(builtInParameter);
                }
                else
                {
                    foreach (Parameter elementParameter in revitElement.Parameters)
                    {
                        if (elementParameter?.Id != null && elementParameter.Id.Value == item.Id)
                        {
                            parameter = elementParameter;
                            break;
                        }
                    }

                    if (parameter == null)
                    {
                        ParameterElement parameterElement = Context.Document.GetElement(new ElementId(item.Id)) as ParameterElement;
                        Definition definition = parameterElement?.GetDefinition();

                        if (definition != null)
                        {
                            parameter = revitElement.get_Parameter(definition);
                        }
                    }
                }

                if (parameter == null || parameter.IsReadOnly) continue;

                parameter.SetParameterValue(item.Value);
            }
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
