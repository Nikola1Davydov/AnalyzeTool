using AnalyseTool.Infrastructure;
using AnalyseTool.Infrastructure.Model;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Features.Get
{
    [RevitCommand(
        Description = "Returns elements of a Revit category as a lean list (id, name, category, level, isType). " +
                      "Token-friendly: no parameter values unless requested. Pass parameterNames to include specific " +
                      "parameters' values, nameContains to filter by name, and limit to cap the count. " +
                      $"Call {nameof(GetCategoriesInRevit)} for valid category names and " +
                      $"{nameof(GetCategoryParameters)} to discover parameter names.",
        ReadOnly = true,
        InputType = typeof(GetElements.Request))]
    internal sealed class GetElements : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            if (string.IsNullOrEmpty(data?.Category))
                return Task.FromResult<object?>(new List<ElementSummary>());

            return ctx.RunInRevitAsync<object?>(app =>
                new DataElementsCollectorService()
                    .GetElementSummaries(app.ActiveUIDocument.Document, data.Category,
                                         data.NameContains, data.ParameterNames, data.Limit)
                    .ToList());
        }

        internal sealed record Request
        {
            [Description("Revit category name as returned by GetCategoriesInRevit. Language-specific: e.g. \"Wände\", not \"Walls\".")]
            public string Category { get; set; } = string.Empty;

            [Description("Optional: return only these parameters' values per element (use GetCategoryParameters to " +
                         "discover names). Omit to return elements without parameter values.")]
            public List<string>? ParameterNames { get; set; }

            [Description("Optional: only include elements whose name contains this text (case-insensitive).")]
            public string? NameContains { get; set; }

            [Description("Optional: cap the number of elements returned. Recommended for large categories.")]
            public int? Limit { get; set; }
        }
    }
}
