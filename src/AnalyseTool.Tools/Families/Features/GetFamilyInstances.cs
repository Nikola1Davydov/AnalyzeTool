using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;
using System.ComponentModel;

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
        InputType = typeof(GetFamilyInstances.Request),
        OutputType = typeof(FamilyInstancesResult))]
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
            [Description("Owning family id — the single-family case. Ids come from GetFamilies.")]
            public long? FamilyId { get; set; }

            /// <summary>Owning family ids (multi-family case, e.g. the Family Types tab over filtered families).</summary>
            [Description("Owning family ids — the multi-family case. Use instead of familyId, not with it.")]
            public List<long>? FamilyIds { get; set; }

            /// <summary>Single type (FamilySymbol) id to narrow the instances to one type.</summary>
            [Description("Optional FamilySymbol id, narrowing the answer to instances of that one type.")]
            public long? TypeId { get; set; }

            /// <summary>Type (FamilySymbol) ids to narrow the instances (e.g. a grouped row of types).</summary>
            [Description("Optional FamilySymbol ids, narrowing the answer to instances of those types.")]
            public List<long>? TypeIds { get; set; }

            /// <summary>Optional cap on the number of returned instances (the total count is still reported).</summary>
            [Description("Optional cap on how many instances come back. The total count is reported either " +
                         "way, so a truncated answer still says how much was left out.")]
            public int? Limit { get; set; }
        }
    }
}
