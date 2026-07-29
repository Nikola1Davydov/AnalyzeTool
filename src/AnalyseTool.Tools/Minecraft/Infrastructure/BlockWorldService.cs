using AnalyseTool.Tools.Families;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.Tools.Minecraft
{
    /// <summary>One block to place: integer cell coordinates in MINECRAFT convention (y is up).</summary>
    internal readonly record struct BlockPlacement(int X, int Y, int Z, string Block);

    /// <summary>What a placement run produced, for the command result.</summary>
    internal sealed class BlockPlaceResult
    {
        public int Created;
        public int Failed;
        public string Mode = "family";
        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// Places and clears block cubes. Blocks become instances of the cube family (material set per
    /// instance); when no family template is available they fall back to per-block DirectShapes with
    /// the material baked into the solid. Every created element is tagged via its Comments parameter
    /// (<c>MC:&lt;block&gt;</c>) so ClearRegion touches only OUR blocks, never the user's model.
    /// Minecraft axes (x east, y UP, z south) map to Revit as X=x, Y=z, Z=y; a block occupies the
    /// cube whose MIN corner is its cell origin.
    /// </summary>
    internal sealed class BlockWorldService
    {
        public const string CommentPrefix = "MC:";

        private readonly Document _doc;
        private readonly double _size;

        public BlockWorldService(Document doc, double blockSizeFeet)
        {
            _doc = doc;
            _size = blockSizeFeet;
        }

        /// <summary>Runs the whole placement: family lookup/authoring OUTSIDE the transaction (family
        /// load manages its own), then one transaction for materials + all blocks.</summary>
        public BlockPlaceResult PlaceBlocks(UIApplication app, IReadOnlyList<BlockPlacement> blocks,
            Action<double, string>? progress, CancellationToken ct)
        {
            var result = new BlockPlaceResult();

            FamilySymbol? symbol = BlockFamilyService.EnsureBlockSymbol(app.Application, _doc, _size);
            if (symbol == null)
            {
                result.Mode = "directShape";
                result.Warnings.Add("No Generic Model family template found — blocks are DirectShapes " +
                                    "(solid geometry, material baked in, no per-instance material parameter).");
            }

            using Transaction t = new(_doc, $"Minecraft: place {blocks.Count} blocks");
            t.Start();
            SwallowWarningsPreprocessor.Apply(t);

            var materials = new BlockMaterialService(_doc);
            if (symbol != null && !symbol.IsActive) symbol.Activate();

            for (int i = 0; i < blocks.Count; i++)
            {
                if (i % 200 == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Invoke((double)i / blocks.Count, $"Blocks… {i}/{blocks.Count}");
                }

                BlockPlacement block = blocks[i];
                ElementId materialId = materials.EnsureMaterial(block.Block);
                try
                {
                    Element element = symbol != null
                        ? PlaceFamilyBlock(symbol, block, materialId)
                        : PlaceDirectShapeBlock(block, materialId);
                    element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                        ?.Set(CommentPrefix + block.Block);
                    result.Created++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.Failed++;
                    if (result.Warnings.Count < 20)
                        result.Warnings.Add($"Block ({block.X},{block.Y},{block.Z}) failed: {ex.Message}");
                }
            }

            progress?.Invoke(0.98, "Committing…");
            t.Commit();
            return result;
        }

        private Element PlaceFamilyBlock(FamilySymbol symbol, BlockPlacement block, ElementId materialId)
        {
            FamilyInstance instance = _doc.Create.NewFamilyInstance(
                CellOrigin(block), symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            instance.LookupParameter(BlockFamilyService.MaterialParamName)?.Set(materialId);
            return instance;
        }

        private Element PlaceDirectShapeBlock(BlockPlacement block, ElementId materialId)
        {
            XYZ o = CellOrigin(block);
            var square = new CurveLoop();
            XYZ[] corners =
            {
                o, new(o.X + _size, o.Y, o.Z), new(o.X + _size, o.Y + _size, o.Z), new(o.X, o.Y + _size, o.Z)
            };
            for (int i = 0; i < 4; i++)
                square.Append(Line.CreateBound(corners[i], corners[(i + 1) % 4]));

            Solid cube = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { square }, XYZ.BasisZ, _size,
                new SolidOptions(materialId, ElementId.InvalidElementId));

            DirectShape shape = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.SetShape(new List<GeometryObject> { cube });
            return shape;
        }

        /// <summary>Deletes OUR blocks (Comments starting with <c>MC:</c>) whose cells intersect the
        /// given Minecraft-coordinate box. Runs its own transaction.</summary>
        public int ClearRegion(int x1, int y1, int z1, int x2, int y2, int z2)
        {
            XYZ min = new(Math.Min(x1, x2) * _size, Math.Min(z1, z2) * _size, Math.Min(y1, y2) * _size);
            XYZ max = new((Math.Max(x1, x2) + 1) * _size, (Math.Max(z1, z2) + 1) * _size, (Math.Max(y1, y2) + 1) * _size);

            // Shrink slightly so blocks merely TOUCHING the region from outside are not caught.
            double e = _size * 0.05;
            var outline = new Outline(new XYZ(min.X + e, min.Y + e, min.Z + e), new XYZ(max.X - e, max.Y - e, max.Z - e));

            List<ElementId> doomed = new FilteredElementCollector(_doc)
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Where(el => el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()
                    ?.StartsWith(CommentPrefix, StringComparison.Ordinal) == true)
                .Select(el => el.Id)
                .ToList();

            if (doomed.Count == 0) return 0;

            using Transaction t = new(_doc, $"Minecraft: clear {doomed.Count} blocks");
            t.Start();
            SwallowWarningsPreprocessor.Apply(t);
            _doc.Delete(doomed);
            t.Commit();
            return doomed.Count;
        }

        private XYZ CellOrigin(BlockPlacement b) => new(b.X * _size, b.Z * _size, b.Y * _size);
    }
}
