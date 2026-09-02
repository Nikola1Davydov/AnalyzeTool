using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Elements
{
    /// <summary>
    /// Read-only: non-empty type parameters for a batch of family types in one call. Backs the manager's
    /// naming-rule engine (compose type names from parameter values, e.g. "Möb_Alu_1000x2000") — one
    /// round-trip for the whole selection instead of one per type.
    /// </summary>
    [RevitCommand(
        Description = "Returns ALL type parameters (display values, empty ones included) for a batch of " +
                      "element types. Read-only. Payload: { typeIds: [long] } — type ids from GetElements " +
                      "with elementKind \"types\". Returns { types: [{ typeId, parameters: [{ name, value, id, " +
                      "builtInParameter, spec, unit }] }] }; 'id' tells two parameters of one name apart and is what " +
                      "SetDataToParameters takes; 'spec' says what the value means (length, area, number, string...) " +
                      "and 'unit' the document unit a numeric value is in (millimeters, feet...). " +
                      "Cost: reads only the given types — cheap.",
        ReadOnly = true,
        InputType = typeof(GetTypeParameters.Request),
        OutputType = typeof(TypeParametersResult))]
    internal sealed class GetTypeParameters : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            List<long> ids = req?.TypeIds ?? [];

            return ctx.RunInRevitAsync<object?>(app =>
                new TypeAndWorksetService().GetTypeParameters(app.ActiveUIDocument.Document, ids));
        }

        internal sealed class Request
        {
            [Description("Revit ElementIds (long) of the types, as returned by GetFamilyTypeRows.")]
            public List<long>? TypeIds { get; set; }
        }
    }
}
