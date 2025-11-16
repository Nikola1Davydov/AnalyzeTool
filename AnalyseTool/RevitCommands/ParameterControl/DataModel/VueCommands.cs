using Newtonsoft.Json;

namespace AnalyseTool.RevitCommands.ParameterControl.DataModel
{
    internal record VueCommands
    {
        public CommandsEnum CommandsEnum { get; init; }
        public string JsonData { get; init; }

        public VueCommands(string json)
        {
            DeserializeJson(json);

        }
        private void DeserializeJson(string json)
        {
            //var data = JsonSerializer.Deserialize<VueCommands>(json);
            //CommandsEnum = commandsEnum;
            //JsonData = jsonData;
        }
    }
}

