namespace AnalyseTool.Sdk
{
    /// <summary>
    /// Optional. Sets the wire command name an <see cref="IRevitTask"/> is dispatched under
    /// (what JS calls via AT.invoke and what MCP exposes). When absent, the class name is used.
    /// Use this to decouple the public command name from the C# class name so refactoring the
    /// class does not break callers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RevitCommandAttribute : Attribute
    {
        /// <summary>Uses the class name as the command (wire) name. Use this overload when you only
        /// need metadata (Description/ReadOnly/Destructive/InputType) but the class name is already
        /// the name you want.</summary>
        public RevitCommandAttribute() { }

        /// <summary>Overrides the command (wire) name so it is decoupled from the class name.</summary>
        public RevitCommandAttribute(string name) => Name = name;

        /// <summary>Explicit wire name. When null/empty the dispatcher falls back to the class name.</summary>
        public string? Name { get; }

        /// <summary>
        /// Human/AI-facing description of what the command does, when to use it, and what it returns.
        /// Becomes the MCP tool description. Keep it concise and specific.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Hint that the command only READS the model (makes no changes). Maps to the MCP tool's
        /// <c>readOnlyHint</c> annotation so clients can treat it as safe.
        /// </summary>
        public bool ReadOnly { get; set; }

        /// <summary>
        /// Hint that the command may make DESTRUCTIVE or hard-to-undo changes (delete, overwrite).
        /// Maps to the MCP tool's <c>destructiveHint</c> annotation so clients can warn/confirm.
        /// </summary>
        public bool Destructive { get; set; }

        /// <summary>
        /// Optional CLR type describing the command's JSON input. The host generates a JSON Schema
        /// from this type and publishes it as the MCP tool's input schema, so an AI knows which
        /// arguments to send. You still read the payload yourself via <c>ctx.Payload.As&lt;T&gt;()</c>.
        /// Must be at least <c>internal</c> (so <c>typeof(...)</c> can reference it). Leave null for
        /// commands that take no arguments.
        /// </summary>
        public Type? InputType { get; set; }
    }
}
