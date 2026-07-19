using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Features.Families
{
    /// <summary>
    /// Loads the given family (.rfa) files into the current document, one Revit round-trip per file so it
    /// can report live progress (see <see cref="IProgressAware"/>). A file that can't be loaded (already
    /// present, invalid) is skipped and counted. Backs the palette Library "Load" action.
    /// </summary>
    [RevitCommand(
        Description = "Loads the given .rfa files into the current document, skipping any that fail. " +
                      "Returns { loaded, failed }.",
        InputType = typeof(LoadLibraryFamilies.Request))]
    internal sealed class LoadLibraryFamilies : IRevitTask, IProgressAware
    {
        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();
            List<string> paths = req.Paths ?? new List<string>();
            if (paths.Count == 0) return new { ok = true, loaded = 0, failed = 0 };

            LibraryService service = new();
            int loaded = 0, failed = 0;

            for (int i = 0; i < paths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                string path = paths[i];

                // One family per round-trip: control returns to the UI thread between files so the
                // progress bar animates (loading is slow, so the round-trip overhead is negligible).
                bool ok = await ctx.RunInRevitAsync(app => service.LoadOne(app.ActiveUIDocument.Document, path));
                if (ok) loaded++; else failed++;

                Progress?.Report(new ProgressInfo((i + 1) / (double)paths.Count, "Loading families…"));
            }

            return new { ok = true, loaded, failed };
        }

        public sealed class Request
        {
            [Description("Full paths of the .rfa files to load into the document.")]
            public List<string>? Paths { get; set; }
        }
    }
}
