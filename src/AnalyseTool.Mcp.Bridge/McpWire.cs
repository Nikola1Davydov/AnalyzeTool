namespace AnalyseTool.Mcp
{
    /// <summary>
    /// Wire contract of the localhost TCP bridge between the two halves of the MCP subsystem:
    /// AnalyseTool.Mcp.exe (out-of-process, System.Text.Json) and AnalyseTool.Mcp.Bridge
    /// (in-Revit, Newtonsoft). The file is COMPILED INTO BOTH projects (linked in the exe's
    /// csproj), so renaming a field breaks the build instead of silently breaking the wire.
    ///
    /// Shapes:
    ///   request  { id, type: "invoke"|"list", command?, payload? }
    ///   response { id, result } | { id, error }
    ///   list result: { commands: [ { name, source, description, readOnly, destructive, inputSchema } ] }
    /// </summary>
    internal static class McpWire
    {
        public const int DefaultPort = 17890;

        // Envelope fields
        public const string Id = "id";
        public const string Type = "type";
        public const string Command = "command";
        public const string Payload = "payload";
        public const string Result = "result";
        public const string Error = "error";

        // Request types
        public const string TypeInvoke = "invoke";
        public const string TypeList = "list";

        // "list" result fields
        public const string Commands = "commands";
        public const string Name = "name";
        public const string SourceField = "source";
        public const string Description = "description";
        public const string ReadOnly = "readOnly";
        public const string Destructive = "destructive";
        public const string InputSchema = "inputSchema";
    }
}
