using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Read-only list of the placed instances of a family (optionally narrowed to one type), each with
    /// its type, category, level and workset. Backs Select/Isolate (the returned ids feed
    /// SelectionInRevit / IsolationInRevit), the Worksets view and advanced instance filtering.
    /// </summary>
    [RevitCommand(
        Description = "Lists the placed instances of one or more families (optionally one type) with " +
                      "type, category, level and workset. Read-only. Pass familyId or familyIds, " +
                      "optional typeId and limit.",
        ReadOnly = true,
        InputType = typeof(GetFamilyInstances.Request))]
    internal sealed class GetFamilyInstances : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            List<long> familyIds = new();
            if (req.FamilyId is long single) familyIds.Add(single);
            if (req.FamilyIds is { Count: > 0 }) familyIds.AddRange(req.FamilyIds);

            List<long> typeIds = new();
            if (req.TypeId is long t) typeIds.Add(t);
            if (req.TypeIds is { Count: > 0 }) typeIds.AddRange(req.TypeIds);

            return ctx.RunInRevitAsync<object?>(app =>
                new FamiliesService().GetFamilyInstances(
                    app.ActiveUIDocument.Document, familyIds, typeIds, req.Limit));
        }

        public sealed class Request
        {
            /// <summary>Owning family id (single-family case, e.g. Select/Isolate from a card).</summary>
            public long? FamilyId { get; set; }

            /// <summary>Owning family ids (multi-family case, e.g. the Family Types tab over filtered families).</summary>
            public List<long>? FamilyIds { get; set; }

            /// <summary>Single type (FamilySymbol) id to narrow the instances to one type.</summary>
            public long? TypeId { get; set; }

            /// <summary>Type (FamilySymbol) ids to narrow the instances (e.g. a grouped row of types).</summary>
            public List<long>? TypeIds { get; set; }

            /// <summary>Optional cap on the number of returned instances (the total count is still reported).</summary>
            public int? Limit { get; set; }
        }
    }
}
