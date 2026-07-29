using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Doom
{
    /// <summary>What <see cref="DoomLevelBuilder"/> needs to know: target level/types and the scale.
    /// <c>FeetPerDoomUnit</c> converts Doom's 16-bit map units into Revit internal units (feet); the
    /// chosen level's elevation is treated as Doom world z = 0.</summary>
    internal sealed record DoomBuildOptions(
        Level Level,
        ElementId WallTypeId,
        ElementId FloorTypeId,
        double FeetPerDoomUnit,
        bool BuildCeilings);

    /// <summary>Counts of what a build produced (and skipped), for the command result.</summary>
    internal sealed class DoomBuildResult
    {
        public int WallsCreated;
        public int FloorsCreated;
        public int CeilingsCreated;
        public int WallsSkipped;
        public int FloorsSkipped;
        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// Turns a parsed <see cref="DoomMap"/> into native Revit geometry, inside the caller's open
    /// transaction: one-sided linedefs become full-height walls, two-sided linedefs become the step
    /// (floor-height difference) and lintel (ceiling-height difference) walls between their sectors,
    /// and every sector becomes a floor slab from its traced boundary loops (holes included). All
    /// Revit API work — must run inside <c>RunInRevitAsync</c> with a transaction already started.
    /// </summary>
    internal sealed class DoomLevelBuilder
    {
        /// <summary>Anything shorter/thinner than half a Doom map unit is map-data noise, not geometry.</summary>
        private const double MinDoomUnits = 0.5;

        private readonly Document _doc;
        private readonly DoomMap _map;
        private readonly DoomBuildOptions _opt;
        private readonly double _shortCurve;

        public DoomLevelBuilder(Document doc, DoomMap map, DoomBuildOptions options)
        {
            _doc = doc;
            _map = map;
            _opt = options;
            _shortCurve = doc.Application.ShortCurveTolerance;
        }

        public DoomBuildResult Build(Action<double, string>? progress, CancellationToken ct)
        {
            var result = new DoomBuildResult();

            Dictionary<int, List<SectorLoop>> loops = SectorLoopBuilder.BuildLoops(_map, result.Warnings);

            int total = _map.Linedefs.Count + loops.Count;
            int done = 0;

            foreach (DoomLinedef line in _map.Linedefs)
            {
                if (++done % 50 == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Invoke((double)done / total, $"Walls… {done}/{_map.Linedefs.Count}");
                }
                BuildWalls(line, result);
            }

            foreach ((int sectorIndex, List<SectorLoop> sectorLoops) in loops)
            {
                if (++done % 20 == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Invoke((double)done / total, $"Floors… sector {sectorIndex}");
                }
                BuildSlabs(sectorIndex, sectorLoops, result);
            }

            return result;
        }

        private void BuildWalls(DoomLinedef line, DoomBuildResult result)
        {
            int rightSector = SectorOf(line.RightSide);
            int leftSector = SectorOf(line.LeftSide);
            if (rightSector < 0 && leftSector < 0) return;

            XYZ p1 = ToXyz(_map.Vertices[line.V1]);
            XYZ p2 = ToXyz(_map.Vertices[line.V2]);
            if (p1.DistanceTo(p2) < Math.Max(_shortCurve, MinDoomUnits * _opt.FeetPerDoomUnit)) return;

            if (rightSector < 0 || leftSector < 0)
            {
                // One-sided: a solid wall over the full floor-to-ceiling extent of its sector.
                DoomSector s = _map.Sectors[rightSector >= 0 ? rightSector : leftSector];
                CreateWall(p1, p2, s.FloorHeight, s.CeilingHeight, result);
                return;
            }

            if (rightSector == leftSector) return;

            // Two-sided: the opening between the sectors stays open; only the height MISMATCHES are
            // solid — the step between the two floors and the lintel between the two ceilings.
            DoomSector a = _map.Sectors[rightSector];
            DoomSector b = _map.Sectors[leftSector];
            CreateWall(p1, p2, Math.Min(a.FloorHeight, b.FloorHeight), Math.Max(a.FloorHeight, b.FloorHeight), result);
            CreateWall(p1, p2, Math.Min(a.CeilingHeight, b.CeilingHeight), Math.Max(a.CeilingHeight, b.CeilingHeight), result);
        }

        private void CreateWall(XYZ p1, XYZ p2, int bottomDoom, int topDoom, DoomBuildResult result)
        {
            if (topDoom - bottomDoom < MinDoomUnits) return;

            try
            {
                Line locationLine = Line.CreateBound(p1, p2);
                Wall.Create(_doc, locationLine, _opt.WallTypeId, _opt.Level.Id,
                    height: (topDoom - bottomDoom) * _opt.FeetPerDoomUnit,
                    offset: bottomDoom * _opt.FeetPerDoomUnit,
                    flip: false, structural: false);
                result.WallsCreated++;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.WallsSkipped++;
                AddWarning(result, $"Wall skipped: {ex.Message}");
            }
        }

        private void BuildSlabs(int sectorIndex, List<SectorLoop> loops, DoomBuildResult result)
        {
            DoomSector sector = _map.Sectors[sectorIndex];
            List<(CurveLoop Loop, List<int> Chain, bool IsHole)> curveLoops = new();
            foreach (SectorLoop loop in loops)
            {
                CurveLoop? cl = ToCurveLoop(loop.VertexChain);
                if (cl != null) curveLoops.Add((cl, loop.VertexChain, loop.IsHole));
            }

            // One floor per outer boundary, with the holes that geometrically lie inside it. A Doom
            // sector number is often reused for several disjoint areas — those become separate slabs.
            foreach ((CurveLoop outer, List<int> outerChain, bool isHole) in curveLoops)
            {
                if (isHole) continue;

                var profile = new List<CurveLoop> { outer };
                foreach ((CurveLoop hole, List<int> holeChain, bool holeFlag) in curveLoops)
                    if (holeFlag && ContainsPoint(outerChain, _map.Vertices[holeChain[0]]))
                        profile.Add(hole);

                CreateSlab(profile, sector.FloorHeight, sectorIndex, "floor", result, isCeiling: false);
                if (_opt.BuildCeilings)
                    CreateSlab(CloneProfile(profile), sector.CeilingHeight, sectorIndex, "ceiling", result, isCeiling: true);
            }
        }

        private void CreateSlab(List<CurveLoop> profile, int doomHeight, int sectorIndex, string what,
            DoomBuildResult result, bool isCeiling)
        {
            try
            {
                Floor floor = Floor.Create(_doc, profile, _opt.FloorTypeId, _opt.Level.Id);
                floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)
                    ?.Set(doomHeight * _opt.FeetPerDoomUnit);
                if (isCeiling) result.CeilingsCreated++; else result.FloorsCreated++;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.FloorsSkipped++;
                AddWarning(result, $"Sector {sectorIndex} {what} skipped: {ex.Message}");
            }
        }

        /// <summary>Builds a closed horizontal CurveLoop at z=0 from a vertex chain, dropping
        /// segments below the short-curve tolerance. Null when fewer than 3 usable points remain.</summary>
        private CurveLoop? ToCurveLoop(List<int> chain)
        {
            var points = new List<XYZ>();
            foreach (int v in chain)
            {
                XYZ p = ToXyz(_map.Vertices[v]);
                if (points.Count == 0 || points[^1].DistanceTo(p) > _shortCurve) points.Add(p);
            }
            while (points.Count >= 3 && points[^1].DistanceTo(points[0]) <= _shortCurve)
                points.RemoveAt(points.Count - 1);
            if (points.Count < 3) return null;

            var loop = new CurveLoop();
            for (int i = 0; i < points.Count; i++)
                loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
            return loop;
        }

        /// <summary>Floor.Create consumes its profile loops, so the ceiling needs fresh ones.</summary>
        private static List<CurveLoop> CloneProfile(List<CurveLoop> profile) =>
            profile.Select(CurveLoop.CreateViaCopy).ToList();

        /// <summary>Even-odd ray cast: is the point inside the polygon given by the vertex chain?</summary>
        private bool ContainsPoint(List<int> chain, DoomVertex point)
        {
            bool inside = false;
            for (int i = 0, j = chain.Count - 1; i < chain.Count; j = i++)
            {
                DoomVertex a = _map.Vertices[chain[i]];
                DoomVertex b = _map.Vertices[chain[j]];
                if (a.Y > point.Y != b.Y > point.Y &&
                    point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        private int SectorOf(int sidedefIndex) =>
            sidedefIndex >= 0 && sidedefIndex < _map.Sidedefs.Count ? _map.Sidedefs[sidedefIndex].SectorIndex : -1;

        private XYZ ToXyz(DoomVertex v) => new(v.X * _opt.FeetPerDoomUnit, v.Y * _opt.FeetPerDoomUnit, 0);

        private static void AddWarning(DoomBuildResult result, string message)
        {
            const int cap = 25;
            if (result.Warnings.Count < cap) result.Warnings.Add(message);
            else if (result.Warnings.Count == cap) result.Warnings.Add("… further warnings truncated.");
        }
    }
}
