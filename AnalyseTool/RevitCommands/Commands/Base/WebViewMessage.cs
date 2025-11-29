using Newtonsoft.Json.Linq;

namespace AnalyseTool.RevitCommands.Commands.Base
{
    internal record WebViewMessage
    {
        public string Type { get; set; }  // e.g., "requests", "response", "event"
        public string Command { get; set; } // seletion or isolation
        public JToken Payload { get; set; } // Actual data being sent
    }
}

    