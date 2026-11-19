using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Threading;
using AnalyseTool.Hosting.Visualization;

namespace AnalyseTool.Features.Views;

/// <summary>
///     Removes everything <see cref="ShowGeometry" /> drew, in every document.
/// </summary>
/// <remarks>Works without an open document too, so it goes through the dispatcher, not <c>ActiveDocument</c>.</remarks>
[McpTool(ReadOnly = true, Title = "Clear shown geometry")]
[Command(Description = "Removes all the geometry ShowGeometry drew, in every document. Returns how many geometries were removed.")]
public sealed class ClearGeometry(IRevitDispatcher dispatcher, IGeometryOverlay overlay) : ICommand<ClearGeometryResult>
{
    /// <inheritdoc />
    public Task<ClearGeometryResult> ExecuteAsync(CommandContext context)
    {
        return dispatcher.InvokeAsync(_ => new ClearGeometryResult(overlay.Clear()), context.CancellationToken);
    }
}

/// <summary>The result of <see cref="ClearGeometry" />.</summary>
/// <param name="Removed">How many geometries were removed.</param>
public sealed record ClearGeometryResult(int Removed);
