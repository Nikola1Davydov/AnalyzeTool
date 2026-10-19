using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.SharedParameters
{
    [RevitCommand(
        Description = "Evaluates how well parameters are filled in the open document. For each requested parameter, " +
                      "over the elements of the categories it is bound to: { name, guid, isInstance, total, filled, " +
                      "categories: [{ name, total, filled }], levels: [{ name, total, filled }], values: [{ value, count }], " +
                      "otherValuesCount, distinctValues, error }. A type parameter is counted per instance with its " +
                      "type's value. Identify a shared parameter by guid, a project parameter by name. Read-only. Cost: " +
                      "one pass over the bound categories per parameter.",
        ReadOnly = true,
        InputType = typeof(GetParameterReport.Request),
        OutputType = typeof(ParameterReportResult))]
    internal sealed class GetParameterReport : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? request = ctx.Payload.As<Request>();
            List<ParameterReportService.Request> items = (request?.Parameters ?? new())
                .Select(p => new ParameterReportService.Request(
                    Guid.TryParse(p.Guid, out Guid g) ? g : null, p.Name))
                .ToList();

            return ctx.RunInRevitAsync<object?>(app =>
                new ParameterReportService().Build(app.ActiveUIDocument.Document, items, request?.TopValues ?? 10));
        }

        internal sealed record Request
        {
            [Description("Parameters to evaluate.")]
            public List<ParameterRef> Parameters { get; set; } = new();

            [Description("How many of the most frequent values to list per parameter (1–100, default 10).")]
            public int TopValues { get; set; } = 10;
        }

        internal sealed record ParameterRef
        {
            [Description("GUID of a shared parameter.")]
            public string? Guid { get; set; }

            [Description("Name — used for a project parameter, which has no GUID.")]
            public string? Name { get; set; }
        }
    }
}
