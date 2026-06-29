using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Infrastructure
{
    /// <summary>
    /// Tessellates a family's 3D geometry into a compact triangle mesh for the Three.js gallery viewer.
    /// Read-only with respect to the project. For loadable families it reads geometry straight from the
    /// family's own document (<see cref="Document.EditFamily"/>) so even families with no placed instance
    /// get a preview; EditFamily returns a separate in-memory document (no project write) which we always
    /// close. In-place families can't be opened that way, so they fall back to their placed instance. We
    /// deliberately never temp-place an instance (that would be a model write).
    /// </summary>
    public sealed class FamilyMeshService
    {
        private static readonly Options GeometryOptions = new()
        {
            DetailLevel = ViewDetailLevel.Fine,
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
        };

        public FamilyMesh Extract(Document doc, long familyId)
        {
            if (doc.GetElement(new ElementId(familyId)) is not Family family)
                return FamilyMesh.Unavailable("Family not found.");

            // Loadable families: read from the family's own document (works with zero placed instances).
            if (!family.IsInPlace)
            {
                FamilyMesh fromDoc = TryExtractFromFamilyDocument(doc, family);
                if (fromDoc.Available) return fromDoc;
            }

            // In-place families, or a loadable family whose document couldn't be read.
            return ExtractFromInstance(doc, family);
        }

        private static FamilyMesh TryExtractFromFamilyDocument(Document doc, Family family)
        {
            Document? familyDoc = null;
            try
            {
                familyDoc = doc.EditFamily(family);
                if (familyDoc is null)
                    return FamilyMesh.Unavailable("Could not open the family document.");

                List<double> positions = new();
                List<int> indices = new();

                foreach (Element element in new FilteredElementCollector(familyDoc).WhereElementIsNotElementType())
                {
                    GeometryElement? geometry = element.get_Geometry(GeometryOptions);
                    if (geometry is null) continue;
                    foreach (GeometryObject obj in geometry)
                        Collect(obj, positions, indices);
                }

                return indices.Count == 0
                    ? FamilyMesh.Unavailable("No solid geometry in the family document.")
                    : new FamilyMesh(true, null, positions, indices);
            }
            catch
            {
                return FamilyMesh.Unavailable("The family document could not be read.");
            }
            finally
            {
                try { familyDoc?.Close(false); } catch { /* best-effort */ }
            }
        }

        private static FamilyMesh ExtractFromInstance(Document doc, Family family)
        {
            HashSet<long> symbolIds = family.GetFamilySymbolIds().Select(s => s.Value).ToHashSet();
            FamilyInstance? instance = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .FirstOrDefault(fi => fi.Symbol is not null &&
                    (symbolIds.Contains(fi.Symbol.Id.Value) || fi.Symbol.Family?.Id == family.Id));

            if (instance is null)
                return FamilyMesh.Unavailable("No placed instance to extract 3D geometry from.");

            List<double> positions = new();
            List<int> indices = new();
            GeometryElement? geometry = instance.get_Geometry(GeometryOptions);
            if (geometry is not null)
                foreach (GeometryObject obj in geometry)
                    Collect(obj, positions, indices);

            return indices.Count == 0
                ? FamilyMesh.Unavailable("No solid geometry found.")
                : new FamilyMesh(true, null, positions, indices);
        }

        private static void Collect(GeometryObject obj, List<double> positions, List<int> indices)
        {
            switch (obj)
            {
                // Symbol-local geometry: family-origin coordinates, no instance world-transform applied.
                case GeometryInstance gi:
                    foreach (GeometryObject inner in gi.GetSymbolGeometry())
                        Collect(inner, positions, indices);
                    break;
                case Solid solid when solid.Faces.Size > 0 && solid.Volume > 0:
                    foreach (Face face in solid.Faces)
                        AddFace(face, positions, indices);
                    break;
            }
        }

        private static void AddFace(Face face, List<double> positions, List<int> indices)
        {
            Mesh mesh = face.Triangulate();
            if (mesh is null) return;

            int baseIndex = positions.Count / 3;
            foreach (XYZ v in mesh.Vertices)
            {
                positions.Add(Math.Round(v.X, 4));
                positions.Add(Math.Round(v.Y, 4));
                positions.Add(Math.Round(v.Z, 4));
            }
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                MeshTriangle t = mesh.get_Triangle(i);
                indices.Add(baseIndex + (int)t.get_Index(0));
                indices.Add(baseIndex + (int)t.get_Index(1));
                indices.Add(baseIndex + (int)t.get_Index(2));
            }
        }
    }

    public sealed record FamilyMesh(
        [property: JsonProperty("available")] bool Available,
        [property: JsonProperty("reason")] string? Reason,
        [property: JsonProperty("positions")] IReadOnlyList<double>? Positions,
        [property: JsonProperty("indices")] IReadOnlyList<int>? Indices)
    {
        public static FamilyMesh Unavailable(string reason) => new(false, reason, null, null);
    }
}
