using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Bot
{
    /// <summary>
    /// Walks a grid of standing points through every room and looks up, reporting where the clear height
    /// falls below the limit and what is in the way.
    ///
    /// <para>Runs in batches of rooms: each batch is one Revit round-trip, so the UI keeps breathing, the
    /// progress bar actually moves and cancellation lands between batches instead of after the whole
    /// model.</para>
    /// </summary>
    [RevitCommand(
        Description = "Headroom sweep: samples a grid of floor points in every room, casts a ray straight " +
                      "up and reports rooms where the clear height is below minClearanceM (default 2.1 m), " +
                      "naming the obstructing element. Finds low ducts, beams, trays and pipes that clash " +
                      "with nothing and therefore never show up in clash detection. Reads only — it builds " +
                      "a throwaway 3D view for the raycasts and rolls it back, leaving no undo entry.",
        ReadOnly = true,
        InputType = typeof(BotScanHeadroom.Request))]
    internal sealed class BotScanHeadroom : IRevitTask, IProgressAware
    {
        // Rooms per Revit round-trip. Small enough that the UI stays responsive on a heavy model, large
        // enough that the per-batch scan view is not created for every single room.
        private const int RoomsPerBatch = 8;

        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
        {
            Request request = revitContext.Payload.As<Request>() ?? new Request();
            HeadroomOptions options = request.ToOptions();
            HeadroomService service = new();

            List<long> rooms = await revitContext.RunInRevitAsync(app =>
                service.PlanRooms(app.ActiveUIDocument.Document, options));

            if (rooms.Count == 0)
            {
                return new
                {
                    ok = true,
                    roomsScanned = 0,
                    message = "No placed, enclosed room matched the filter — nothing to walk.",
                };
            }

            List<RoomHeadroom> scanned = new(rooms.Count);
            for (int i = 0; i < rooms.Count; i += RoomsPerBatch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<long> batch = rooms.GetRange(i, Math.Min(RoomsPerBatch, rooms.Count - i));

                scanned.AddRange(await revitContext.RunInRevitAsync(app =>
                    service.ScanRooms(app.ActiveUIDocument.Document, batch, options, cancellationToken)));

                Progress?.Report(new ProgressInfo(
                    Math.Min(1d, (i + batch.Count) / (double)rooms.Count),
                    $"Checking headroom… {Math.Min(i + batch.Count, rooms.Count)}/{rooms.Count} rooms"));
            }

            List<RoomHeadroom> failing = scanned
                .Where(r => r.LowPoints > 0)
                .OrderBy(r => r.MinClearanceM ?? double.MaxValue)
                .ToList();

            int cap = Math.Max(1, request.MaxRooms ?? 50);
            List<RoomHeadroom> reported = failing.Take(cap).ToList();

            return new
            {
                ok = true,
                minClearanceM = options.MinClearanceM,
                gridStepM = options.GridStepM,
                includeLinks = options.IncludeLinks,
                roomsScanned = scanned.Count,
                roomsWithIssues = failing.Count,
                roomsNotSampled = scanned.Count(r => r.Note != null),
                sampledPoints = scanned.Sum(r => r.SampledPoints),
                lowPoints = scanned.Sum(r => r.LowPoints),
                worstClearanceM = failing.Count == 0 ? (double?)null : failing[0].MinClearanceM,
                rooms = reported,
                topObstructions = TopObstructions(failing),
                // Never let a cap read as "that was everything".
                truncated = failing.Count > reported.Count,
                note = failing.Count > reported.Count
                    ? $"{failing.Count} rooms are below the limit; the {reported.Count} worst are listed. " +
                      "Raise maxRooms or filter by level to see the rest."
                    : null,
            };
        }

        /// <summary>The same element blocking several rooms is one fix, not many — surface those first.</summary>
        private static List<ObstructionCount> TopObstructions(IEnumerable<RoomHeadroom> failing) =>
            failing
                .Where(r => r.Obstruction != null)
                .GroupBy(r => (r.Obstruction!.Id, r.Obstruction.LinkName))
                .Select(g => new ObstructionCount(
                    g.Key.Id,
                    g.First().Obstruction!.Name,
                    g.First().Obstruction!.Category,
                    g.Key.LinkName,
                    g.Count(),
                    g.Min(r => r.MinClearanceM ?? 0)))
                .OrderByDescending(o => o.Rooms)
                .ThenBy(o => o.MinClearanceM)
                .Take(10)
                .ToList();

        internal sealed class Request
        {
            [Description("Clear height in metres below which a point is a defect. Default 2.1.")]
            public double? MinClearanceM { get; set; }

            [Description("Spacing of the sample grid inside a room, in metres. Default 1.0. " +
                         "Smaller finds smaller obstructions and costs proportionally more time.")]
            public double? GridStepM { get; set; }

            [Description("Ray budget per room; a bigger room is sampled on a coarser grid instead of being " +
                         "cut off. Default 400.")]
            public int? MaxPointsPerRoom { get; set; }

            [Description("Count obstructions inside linked models (where the ducts usually live). Default true.")]
            public bool? IncludeLinks { get; set; }

            [Description("Only these levels, by exact name. Empty = all levels.")]
            public List<string>? LevelNames { get; set; }

            [Description("Only these rooms, by element id. Empty = all placed rooms.")]
            public List<long>? RoomIds { get; set; }

            [Description("How many failing rooms to list in the result (worst first). Default 50.")]
            public int? MaxRooms { get; set; }

            public HeadroomOptions ToOptions() => new()
            {
                MinClearanceM = MinClearanceM ?? 2.1,
                GridStepM = GridStepM ?? 1.0,
                MaxPointsPerRoom = MaxPointsPerRoom ?? 400,
                IncludeLinks = IncludeLinks ?? true,
                LevelNames = LevelNames,
                RoomIds = RoomIds,
            };
        }
    }
}
