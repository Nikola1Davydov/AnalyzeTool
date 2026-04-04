using Newtonsoft.Json;

namespace AnalyseTool.RevitCommands.Model
{
    internal record DocumentData()
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("id")]
        public string Id { get; set; }
    }
}
