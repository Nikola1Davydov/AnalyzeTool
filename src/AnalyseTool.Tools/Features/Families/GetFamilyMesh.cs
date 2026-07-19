using AnalyseTool.Tools.Infrastructure;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Features.Families
{
    /// <summary>
    /// Read-only: a compact triangle mesh ({ positions[], indices[] }) of a family's 3D geometry for the
    /// Three.js gallery viewer, tessellated from an already-placed instance. Families with no instance
    /// return { available: false } (we don't temp-place one — that would be a model write). The WebView
    /// caches the mesh in IndexedDB, so a cache hit never reaches this command.
    /// </summary>
    [RevitCommand(
        Description = "Returns a triangle mesh (positions[], indices[]) of a family's 3D geometry for a " +
                      "viewer, tessellated from a placed instance. Read-only; { available:false } if the " +
                      "family has no placed instance. Pass the family id from GetFamilies.",
        ReadOnly = true,
        InputType = typeof(GetFamilyMesh.Request))]
    internal sealed class GetFamilyMesh : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            long id = req?.Id ?? 0;

            return ctx.RunInRevitAsync<object?>(app =>
                new FamilyMeshService().Extract(app.ActiveUIDocument.Document, id));
        }

        internal sealed class Request
        {
            [Description("Revit ElementId (long) of the family, as returned by GetFamilies.")]
            public long Id { get; set; }
        }
    }
}
