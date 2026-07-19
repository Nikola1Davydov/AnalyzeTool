using AnalyseTool.Sdk;
using AnalyseTool.Tools.Infrastructure;
using System.ComponentModel;

namespace AnalyseTool.Tools.Features.Families
{
    /// <summary>
    /// Lists the family (.rfa) files under the given library folders, each tagged with its root folder and
    /// whether a family of that name is already loaded in the document. Backs the palette's Library mode.
    /// </summary>
    [RevitCommand(
        Description = "Lists Revit family files under the given folders, flagging which are already loaded " +
                      "in the document. Read-only.",
        ReadOnly = true,
        InputType = typeof(GetLibraryFamilies.Request))]
    internal sealed class GetLibraryFamilies : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            // Phase 1 — pure file I/O off the Revit thread, so a large/network library never freezes Revit.
            IReadOnlyList<LibraryService.ScannedFile> files = await Task.Run(
                () => LibraryService.ScanFiles(req.Folders ?? new List<string>()), ct);

            // Phase 2 — Revit-side decoration (loaded flags + saved-in version, cached by path+mtime).
            return await ctx.RunInRevitAsync<object?>(app =>
            {
                var families = new LibraryService().Decorate(app.ActiveUIDocument.Document, files);
                return new { count = families.Count, families };
            });
        }

        public sealed class Request
        {
            [Description("Library folder paths to scan (recursively) for .rfa files.")]
            public List<string>? Folders { get; set; }
        }
    }
}
