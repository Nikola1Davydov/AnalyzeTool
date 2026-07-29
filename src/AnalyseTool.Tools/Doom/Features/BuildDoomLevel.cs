using AnalyseTool.Sdk;
using AnalyseTool.Tools.Families;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.Doom
{
    /// <summary>
    /// Builds the geometry of a classic Doom map as NATIVE Revit elements: walls from linedefs
    /// (solid one-sided walls, plus step/lintel walls where two sectors meet at different heights)
    /// and floor slabs from the sector polygons — so the level can be walked in a Revit 3D view or
    /// exported like any other model. The WAD is parsed off the Revit thread; only element creation
    /// runs inside <c>RunInRevitAsync</c>, in a single transaction (one Ctrl+Z removes the level).
    /// </summary>
    [RevitCommand(
        Description = "Builds a classic Doom map (from a WAD file) as native Revit geometry: walls from " +
                      "linedefs, floors (optionally ceilings) from sectors, on the given or lowest level. " +
                      "Payload: { wadPath, mapName (default E1M1), metersPerDoomUnit (default 0.03125), " +
                      "levelId?, wallTypeId?, floorTypeId?, buildCeilings? }. Modifies the model in one transaction.",
        InputType = typeof(BuildDoomLevel.Request))]
    internal sealed class BuildDoomLevel : IRevitTask, IProgressAware
    {
        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            if (string.IsNullOrWhiteSpace(req.WadPath) || !File.Exists(req.WadPath))
                return new { ok = false, error = $"WAD file not found: '{req.WadPath}'." };
            if (req.MetersPerDoomUnit <= 0)
                return new { ok = false, error = "metersPerDoomUnit must be positive." };

            Progress?.Report(new ProgressInfo(0.02, $"Parsing {Path.GetFileName(req.WadPath)}…"));

            DoomMap map;
            try
            {
                map = WadReader.ReadMap(req.WadPath, req.MapName);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return new { ok = false, error = ex.Message };
            }

            // Doom map units → Revit internal units (feet).
            double feetPerDoomUnit = req.MetersPerDoomUnit / 0.3048;

            return await ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;

                Level? level = ResolveLevel(doc, req.LevelId);
                if (level == null)
                    return new { ok = false, error = "The document has no levels — open a project document." };

                ElementId wallTypeId = ResolveType(doc, req.WallTypeId, ElementTypeGroup.WallType, typeof(WallType));
                ElementId floorTypeId = ResolveType(doc, req.FloorTypeId, ElementTypeGroup.FloorType, typeof(FloorType));
                if (wallTypeId == ElementId.InvalidElementId || floorTypeId == ElementId.InvalidElementId)
                    return new { ok = false, error = "No wall or floor type available in this document." };

                var options = new DoomBuildOptions(level, wallTypeId, floorTypeId, feetPerDoomUnit, req.BuildCeilings);
                var builder = new DoomLevelBuilder(doc, map, options);

                using Transaction t = new(doc, $"Doom: build {map.Name}");
                t.Start();
                SwallowWarningsPreprocessor.Apply(t);

                DoomBuildResult built = builder.Build(
                    (fraction, message) => Progress?.Report(new ProgressInfo(0.05 + fraction * 0.9, message)),
                    ct);

                Progress?.Report(new ProgressInfo(0.97, "Committing…"));
                t.Commit();

                return new
                {
                    ok = true,
                    map = map.Name,
                    level = level.Name,
                    wallsCreated = built.WallsCreated,
                    floorsCreated = built.FloorsCreated,
                    ceilingsCreated = built.CeilingsCreated,
                    wallsSkipped = built.WallsSkipped,
                    floorsSkipped = built.FloorsSkipped,
                    warnings = built.Warnings
                };
            });
        }

        private static Level? ResolveLevel(Document doc, long levelId)
        {
            if (levelId > 0) return doc.GetElement(new ElementId(levelId)) as Level;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation)
                .FirstOrDefault();
        }

        /// <summary>Explicit type id → document default → first type of the class, in that order.</summary>
        private static ElementId ResolveType(Document doc, long requested, ElementTypeGroup group, Type typeClass)
        {
            if (requested > 0 && doc.GetElement(new ElementId(requested)) is ElementType requestedType)
                return requestedType.Id;

            ElementId defaultId = doc.GetDefaultElementTypeId(group);
            if (defaultId != ElementId.InvalidElementId && doc.GetElement(defaultId) != null)
                return defaultId;

            return new FilteredElementCollector(doc).OfClass(typeClass)
                .FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
        }

        public sealed class Request
        {
            [JsonProperty("wadPath")]
            [Description("Absolute path to the WAD file (e.g. the shareware DOOM1.WAD).")]
            public string WadPath { get; set; } = string.Empty;

            [JsonProperty("mapName")]
            [Description("Map marker lump to build: 'E1M1'…'E4M9' (Doom) or 'MAP01'…'MAP32' (Doom II).")]
            public string MapName { get; set; } = "E1M1";

            [JsonProperty("metersPerDoomUnit")]
            [Description("Scale: meters per Doom map unit. Default 0.03125 (32 units/m), the common convention.")]
            public double MetersPerDoomUnit { get; set; } = 0.03125;

            [JsonProperty("levelId")]
            [Description("Revit Level to build on (its elevation becomes Doom z=0). 0 = lowest level in the document.")]
            public long LevelId { get; set; }

            [JsonProperty("wallTypeId")]
            [Description("WallType to use. 0 = the document's default wall type.")]
            public long WallTypeId { get; set; }

            [JsonProperty("floorTypeId")]
            [Description("FloorType to use for floors and ceilings. 0 = the document's default floor type.")]
            public long FloorTypeId { get; set; }

            [JsonProperty("buildCeilings")]
            [Description("Also build each sector's ceiling as a slab at its ceiling height. Default false.")]
            public bool BuildCeilings { get; set; }
        }
    }
}
