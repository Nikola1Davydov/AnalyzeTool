using AnalyseTool.Tools.Infrastructure;
using AnalyseTool.Sdk;
using AnalyseTool.Tools.Infrastructure.Model;
using System.ComponentModel;

namespace AnalyseTool.Tools.Features.Get
{
    [RevitCommand(
        Description = "Returns the parameter names available on elements of a Revit category " +
                      "(name, storageType, isReadOnly, isType), sampled from a representative element. " +
                      $"Use this to discover which parameterNames to request from {nameof(GetElements)}. " +
                      $"Call {nameof(GetCategoriesInRevit)} first for valid category names.",
        ReadOnly = true,
        InputType = typeof(GetCategoryParameters.Request))]
    internal sealed class GetCategoryParameters : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            if (string.IsNullOrEmpty(data?.Category))
                return Task.FromResult<object?>(new List<CategoryParameterInfo>());

            return ctx.RunInRevitAsync<object?>(app =>
                new DataElementsCollectorService()
                    .GetCategoryParameterInfos(app.ActiveUIDocument.Document, data.Category)
                    .ToList());
        }

        internal sealed record Request
        {
            [Description("Revit category name as returned by GetCategoriesInRevit. Language-specific: e.g. \"Wände\", not \"Walls\".")]
            public string Category { get; set; } = string.Empty;
        }
    }
}
