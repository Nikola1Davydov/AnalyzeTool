using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;

namespace AnalyseTool.Features.Families
{
    /// <summary>
    /// Renders a small PNG thumbnail (returned as a base64 data URI) for one family, via the Revit
    /// preview image of its first type. Read-only and stateless — the WebView caches the result in
    /// IndexedDB, so a cache hit never reaches this command (no Revit-thread round-trip).
    /// </summary>
    [RevitCommand(
        Description = "Renders a small PNG thumbnail (base64 data URI) from the Revit preview image of a " +
                      "family (its first type) OR a specific type/system type. Read-only. Pass a family id " +
                      "or a type (ElementType) id.",
        ReadOnly = true,
        InputType = typeof(GetFamilyPreview.Request))]
    internal sealed class GetFamilyPreview : IRevitTask
    {
        private const int DefaultSize = 256;

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (req is null || req.Id <= 0)
                return Task.FromResult<object?>(new { id = req?.Id ?? 0, dataUri = (string?)null });

            int size = req.Size is int s and >= 32 and <= 512 ? s : DefaultSize;

            return ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;
                return new { id = req.Id, dataUri = Render(doc, req.Id, size) };
            });
        }

        private static string? Render(Document doc, long id, int size)
        {
            // GetPreviewImage lives on ElementType. The id may be a TYPE directly (FamilySymbol or a system
            // type like WallType) — use it as-is — or a FAMILY, in which case its first type represents it.
            Element? element = doc.GetElement(new ElementId(id));
            ElementType? type = element as ElementType;
            if (type is null && element is Family family)
            {
                ElementId symbolId = family.GetFamilySymbolIds().FirstOrDefault() ?? ElementId.InvalidElementId;
                type = doc.GetElement(symbolId) as ElementType;
            }
            if (type is null) return null;

            try
            {
                using System.Drawing.Bitmap? bitmap = type.GetPreviewImage(new System.Drawing.Size(size, size));
                if (bitmap is null) return null;

                using MemoryStream ms = new();
                bitmap.Save(ms, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
            catch
            {
                return null; // preview is best-effort — the card falls back to a placeholder tile
            }
        }

        internal sealed class Request
        {
            [Description("Revit ElementId (long) of the family, as returned by GetFamilies.")]
            public long Id { get; set; }

            [Description("Optional square thumbnail size in pixels (32–512, default 256).")]
            public int? Size { get; set; }
        }
    }
}
