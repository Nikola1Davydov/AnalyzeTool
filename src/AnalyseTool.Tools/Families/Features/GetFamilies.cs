using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Families
{
    /// <summary>
    /// Read-only inventory of the families in the active document (category, type count, placed-instance
    /// count, in-place flag). Backbone for the Family Control window's table. Filter with
    /// categoryContains / nameContains; cap the rows with limit.
    /// </summary>
    [RevitCommand(
        Description = "Lists the families in the active document with category, type count and " +
                      "placed-instance count, plus an in-place flag. Read-only. Filter with " +
                      "categoryContains / nameContains; cap the result with limit.",
        ReadOnly = true,
        InputType = typeof(GetFamilies.Request))]
    internal sealed class GetFamilies : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
                new FamiliesService().GetFamilies(
                    app.ActiveUIDocument.Document, req.CategoryContains, req.NameContains, req.Limit));
        }

        public sealed class Request
        {
            /// <summary>Optional case-insensitive substring filter on the family's category.</summary>
            public string? CategoryContains { get; set; }

            /// <summary>Optional case-insensitive substring filter on the family name.</summary>
            public string? NameContains { get; set; }

            /// <summary>Optional cap on the number of returned families (the total count is still reported).</summary>
            public int? Limit { get; set; }
        }
    }
}
