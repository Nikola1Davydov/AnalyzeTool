using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using System.ComponentModel;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Deletes the given families for "purge unused families", chunked so it can report live progress
    /// (see <see cref="IProgressAware"/>). Each family is deleted in its own transaction, so one that
    /// can't be removed is skipped and counted rather than aborting the purge. Returns { deleted, failed }.
    /// </summary>
    [RevitCommand(
        Description = "Deletes the given families, skipping (and counting) any that can't be removed. " +
                      "Used by 'purge unused families'. Returns { deleted, failed }.",
        InputType = typeof(PurgeFamilies.Request))]
    internal sealed class PurgeFamilies : IRevitTask, IProgressAware
    {
        private const int ChunkSize = 40;

        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();
            FamilyActionsService service = new();

            // Validate once, then delete in chunks — control returns to the UI thread between chunks so the
            // progress bar animates (a single long transaction would block the UI until it finished).
            List<long> plan = await ctx.RunInRevitAsync(app =>
                service.PlanPurgeFamilies(app.ActiveUIDocument.Document, req.FamilyIds));

            if (plan.Count == 0) return new { ok = true, deleted = 0, failed = 0 };

            int deleted = 0, failed = 0, done = 0;
            for (int i = 0; i < plan.Count; i += ChunkSize)
            {
                ct.ThrowIfCancellationRequested();
                List<long> chunk = plan.GetRange(i, Math.Min(ChunkSize, plan.Count - i));

                ChunkResult res = await ctx.RunInRevitAsync(app =>
                    service.PurgeChunk(app.ActiveUIDocument.Document, chunk));

                deleted += res.Deleted;
                failed += res.Failed;
                done += chunk.Count;
                Progress?.Report(new ProgressInfo(done / (double)plan.Count, "Deleting unused families…"));
            }

            return new { ok = true, deleted, failed };
        }

        public sealed class Request
        {
            [Description("Family ids to delete (typically the unused ones).")]
            public List<long>? FamilyIds { get; set; }
        }
    }
}
