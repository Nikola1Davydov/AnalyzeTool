using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Underlays
{
    /// <summary>One geometry object of a CAD import, with the transform that places it in the project
    /// and the block it sits in.</summary>
    internal readonly record struct CadItem(GeometryObject Object, Transform Transform, ElementId StyleId, string? Block);

    /// <summary>
    /// Flattens the geometry of an <see cref="ImportInstance"/>: the import is one
    /// <see cref="GeometryInstance"/> (the file) whose symbol geometry holds the primitives and, for
    /// every block reference, a nested GeometryInstance. Transforms are ACCUMULATED down that tree
    /// instead of relying on GetInstanceGeometry, whose frame for a nested instance depends on how it
    /// was reached — here every item carries the one transform that maps it into project coordinates.
    /// </summary>
    internal static class CadGeometryWalker
    {
        private const int MaxDepth = 16;

        /// <param name="cad">The import.</param>
        /// <param name="toHost">Applied on top of everything: the link transform for an import that lives in
        /// a Revit link, so its items land in the host's coordinates. Null for an import of the host.</param>
        public static List<CadItem> Walk(ImportInstance cad, Transform? toHost = null)
        {
            List<CadItem> items = new();
            GeometryElement? root = RootGeometry(cad);
            string? fileName = cad.Document.GetElement(cad.GetTypeId())?.Name;
            if (root is not null) Visit(root, toHost ?? Transform.Identity, 0, null, fileName, items);
            return items;
        }

        /// <summary>The primitive type an object is reported as, or null for objects that are not
        /// reported (an empty solid — Revit hands out plenty of those for DWG content).</summary>
        public static string? Classify(GeometryObject obj) => obj switch
        {
            GeometryInstance => "block",
            Line => "line",
            Arc arc => arc.IsBound ? "arc" : "circle",
            Ellipse => "ellipse",
            NurbSpline or HermiteSpline => "spline",
            Curve => "curve",
            PolyLine => "polyline",
            Point => "point",
            Solid solid => solid.Faces.IsEmpty && solid.Edges.IsEmpty ? null : "solid",
            Mesh => "mesh",
            _ => null,
        };

        private static GeometryElement? RootGeometry(ImportInstance cad)
        {
            // Model options first: they return every layer, including the ones a view hides. A 2D
            // "current view only" import may answer nothing without a view, so fall back to its owner
            // view — which then yields only what that view shows.
            GeometryElement? geometry = Get(cad, new Options());
            if (HasContent(geometry)) return geometry;

            View? owner = cad.ViewSpecific ? cad.Document.GetElement(cad.OwnerViewId) as View : null;
            return owner is null ? geometry : Get(cad, new Options { View = owner });

            static GeometryElement? Get(ImportInstance cad, Options options)
            {
                try { return cad.get_Geometry(options); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
            }

            static bool HasContent(GeometryElement? geometry)
            {
                if (geometry is null) return false;
                using IEnumerator<GeometryObject> e = geometry.GetEnumerator();
                return e.MoveNext();
            }
        }

        private static void Visit(GeometryElement geometry, Transform transform, int depth, string? block, string? fileName, List<CadItem> items)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is GeometryInstance instance)
                {
                    // Depth 0 is the file itself, not a block reference in it.
                    string? name = depth == 0 ? block : BlockName(instance, fileName) ?? block;
                    if (depth > 0) items.Add(new CadItem(instance, transform, instance.GraphicsStyleId, name));
                    if (depth >= MaxDepth) continue;

                    GeometryElement? symbol = SymbolGeometry(instance);
                    if (symbol is not null) Visit(symbol, transform.Multiply(instance.Transform), depth + 1, name, fileName, items);
                    continue;
                }
                items.Add(new CadItem(obj, transform, obj.GraphicsStyleId, block));
            }
        }

        private static GeometryElement? SymbolGeometry(GeometryInstance instance)
        {
            try { return instance.GetSymbolGeometry(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        private static string? BlockName(GeometryInstance instance, string? fileName)
        {
            // DWG block references come through as nested instances; whether their symbol carries the
            // block name depends on the file and the Revit version — use it when it is there. A symbol
            // that is the import's own type names the FILE, which is no block name at all.
            try
            {
                using SymbolGeometryId id = instance.GetSymbolGeometryId();
                string? name = instance.GetDocument()?.GetElement(id.SymbolId)?.Name;
                return string.IsNullOrWhiteSpace(name) || string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase) ? null : name;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }
    }
}
