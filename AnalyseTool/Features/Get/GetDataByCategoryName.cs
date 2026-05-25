using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Features.Get
{
    [RevitCommand(
        Description = "Returns all elements of the given Revit category in the active document, " +
                      $"each with its parameters. Call {nameof(GetCategoriesInRevit)} first to get valid category names.",
        ReadOnly = true,
        InputType = typeof(GetDataByCategoryName.Request))]
    internal sealed class GetDataByCategoryName : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            if (string.IsNullOrEmpty(data?.CategoryName))
                return Task.FromResult<object?>(new List<DataElement>());

            return ctx.RunInRevitAsync<object?>(app =>
            {
                DataElementsCollectorService collector = new DataElementsCollectorService();
                return collector.GetAllElementsByCategory(app.ActiveUIDocument.Document, data.CategoryName)?.ToList()
                       ?? new List<DataElement>();
            });
        }

        internal sealed record Request
        {
            [Description("Revit category name as returned by GetCategoriesInRevit. Language-specific: e.g. \"Wände\", not \"Walls\".")]
            public string CategoryName { get; set; } = string.Empty;
        }
    }
}
