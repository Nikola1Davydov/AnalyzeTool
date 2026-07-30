using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>
    /// Reads the block world back OUT of the model: every MC-tagged element (family instance or
    /// DirectShape) becomes a cell + block type, derived from its bounding-box center — which works
    /// for corner-origin MC_Block instances, bottom-center user-family instances and DirectShapes
    /// alike. This is how the game panel restores its 3D world when it opens.
    /// </summary>
    [RevitCommand(
        Description = "Returns all Minecraft-style blocks placed in the model as cells: { blocks: [{x,y,z,block}] }. " +
                      "y is UP. Payload: { blockSizeMeters (default 1) — must match the size used when placing }.",
        ReadOnly = true,
        InputType = typeof(McGetBlocks.Request))]
    internal sealed class McGetBlocks : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();
            if (req.BlockSizeMeters <= 0)
                return Task.FromResult<object?>(new { ok = false, error = "blockSizeMeters must be positive." });

            double size = req.BlockSizeMeters / 0.3048;

            return ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;
                var blocks = new List<object>();

                var classes = new ElementMulticlassFilter(new List<Type> { typeof(FamilyInstance), typeof(DirectShape) });
                foreach (Element el in new FilteredElementCollector(doc).WherePasses(classes))
                {
                    string? comment = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                    if (comment == null || !comment.StartsWith(BlockWorldService.CommentPrefix, StringComparison.Ordinal))
                        continue;

                    BoundingBoxXYZ? box = el.get_BoundingBox(null);
                    if (box == null) continue;

                    XYZ center = (box.Min + box.Max) * 0.5;
                    blocks.Add(new
                    {
                        x = (int)Math.Floor(center.X / size),
                        y = (int)Math.Floor(center.Z / size), // Revit z is Minecraft y (up)
                        z = (int)Math.Floor(center.Y / size),
                        block = comment.Substring(BlockWorldService.CommentPrefix.Length),
                    });
                }

                return new { ok = true, count = blocks.Count, blocks };
            });
        }

        public sealed class Request
        {
            [JsonProperty("blockSizeMeters")]
            [Description("Edge length of one block in meters — must match the size used when placing. Default 1.")]
            public double BlockSizeMeters { get; set; } = 1.0;
        }
    }
}
