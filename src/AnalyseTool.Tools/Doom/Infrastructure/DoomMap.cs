namespace AnalyseTool.Tools.Doom
{
    /// <summary>A map vertex in Doom map units (16-bit integer grid, y grows "north").</summary>
    internal readonly record struct DoomVertex(double X, double Y);

    /// <summary>
    /// A linedef: a wall segment / sector boundary from V1 to V2. Sidedef indices are -1 when the
    /// side is absent; a linedef with only a right side is a solid one-sided wall. The sector a side
    /// faces lies to the RIGHT of the V1→V2 direction for the right side (and to the right of V2→V1
    /// for the left side) — the invariant the sector-loop tracing relies on.
    /// </summary>
    internal readonly record struct DoomLinedef(int V1, int V2, int RightSide, int LeftSide);

    /// <summary>A sidedef reduced to what geometry needs: which sector this side faces.</summary>
    internal readonly record struct DoomSidedef(int SectorIndex);

    /// <summary>A sector's vertical extent in Doom map units (heights are absolute, world z).</summary>
    internal readonly record struct DoomSector(int FloorHeight, int CeilingHeight);

    /// <summary>One parsed map (e.g. E1M1) — only the four lumps geometry needs.</summary>
    internal sealed record DoomMap(
        string Name,
        IReadOnlyList<DoomVertex> Vertices,
        IReadOnlyList<DoomLinedef> Linedefs,
        IReadOnlyList<DoomSidedef> Sidedefs,
        IReadOnlyList<DoomSector> Sectors);
}
