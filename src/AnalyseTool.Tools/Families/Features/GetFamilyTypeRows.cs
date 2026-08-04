using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Read-only rows at the family TYPE level for the given families: family/type/category, placed
    /// instance count, the distinct worksets the instances occupy, and all type parameters. Backs the
    /// Family Types tab (grouped by type, splittable by a chosen parameter, parameters as columns).
    /// </summary>
    [RevitCommand(
        Description = "Lists family types (FamilySymbols) for the given families with category, instance " +
                      "count, the worksets their instances occupy and all type parameters. Read-only. " +
                      "'familyIds' is REQUIRED for loadable families: empty means NO loadable family is " +
                      "listed, not all of them — the result is then system types only, every row with " +
                      "isSystem: true. Get the ids from GetFamilies first (in a pipeline: " +
                      "\"bind\": { \"familyIds\": \"<node>.families[*].id\" }).",
        ReadOnly = true,
        InputType = typeof(GetFamilyTypeRows.Request),
        OutputType = typeof(TypeRowsResult))]
    internal sealed class GetFamilyTypeRows : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
                new FamiliesService().GetFamilyTypeRows(
                    app.ActiveUIDocument.Document, req.FamilyIds ?? new List<long>()));
        }

        public sealed class Request
        {
            [Description("Family ids whose types to list. Empty lists NO loadable family — not all of " +
                         "them. Take the ids from GetFamilies.")]
            public List<long>? FamilyIds { get; set; }
        }
    }
}
