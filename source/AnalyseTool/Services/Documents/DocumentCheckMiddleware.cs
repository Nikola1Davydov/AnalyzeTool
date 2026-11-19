using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Pipeline;

namespace AnalyseTool.Services.Documents;

/// <summary>
///     Fixes the document of the call from the caller's <see cref="Header" /> header, so <see cref="ActiveDocument" /> refuses
///     to work when the user has switched to another document since the caller loaded its data.
/// </summary>
/// <remarks>Without the header — a ribbon button — the document is the one active at the command's first access.</remarks>
public sealed class DocumentCheckMiddleware(ActiveDocument document) : ICommandMiddleware
{
    /// <summary>The header with the <see cref="ActiveDocument.GetKey" /> of the document the caller works with.</summary>
    public const string Header = "Document";

    /// <inheritdoc />
    public Task<object?> InvokeAsync(CommandContext context, CommandDelegate next)
    {
        if (context.Request.Headers.TryGetValue(Header, out var key))
        {
            document.Expect(key);
        }

        return next(context);
    }
}
