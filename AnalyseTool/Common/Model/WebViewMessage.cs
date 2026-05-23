using Newtonsoft.Json.Linq;

namespace AnalyseTool.Common.Model
{
    internal record WebViewMessage
    {
        public string Type { get; set; }  // e.g., "requests", "response", "event"
        public string Command { get; set; } // seletion or isolation
        public JToken Payload { get; set; } // Actual data being sent

        // Correlation id for request/response matching (AT.invoke). Null for legacy fire-and-forget.
        public string? Id { get; set; }

        // Set on the response envelope when the command failed; lets AT.invoke reject the promise.
        public string? Error { get; set; }
    }
}

    