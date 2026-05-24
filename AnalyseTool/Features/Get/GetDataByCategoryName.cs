using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Get
{
    [RevitCommand("GetDataByCategoryName",
        Description = "Returns all elements of the given Revit category in the active document, " +
                      "each with its parameters. Call GetCategoriesInRevit first to get valid category names.",
        ReadOnly = true)]
    internal sealed class GetDataByCategoryName : RevitTask<GetDataByCategoryName.Request>
    {
        public override Task<object?> ExecuteAsync(Request data, IRevitContext ctx, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(data?.CategoryName))
                return Task.FromResult<object?>(new List<DataElement>());

            return ctx.RunInRevitAsync<object?>(app =>
            {
                DataElementsCollectorService collector = new DataElementsCollectorService();
                return collector.GetAllElementsByCategory(app.ActiveUIDocument.Document, data.CategoryName)?.ToList()
                       ?? new List<DataElement>();
            });
        }

        public sealed record Request
        {
            /// <summary>Revit category name, e.g. "Walls", "Doors" (as returned by GetCategoriesInRevit).</summary>
            public string CategoryName { get; set; } = string.Empty;
        }
    }
}
