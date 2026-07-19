using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Features.Families
{
    /// <summary>
    /// Read-only: non-empty type parameters for a batch of family types in one call. Backs the manager's
    /// naming-rule engine (compose type names from parameter values, e.g. "Möb_Alu_1000x2000") — one
    /// round-trip for the whole selection instead of one per type.
    /// </summary>
    [RevitCommand(
        Description = "Returns the non-empty type parameters (display values) for a batch of family " +
                      "types. Payload: { typeIds: [long] }. Returns { types: [{ typeId, parameters: " +
                      "[{ name, value }] }] }. Read-only.",
        ReadOnly = true,
        InputType = typeof(GetTypeParameters.Request))]
    internal sealed class GetTypeParameters : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            List<long> ids = req?.TypeIds ?? [];

            return ctx.RunInRevitAsync<object?>(app =>
                new FamiliesService().GetTypeParameters(app.ActiveUIDocument.Document, ids));
        }

        internal sealed class Request
        {
            [Description("Revit ElementIds (long) of the types, as returned by GetFamilyTypeRows.")]
            public List<long>? TypeIds { get; set; }
        }
    }
}
