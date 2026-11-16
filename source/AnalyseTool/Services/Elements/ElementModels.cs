using System.Text.Json.Serialization;
using AnalyseTool.Services.Parameters;

namespace AnalyseTool.Services.Elements;

/// <summary>An element with all its parameters — the heavy, page-shaped listing of <c>GetDataByCategoryName</c>.</summary>
/// <param name="Name">The element name.</param>
/// <param name="Id">The element id.</param>
/// <param name="Level">The name of the element's level; empty when it has none.</param>
/// <param name="IsElementType">Whether the element is a type.</param>
/// <param name="ElementTypeId">The id of its type; -1 when it has none.</param>
/// <param name="CategoryName">The localised category name.</param>
/// <param name="Parameters">Every parameter of the element.</param>
public sealed record DataElement(
    string Name,
    long Id,
    string Level,
    bool IsElementType,
    long ElementTypeId,
    string? CategoryName,
    IReadOnlyList<ParameterData> Parameters)
{
    /// <summary>Describes an element with all its parameters; call it on the Revit thread.</summary>
    public static DataElement Of(Element element)
    {
        var id = element.Id.Value;
        var level = element.Document.GetElement(element.LevelId)?.Name ?? string.Empty;
        // Not GetTypeId() == invalid: levels, grids and rooms have no type and are still instances
        var isType = element is ElementType;

        var parameters = new List<ParameterData>();
        foreach (Parameter parameter in element.Parameters)
        {
            parameters.Add(ParameterData.Of(parameter, id, level, isType));
        }

        return new DataElement(element.Name, id, level, isType, element.GetTypeId().Value, element.Category?.Name, parameters);
    }
}

/// <summary>
///     A lean element for AI callers and lists: identity, family, type, and only the parameters the caller asked for.
/// </summary>
/// <param name="Id">The element id.</param>
/// <param name="Name">The element name.</param>
/// <param name="Category">The localised category name.</param>
/// <param name="BuiltInCategory">The <c>BuiltInCategory</c> name (<c>OST_Walls</c>); absent for a category that is not built in.</param>
/// <param name="Level">The level name; empty when the element has none.</param>
/// <param name="LevelId">The level id — the join to <c>GetModelOverview.levels</c>; absent when the element has no level.</param>
/// <param name="IsType">Whether the element is a type.</param>
/// <param name="FamilyId">The owning family; absent for system families, which have no Family element.</param>
/// <param name="FamilyName">The family name, system families included.</param>
/// <param name="TypeName">The type name: its own for a type, its type's for an instance.</param>
/// <param name="Parameters">Name → value of the requested parameters; absent when none were requested.</param>
public sealed record ElementItem(
    long Id,
    string Name,
    string Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BuiltInCategory,
    string Level,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? LevelId,
    bool IsType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? FamilyId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FamilyName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TypeName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string>? Parameters);

/// <summary>
///     What a category query answered: the count before the limit beside what was returned, an error with suggestions for
///     an unknown category instead of an empty list, and which kind was answered.
/// </summary>
/// <param name="Category">The resolved localised category name; null when it was not resolved.</param>
/// <param name="ElementKind"><c>instances</c>, <c>types</c> or <c>all</c>.</param>
/// <param name="Count">The matches before the limit.</param>
/// <param name="Returned">How many elements the answer holds.</param>
/// <param name="Elements">The elements.</param>
/// <param name="Error">Why nothing was answered, if so.</param>
/// <param name="DidYouMean">Category names close to an unknown one; absent otherwise.</param>
public sealed record ElementsResult(
    string? Category,
    string ElementKind,
    int Count,
    int Returned,
    IReadOnlyList<ElementItem> Elements,
    string? Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? DidYouMean);

/// <summary>The parameters of a category query.</summary>
public sealed class ElementQuery
{
    /// <summary>The localised category name.</summary>
    public string? Category { get; init; }

    /// <summary>The <c>BuiltInCategory</c> name; wins over <see cref="Category" />.</summary>
    public string? BuiltInCategory { get; init; }

    /// <summary><c>instances</c> (default), <c>types</c> or <c>all</c>.</summary>
    public string? ElementKind { get; init; }

    /// <summary>Keep elements whose own, family or type name contains this text.</summary>
    public string? NameContains { get; init; }

    /// <summary>Keep elements whose family name contains this text.</summary>
    public string? FamilyNameContains { get; init; }

    /// <summary>Keep elements whose type name contains this text.</summary>
    public string? TypeNameContains { get; init; }

    /// <summary>The parameters to return per element.</summary>
    public IReadOnlyCollection<string>? ParameterNames { get; init; }

    /// <summary>The maximum number of elements to return.</summary>
    public int? Limit { get; init; }
}

/// <summary>What <c>GetCategoryParameters</c> answers: the resolved category, its parameters, or an error with suggestions.</summary>
/// <param name="Category">The localised category name.</param>
/// <param name="BuiltInCategory">The <c>BuiltInCategory</c> name.</param>
/// <param name="Count">How many parameters.</param>
/// <param name="Parameters">The parameters.</param>
/// <param name="Error">Why nothing was answered, if so.</param>
/// <param name="DidYouMean">Category names close to an unknown one; absent otherwise.</param>
public sealed record CategoryParametersResult(
    string? Category,
    string? BuiltInCategory,
    int Count,
    IReadOnlyList<CategoryParameterInfo> Parameters,
    string? Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? DidYouMean);

/// <summary>A parameter available on a category, sampled from a representative element.</summary>
/// <param name="Id">The id <c>SetDataToParameters</c> takes: a negative <c>BuiltInParameter</c> value or the <c>ParameterElement</c> id.</param>
/// <param name="BuiltInParameter">The <c>BuiltInParameter</c> name; absent for shared and project parameters.</param>
/// <param name="Name">The localised name.</param>
/// <param name="StorageType">The storage type.</param>
/// <param name="Spec">What the value means: <c>length</c>, <c>area</c>, <c>number</c>, <c>string</c>…</param>
/// <param name="Unit">The document unit of a measurable spec; absent otherwise.</param>
/// <param name="IsReadOnly">Whether the parameter can be written.</param>
/// <param name="IsType">Whether it is a type parameter.</param>
public sealed record CategoryParameterInfo(
    long Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BuiltInParameter,
    string Name,
    string StorageType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Spec,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Unit,
    bool IsReadOnly,
    bool IsType);
