using AnalyseTool.RevitCommands.Commands.Base;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.DataModel
{
    internal record WebViewMessage
    {
        public CommandsEnum CommandsEnum { get; set; }
        public JObject JsonData { get; set; }
    }
}

    