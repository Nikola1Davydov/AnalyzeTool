namespace AnalyseTool.Tools.Bot
{
    /// <summary>A point in project coordinates, in METRES (the bot slice never speaks feet outwards).</summary>
    public sealed record BotPoint(double X, double Y, double Z);

    /// <summary>
    /// What a ray hit: the element, how far away it is, and — when the hit came from a linked model —
    /// which link it lives in (the id is then meaningful only inside that link).
    /// </summary>
    public sealed record BotHit(
        long Id,
        string Name,
        string? Category,
        double DistanceM,
        string? LinkName = null);

    /// <summary>One ray of a look-around: the compass bearing it was cast at and what it found (null = nothing).</summary>
    public sealed record BotSightLine(string Label, double BearingDeg, BotHit? Hit);

    /// <summary>Headroom result for a single room.</summary>
    /// <param name="Note">Why a room produced no samples (no geometry, not enclosed, …). Null when scanned.</param>
    public sealed record RoomHeadroom(
        long RoomId,
        string Name,
        string? Number,
        string? Level,
        int SampledPoints,
        int LowPoints,
        double GridStepM,
        double? MinClearanceM,
        BotPoint? WorstPoint,
        BotHit? Obstruction,
        string? Note = null);

    /// <summary>An element that turned out to be the worst obstruction in more than one room — the
    /// duct run worth fixing first.</summary>
    public sealed record ObstructionCount(
        long Id,
        string Name,
        string? Category,
        string? LinkName,
        int Rooms,
        double MinClearanceM);
}
