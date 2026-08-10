namespace AnalyseTool.Mcp;

/// <summary>
/// A failed call, carrying the code the agent should branch on rather than only the sentence it should
/// read. Raised for errors the bridge reported AND for the two the client mints itself (the invoke
/// deadline and an unreachable Revit), so CallTool has one shape to render whatever went wrong.
/// </summary>
internal sealed class BridgeException : Exception
{
    public BridgeException(string code, string message, string? hint = null) : base(message)
    {
        Code = code;
        Hint = hint;
    }

    /// <summary>One of <see cref="McpWire.Codes"/>.</summary>
    public string Code { get; }

    /// <summary>What to do about it, when there is something to do; null when there is not.</summary>
    public string? Hint { get; }
}
