using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Get
{
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

        private sealed record Request
        {
            public string CategoryName { get; set; } = string.Empty;
        }
    }
}
