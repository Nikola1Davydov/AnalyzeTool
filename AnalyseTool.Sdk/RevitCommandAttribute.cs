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
    }
}
