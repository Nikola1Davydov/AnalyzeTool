using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Features.Families
{
    /// <summary>
    /// Returns the embedded preview thumbnail of an on-disk family (.rfa) as a PNG data URI, extracted via
    /// the Windows shell WITHOUT opening the family. Best-effort — dataUri is null when no preview exists.
    /// Runs off the Revit thread (pure file/shell work), so it doesn't need <c>RunInRevitAsync</c>.
    /// </summary>
    [RevitCommand(
        Description = "Returns the .rfa file's embedded preview image as a PNG data URI (or null). Read-only.",
        ReadOnly = true,
        HiddenFromMcp = true,
        InputType = typeof(GetLibraryPreview.Request))]
    internal sealed class GetLibraryPreview : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            // Task.Run: the shell extraction blocks its calling thread (STA worker + Join), and this
            // command is dispatched on the UI thread — extracting inline would freeze Revit per tile.
            return Task.Run<object?>(() =>
            {
                string? dataUri = string.IsNullOrWhiteSpace(req.Path) ? null : ShellThumbnail.GetPngDataUri(req.Path!);
                return new { path = req.Path, dataUri };
            }, ct);
        }

        public sealed class Request
        {
            [Description("Full path to the .rfa file whose preview to extract.")]
            public string? Path { get; set; }
        }
    }
}
