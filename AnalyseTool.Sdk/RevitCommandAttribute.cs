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
        public RevitCommandAttribute(string name) => Name = name;

        public string Name { get; }

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
    }
}
