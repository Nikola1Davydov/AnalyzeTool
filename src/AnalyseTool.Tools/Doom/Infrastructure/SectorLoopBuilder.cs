namespace AnalyseTool.Tools.Doom
{
    /// <summary>One closed boundary loop of a sector, as an ordered chain of vertex indices.
    /// <see cref="IsHole"/> distinguishes an inner (hole) loop from an outer boundary.</summary>
    internal sealed record SectorLoop(List<int> VertexChain, bool IsHole);

    /// <summary>
    /// Reconstructs the closed floor polygons of each Doom sector from its linedefs. Doom does not
    /// store sector outlines explicitly — every linedef side just names the sector it faces — so the
    /// directed edges (sector always to the RIGHT of the edge direction) are chained end-to-start
    /// into loops. At a junction vertex the sharpest right turn is taken, which keeps touching loops
    /// from being merged. With this orientation an outer boundary winds clockwise (negative shoelace
    /// area) and a hole winds counter-clockwise. Pure 2D math — no Revit types.
    /// </summary>
    internal static class SectorLoopBuilder
    {
        public static Dictionary<int, List<SectorLoop>> BuildLoops(DoomMap map, List<string> warnings)
        {
            // Directed boundary edges per sector. A linedef whose both sides face the same sector is
            // internal decoration (self-referencing), not a boundary — skip it entirely.
            var edgesBySector = new Dictionary<int, List<(int From, int To)>>();
            foreach (DoomLinedef line in map.Linedefs)
            {
                int right = SectorOf(map, line.RightSide);
                int left = SectorOf(map, line.LeftSide);
                if (right == left) continue;
                if (right >= 0) Add(edgesBySector, right, (line.V1, line.V2));
                if (left >= 0) Add(edgesBySector, left, (line.V2, line.V1));
            }

            var result = new Dictionary<int, List<SectorLoop>>();
            foreach ((int sector, List<(int From, int To)> edges) in edgesBySector)
            {
                List<SectorLoop> loops = TraceLoops(map, sector, edges, warnings);
                if (loops.Count > 0) result[sector] = loops;
            }
            return result;
        }

        private static int SectorOf(DoomMap map, int sidedefIndex) =>
            sidedefIndex >= 0 && sidedefIndex < map.Sidedefs.Count ? map.Sidedefs[sidedefIndex].SectorIndex : -1;

        private static void Add(Dictionary<int, List<(int, int)>> dict, int sector, (int, int) edge)
        {
            if (!dict.TryGetValue(sector, out List<(int, int)>? list)) dict[sector] = list = new();
            list.Add(edge);
        }

        private static List<SectorLoop> TraceLoops(DoomMap map, int sector, List<(int From, int To)> edges, List<string> warnings)
        {
            var byStart = new Dictionary<int, List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                if (!byStart.TryGetValue(edges[i].From, out List<int>? list)) byStart[edges[i].From] = list = new();
                list.Add(i);
            }

            var used = new bool[edges.Count];
            var loops = new List<SectorLoop>();

            for (int start = 0; start < edges.Count; start++)
            {
                if (used[start]) continue;

                var chain = new List<int> { edges[start].From };
                used[start] = true;
                int first = edges[start].From;
                int current = start;
                bool closed = false;

                // A loop can never be longer than the sector's edge count — a hard cap against
                // pathological map data sending the walk in circles.
                for (int guard = 0; guard <= edges.Count; guard++)
                {
                    int at = edges[current].To;
                    if (at == first) { closed = true; break; }
                    chain.Add(at);

                    int next = PickNext(map, edges, byStart, used, current, at);
                    if (next < 0) break;
                    used[next] = true;
                    current = next;
                }

                if (closed && chain.Count >= 3)
                    loops.Add(new SectorLoop(chain, SignedArea(map, chain) > 0));
                else
                    warnings.Add($"Sector {sector}: open boundary chain ({chain.Count} edges) skipped — map data is unclosed here.");
            }

            return loops;
        }

        /// <summary>Picks the unused edge leaving <paramref name="vertex"/> with the sharpest right
        /// turn relative to the incoming edge (interior is on the right), taking the straight-back
        /// reversal only as a last resort.</summary>
        private static int PickNext(DoomMap map, List<(int From, int To)> edges,
            Dictionary<int, List<int>> byStart, bool[] used, int incoming, int vertex)
        {
            if (!byStart.TryGetValue(vertex, out List<int>? candidates)) return -1;

            DoomVertex a = map.Vertices[edges[incoming].From];
            DoomVertex b = map.Vertices[edges[incoming].To];
            (double X, double Y) dIn = (b.X - a.X, b.Y - a.Y);

            int best = -1, bestReverse = -1;
            double bestAngle = double.MaxValue;
            foreach (int c in candidates)
            {
                if (used[c]) continue;
                DoomVertex to = map.Vertices[edges[c].To];
                if (edges[c].To == edges[incoming].From)
                {
                    bestReverse = c;
                    continue;
                }
                (double X, double Y) dOut = (to.X - b.X, to.Y - b.Y);
                double angle = Math.Atan2(dIn.X * dOut.Y - dIn.Y * dOut.X, dIn.X * dOut.X + dIn.Y * dOut.Y);
                if (angle < bestAngle) { bestAngle = angle; best = c; }
            }
            return best >= 0 ? best : bestReverse;
        }

        /// <summary>Shoelace area; positive = counter-clockwise. Outer sector boundaries wind
        /// clockwise here (interior kept to the right), so a positive area marks a hole.</summary>
        private static double SignedArea(DoomMap map, List<int> chain)
        {
            double sum = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                DoomVertex p = map.Vertices[chain[i]];
                DoomVertex q = map.Vertices[chain[(i + 1) % chain.Count]];
                sum += p.X * q.Y - q.X * p.Y;
            }
            return sum / 2;
        }
    }
}
