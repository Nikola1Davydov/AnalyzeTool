using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     A lean, filterable listing of the elements of a category.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Returns elements of a Revit category. Defaults to placed INSTANCES — pass elementKind \"types\" or \"all\" for the type " +
    "palette. Returns { category, elementKind, count, returned, elements, error, didYouMean } — 'count' is the matches before " +
    "'limit', so a truncated answer says so, and an unknown category is an 'error' with suggestions rather than an empty list. " +
    "Each element carries id, name, category, builtInCategory, level, levelId (the join to GetModelOverview.levels), isType, " +
    "familyId/familyName and typeName. Identify the category by the LOCALISED name (e.g. \"Wände\") or, better, by the " +
    "language-independent builtInCategory (e.g. \"OST_Walls\"); GetModelOverview lists the categories that have elements and " +
    "GetCategoryParameters the parameter names. Token-friendly: no parameter values unless parameterNames asks for them. " +
    "Read-only. Cost: scans the requested category and resolves each element's type — pass limit on a large model.")]
public sealed class GetElements(ActiveDocument document, DataElementsCollectorService service) : ICommand<GetElementsRequest, ElementsResult>
{
    /// <inheritdoc />
    public Task<ElementsResult> ExecuteAsync(GetElementsRequest request, CommandContext context)
    {
        var query = new ElementQuery
        {
            Category = request.Category,
            BuiltInCategory = request.BuiltInCategory,
            ElementKind = request.ElementKind,
            NameContains = request.NameContains,
            FamilyNameContains = request.FamilyNameContains,
            TypeNameContains = request.TypeNameContains,
            ParameterNames = request.ParameterNames,
            Limit = request.Limit
        };

        return document.InvokeAsync(revitDocument => service.GetElementSummaries(revitDocument, query), context.CancellationToken);
    }
}

/// <summary>The input of <see cref="GetElements" />.</summary>
/// <param name="Category">The localised category name.</param>
/// <param name="BuiltInCategory">The <c>BuiltInCategory</c> name; wins over <paramref name="Category" />.</param>
/// <param name="ElementKind"><c>instances</c> (default), <c>types</c> or <c>all</c>.</param>
/// <param name="ParameterNames">The parameters whose values to return per element.</param>
/// <param name="NameContains">Keep elements whose own, family or type name contains this text.</param>
/// <param name="FamilyNameContains">Keep elements whose family name contains this text.</param>
/// <param name="TypeNameContains">Keep elements whose type name contains this text.</param>
/// <param name="Limit">The maximum number of elements to return.</param>
public sealed record GetElementsRequest(
    [property: Description("Revit category name, LOCALISED — e.g. \"Wände\", not \"Walls\". Get the exact spelling from " +
                           "GetModelOverview (categoryCounts) or GetCategoriesInRevit. Alternatively pass builtInCategory and skip " +
                           "the language question.")]
    string? Category = null,
    [property: Description("Language-independent category, e.g. \"OST_Walls\" or \"OST_Planting\". Preferred over 'category' when " +
                           "you know it: it means the same thing on every install. Wins if both are given.")]
    string? BuiltInCategory = null,
    [property: Description("\"instances\" (default) — placed elements; \"types\" — the type palette; \"all\" — both. Defaults to " +
                           "instances because system types sort first by id and would otherwise fill a limited answer before a " +
                           "single placed element appeared.")]
    string? ElementKind = null,
    [property: Description("Optional: return only these parameters' values per element (use GetCategoryParameters to discover " +
                           "names). Omit to return elements without parameter values.")]
    IReadOnlyList<string>? ParameterNames = null,
    [property: Description("Optional: keep elements whose OWN name, family name or type name contains this text " +
                           "(case-insensitive). Use familyNameContains or typeNameContains to be precise.")]
    string? NameContains = null,
    [property: Description("Optional: keep elements whose FAMILY name contains this text (case-insensitive) — including system " +
                           "families such as \"Basiswand\".")]
    string? FamilyNameContains = null,
    [property: Description("Optional: keep elements whose TYPE name contains this text (case-insensitive).")]
    string? TypeNameContains = null,
    [property: Description("Optional: cap the number of elements returned. 'count' still reports how many matched, so a capped " +
                           "answer is never mistaken for a complete one.")]
    int? Limit = null);
