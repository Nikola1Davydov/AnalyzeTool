namespace AnalyseTool.Hosting.Mcp;

/// <summary>
///     Opens a command class to AI clients as an MCP tool. Without it a command is never visible over MCP; on a class that
///     is not a registered command it does nothing.
/// </summary>
/// <remarks>
///     The tool name is the command name and its description is <c>[Command(Description = …)]</c>: an AI client chooses
///     tools by their descriptions, so write what the command does and what it returns. The input schema comes from the
///     request type, which must be an object with properties.
/// </remarks>
/// <example>
///     <code>
///     [McpTool(ReadOnly = true)]
///     [Command(Description = "Counts the model elements of the active document by category.")]
///     public sealed class CountElementsByCategory(ActiveDocument document) : ICommand&lt;IReadOnlyList&lt;CategoryCount&gt;&gt;
///     </code>
/// </example>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class McpToolAttribute : Attribute
{
    /// <summary>Gets or sets a human-readable title of the tool.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets a value indicating whether the tool only reads the model; clients may then call it without asking.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Gets or sets a value indicating whether the tool deletes or overwrites data; ignored for a read-only tool.</summary>
    public bool Destructive { get; set; }
}
