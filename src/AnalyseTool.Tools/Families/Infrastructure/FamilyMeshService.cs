using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Infrastructure
{
    /// <summary>
    /// Tessellates a family's 3D geometry into compact triangle-mesh parts (one per material, carrying an
    /// approximate colour + opacity) for the Three.js gallery viewer. Read-only with respect to the
    /// project. For loadable families it reads geometry straight from the family's own document
    /// (<see cref="Document.EditFamily"/>) so even families with no placed instance get a preview;
    /// EditFamily returns a separate in-memory document (no project write) which we always close. In-place
    /// families can't be opened that way, so they fall back to their placed instance. We never temp-place
    /// an instance (that would be a model write). Nested family instances are positioned correctly by
    /// composing each <see cref="GeometryInstance.Transform"/> down the recursion.
    /// </summary>
    public sealed class FamilyMeshService
    {
        private static readonly Options GeometryOptions = new()
        {
            DetailLevel = ViewDetailLevel.Fine,
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
        };

        // Fallback shading when a face has no material (or an invalid colour).
        private static readonly int[] DefaultColor = { 154, 166, 178 };

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

                Dictionary<long, PartBuilder> parts = new();
                foreach (Element element in new FilteredElementCollector(familyDoc).WhereElementIsNotElementType())
                {
                    // Per-element guard: a single unreadable element must not abort the whole family.
                    try
                    {
                        GeometryElement? geometry = element.get_Geometry(GeometryOptions);
                        if (geometry is null) continue;
                        foreach (GeometryObject obj in geometry)
                            Collect(obj, Transform.Identity, familyDoc, parts);
                    }
                    catch { /* skip this element */ }
                }

                return Build(parts) ?? FamilyMesh.Unavailable("No solid geometry in the family document.");
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

            Dictionary<long, PartBuilder> parts = new();
            try
            {
                GeometryElement? geometry = instance.get_Geometry(GeometryOptions);
                if (geometry is not null)
                    foreach (GeometryObject obj in geometry)
                        Collect(obj, Transform.Identity, doc, parts);
            }
            catch { /* fall through to whatever was collected */ }

            return Build(parts) ?? FamilyMesh.Unavailable("No solid geometry found.");
        }

        private static FamilyMesh? Build(Dictionary<long, PartBuilder> parts)
        {
            List<FamilyMeshPart> result = parts.Values
                .Where(p => p.Indices.Count > 0)
                .Select(p => new FamilyMeshPart(p.Color, p.Opacity, p.Positions, p.Indices))
                .ToList();
            return result.Count == 0 ? null : new FamilyMesh(true, null, result);
        }

        private static void Collect(
            GeometryObject obj, Transform transform, Document doc, Dictionary<long, PartBuilder> parts)
        {
            switch (obj)
            {
                // Nested instance: compose its transform so its geometry lands where it actually sits
                // (otherwise every nested family stacks at the family origin).
                case GeometryInstance gi:
                    Transform composed = transform.Multiply(gi.Transform);
                    foreach (GeometryObject inner in gi.GetSymbolGeometry())
                        Collect(inner, composed, doc, parts);
                    break;
                case Solid solid when solid.Faces.Size > 0 && solid.Volume > 0:
                    foreach (Face face in solid.Faces)
                        AddFace(face, transform, doc, parts);
                    break;
            }
        }

        private static void AddFace(
            Face face, Transform transform, Document doc, Dictionary<long, PartBuilder> parts)
        {
            Mesh mesh = face.Triangulate();
            if (mesh is null) return;

            // Reading the face material can throw for some faces; fall back to the default part instead.
            long materialId = -1;
            try { materialId = face.MaterialElementId?.Value ?? -1; }
            catch { materialId = -1; }

            PartBuilder pb = GetOrCreatePart(parts, materialId, doc);

            int baseIndex = pb.Positions.Count / 3;
            foreach (XYZ v in mesh.Vertices)
            {
                XYZ p = transform.OfPoint(v);
                pb.Positions.Add(Math.Round(p.X, 4));
                pb.Positions.Add(Math.Round(p.Y, 4));
                pb.Positions.Add(Math.Round(p.Z, 4));
            }
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                MeshTriangle t = mesh.get_Triangle(i);
                pb.Indices.Add(baseIndex + (int)t.get_Index(0));
                pb.Indices.Add(baseIndex + (int)t.get_Index(1));
                pb.Indices.Add(baseIndex + (int)t.get_Index(2));
            }
        }

        private static PartBuilder GetOrCreatePart(
            Dictionary<long, PartBuilder> parts, long materialId, Document doc)
        {
            if (parts.TryGetValue(materialId, out PartBuilder? existing)) return existing;

            int[] color = DefaultColor;
            double opacity = 1.0;
            try
            {
                if (materialId != -1 && doc.GetElement(new ElementId(materialId)) is Material material)
                {
                    Color c = material.Color;
                    if (c is not null && c.IsValid)
                        color = new[] { (int)c.Red, (int)c.Green, (int)c.Blue };
                    // Revit transparency is 0 (opaque) .. 100 (clear); floor it so glass stays visible.
                    opacity = Math.Clamp(1.0 - material.Transparency / 100.0, 0.1, 1.0);
                }
            }
            catch { /* unreadable material → default grey, opaque */ }

            PartBuilder pb = new() { Color = color, Opacity = opacity };
            parts[materialId] = pb;
            return pb;
        }

        private sealed class PartBuilder
        {
            public List<double> Positions { get; } = new();
            public List<int> Indices { get; } = new();
            public int[] Color { get; set; } = DefaultColor;
            public double Opacity { get; set; } = 1.0;
        }
    }

    public sealed record FamilyMeshPart(
        [property: JsonProperty("color")] IReadOnlyList<int> Color,
        [property: JsonProperty("opacity")] double Opacity,
        [property: JsonProperty("positions")] IReadOnlyList<double> Positions,
        [property: JsonProperty("indices")] IReadOnlyList<int> Indices);

    public sealed record FamilyMesh(
        [property: JsonProperty("available")] bool Available,
        [property: JsonProperty("reason")] string? Reason,
        [property: JsonProperty("parts")] IReadOnlyList<FamilyMeshPart>? Parts)
    {
        public static FamilyMesh Unavailable(string reason) => new(false, reason, null);
    }
}
