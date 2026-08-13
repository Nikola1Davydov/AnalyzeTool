using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns elements of a Revit category. Defaults to placed INSTANCES — pass " +
                      "elementKind \"types\" or \"all\" for the type palette. Returns " +
                      "{ category, elementKind, count, returned, elements, error, didYouMean } — 'count' is " +
                      "the matches before 'limit', so a truncated answer says so, and an unknown category " +
                      "is an 'error' with suggestions rather than an empty list. Each element carries " +
                      "familyId/familyName, which is the join back to GetFamilies. Identify the category " +
                      "by the LOCALISED name (e.g. \"Wände\") or, better, by the language-independent " +
                      $"builtInCategory (e.g. \"OST_Walls\"); {nameof(GetModelOverview)} lists the categories " +
                      $"that have elements and {nameof(GetCategoryParameters)} the parameter names. " +
                      "Token-friendly: no parameter values unless parameterNames asks for them. " +
                      "Read-only. Cost: scans the requested category and resolves each element's type — " +
                      "pass limit on a large model.",
        ReadOnly = true,
        InputType = typeof(GetElements.Request),
        OutputType = typeof(ElementsResult))]
    internal sealed class GetElements : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request data = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
                new DataElementsCollectorService().GetElementSummaries(
                    app.ActiveUIDocument.Document,
                    new ElementQuery
                    {
                        Category = data.Category,
                        BuiltInCategory = data.BuiltInCategory,
                        ElementKind = data.ElementKind,
                        NameContains = data.NameContains,
                        FamilyNameContains = data.FamilyNameContains,
                        TypeNameContains = data.TypeNameContains,
                        ParameterNames = data.ParameterNames,
                        Limit = data.Limit,
                    }));
        }

        internal sealed record Request
        {
            [Description("Revit category name, LOCALISED — e.g. \"Wände\", not \"Walls\". Get the exact " +
                         "spelling from GetModelOverview (categoryCounts) or GetCategoriesInRevit. " +
                         "Alternatively pass builtInCategory and skip the language question.")]
            public string? Category { get; set; }

            [Description("Language-independent category, e.g. \"OST_Walls\" or \"OST_Planting\". Preferred " +
                         "over 'category' when you know it: it means the same thing on every install. " +
                         "Wins if both are given.")]
            public string? BuiltInCategory { get; set; }

            [Description("\"instances\" (default) — placed elements; \"types\" — the type palette; " +
                         "\"all\" — both. Defaults to instances because system types sort first by id and " +
                         "would otherwise fill a limited answer before a single placed element appeared.")]
            public string? ElementKind { get; set; }

            [Description("Optional: return only these parameters' values per element (use GetCategoryParameters to " +
                         "discover names). Omit to return elements without parameter values.")]
            public List<string>? ParameterNames { get; set; }

            [Description("Optional: keep elements whose OWN name, family name or type name contains this " +
                         "text (case-insensitive). Use familyNameContains or typeNameContains to be precise.")]
            public string? NameContains { get; set; }

            [Description("Optional: keep elements whose FAMILY name contains this text (case-insensitive) — " +
                         "including system families such as \"Basiswand\".")]
            public string? FamilyNameContains { get; set; }

            [Description("Optional: keep elements whose TYPE name contains this text (case-insensitive).")]
            public string? TypeNameContains { get; set; }

            [Description("Optional: cap the number of elements returned. 'count' still reports how many " +
                         "matched, so a capped answer is never mistaken for a complete one.")]
            public int? Limit { get; set; }
        }
    }
}
