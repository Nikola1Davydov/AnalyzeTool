using AnalyseTool.Tools.Infrastructure;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Features.Families
{
    /// <summary>
    /// Read-only rows at the family TYPE level for the given families: family/type/category, placed
    /// instance count, the distinct worksets the instances occupy, and all type parameters. Backs the
    /// Family Types tab (grouped by type, splittable by a chosen parameter, parameters as columns).
    /// </summary>
    [RevitCommand(
        Description = "Lists family types (FamilySymbols) for the given families with category, instance " +
                      "count, the worksets their instances occupy and all type parameters. Read-only.",
        ReadOnly = true,
        InputType = typeof(GetFamilyTypeRows.Request))]
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
            [Description("Family ids whose types to list.")]
            public List<long>? FamilyIds { get; set; }
        }
    }
}
