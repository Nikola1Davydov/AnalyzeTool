using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using System.ComponentModel;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Deletes the given family types resiliently for "purge unused". Unlike the batch delete, a type that
    /// cannot be removed (e.g. the last type of a family, or one still referenced) is skipped and counted
    /// in <c>failed</c> rather than aborting the whole purge — so the deletable ones still go. Returns the
    /// number actually deleted and the number that couldn't be.
    /// </summary>
    [RevitCommand(
        Description = "Deletes the given family types, skipping (and counting) any that can't be removed. " +
                      "Used by 'purge unused types'. Returns { deleted, failed }.",
        Destructive = true,
        InputType = typeof(PurgeFamilyTypes.Request),
        OutputType = typeof(PurgeResult))]
    internal sealed class PurgeFamilyTypes : IRevitTask, IProgressAware
    {
        // Types deleted per Revit round-trip. Smaller = smoother progress but more undo entries; larger =
        // fewer undo entries but coarser progress. 40 is a balance for typical unused-type counts.
        private const int ChunkSize = 40;

        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();
            FamilyActionsService service = new();

            // Decide once (whole-family "keep one" reasoning), then delete in chunks. Each chunk is a
            // separate Revit round-trip, so control returns to the UI thread between chunks — that's what
            // lets the progress bar actually animate (a single long transaction would block the UI).
            List<long> plan = await ctx.RunInRevitAsync(app =>
                service.PlanPurgeTypes(app.ActiveUIDocument.Document, req.TypeIds));

            List<TransactionWarning> warnings = new();
            if (plan.Count == 0) return new PurgeResult(true, 0, 0, warnings);

            int deleted = 0, failed = 0, done = 0;
            for (int i = 0; i < plan.Count; i += ChunkSize)
            {
                ct.ThrowIfCancellationRequested();
                List<long> chunk = plan.GetRange(i, Math.Min(ChunkSize, plan.Count - i));

                ChunkResult res = await ctx.RunInRevitAsync(app =>
                    service.PurgeChunk(app.ActiveUIDocument.Document, chunk));

                deleted += res.Deleted;
                failed += res.Failed;
                if (res.Warnings is { Count: > 0 }) warnings.AddRange(res.Warnings);
                done += chunk.Count;
                Progress?.Report(new ProgressInfo(done / (double)plan.Count, "Deleting unused types…"));
            }

            return new PurgeResult(true, deleted, failed, warnings);
        }

        public sealed class Request
        {
            [Description("Family type (FamilySymbol / ElementType) ids to delete, skipping any that can't be.")]
            public List<long>? TypeIds { get; set; }
        }
    }
}
