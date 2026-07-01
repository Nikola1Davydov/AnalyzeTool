using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Features.Families
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
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
            {
                var families = new LibraryService().List(app.ActiveUIDocument.Document, req.Folders ?? new List<string>());
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
