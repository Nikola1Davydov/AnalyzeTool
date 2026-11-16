using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AnalyseTool.Services.SharedParameters;

// What the shared-parameter commands send and take. The file shape is the same both ways: the editor reads a file,
// changes it and sends the whole of it back.

/// <summary>The shared parameter file as the editor sees it.</summary>
public sealed record SharedParameterFileData
{
    /// <summary>The full path of the file; null when Revit has no shared parameter file set.</summary>
    [Description("Full path of the file; null when Revit has no shared parameter file set.")]
    public string? Path { get; init; }

    /// <summary>Whether the file exists on disk.</summary>
    [Description("Whether the file exists on disk.")]
    public bool Exists { get; init; }

    /// <summary>Whether this is the file Revit currently uses.</summary>
    [Description("Whether this is the file Revit currently uses (Manage → Shared Parameters).")]
    public bool IsRevitCurrent { get; init; }

    /// <summary>The version fingerprint of the file.</summary>
    [Description("Version fingerprint of the file; pass it back to SaveSharedParameterFile.")]
    public string Stamp { get; init; } = string.Empty;

    /// <summary>The parameter groups.</summary>
    [Description("Parameter groups of the file.")]
    public IReadOnlyList<SharedParameterGroupDto> Groups { get; init; } = [];

    /// <summary>The parameter definitions.</summary>
    [Description("Parameter definitions of the file.")]
    public IReadOnlyList<SharedParameterDto> Parameters { get; init; } = [];

    /// <summary>Why the file could not be read, if it could not.</summary>
    [Description("Why the file could not be read, if it could not.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

/// <summary>A group of the shared parameter file.</summary>
public sealed record SharedParameterGroupDto
{
    /// <summary>The group number, unique in the file.</summary>
    [Description("Group number, unique in the file.")]
    public int Id { get; init; }

    /// <summary>The group name.</summary>
    [Description("Group name.")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>A parameter definition of the shared parameter file.</summary>
public sealed record SharedParameterDto
{
    /// <summary>The GUID — the parameter's identity.</summary>
    [Description("Parameter GUID — its identity; never changes once created.")]
    public string Guid { get; init; } = string.Empty;

    /// <summary>The parameter name.</summary>
    [Description("Parameter name.")]
    public string Name { get; init; } = string.Empty;

    /// <summary>The data type as the file spells it.</summary>
    [Description("Data type as the file spells it: TEXT, INTEGER, NUMBER, LENGTH, AREA, VOLUME, ANGLE, YESNO, URL, MATERIAL…")]
    public string DataType { get; init; } = string.Empty;

    /// <summary>For FAMILYTYPE parameters the category id; empty otherwise.</summary>
    [Description("For FAMILYTYPE parameters the category id; empty otherwise.")]
    public string DataCategory { get; init; } = string.Empty;

    /// <summary>The id of the group.</summary>
    [Description("Id of the group the parameter belongs to.")]
    public int GroupId { get; init; }

    /// <summary>Whether the parameter is visible in Revit.</summary>
    [Description("Whether the parameter is visible in Revit.")]
    public bool Visible { get; init; } = true;

    /// <summary>The tooltip text.</summary>
    [Description("Tooltip text.")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Whether users may edit the value.</summary>
    [Description("Whether users may edit the value.")]
    public bool UserModifiable { get; init; } = true;

    /// <summary>Whether Revit hides the parameter when it has no value.</summary>
    [Description("Whether Revit hides the parameter when it has no value.")]
    public bool HideWhenNoValue { get; init; }
}

/// <summary>A parameter the open project has: bound to categories, or only carried by families.</summary>
public sealed record ProjectParameterDto
{
    /// <summary>The <c>ParameterElement</c> id.</summary>
    public long Id { get; init; }

    /// <summary>The GUID of a shared parameter; absent for a project parameter.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Guid { get; init; }

    /// <summary>The parameter name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Shared (has a GUID) or project parameter.</summary>
    public bool IsShared { get; init; }

    /// <summary>Bound to categories; false — only loaded families carry it.</summary>
    public bool Bound { get; init; }

    /// <summary>Instance binding (true) or type binding (false); false when not bound.</summary>
    public bool IsInstance { get; init; }

    /// <summary>The data type, as Revit labels it.</summary>
    public string DataType { get; init; } = string.Empty;

    /// <summary>The properties-palette group, as Revit labels it.</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>The names of the categories the parameter is bound to.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];
}

/// <summary>The shared and project parameters of the open document.</summary>
/// <param name="Parameters">The parameters by name.</param>
public sealed record ProjectParametersResult(IReadOnlyList<ProjectParameterDto> Parameters);

/// <summary>A category a parameter can be bound to.</summary>
/// <param name="Id">The category id.</param>
/// <param name="Name">The localised name.</param>
/// <param name="Type"><c>Model</c> or <c>Annotation</c>.</param>
public sealed record BindableCategoryDto(long Id, string Name, string Type);

/// <summary>A properties-palette group.</summary>
/// <param name="TypeId">The <c>GroupTypeId</c>, e.g. <c>autodesk.parameter.group:data</c>.</param>
/// <param name="Label">The localised label.</param>
public sealed record ParameterGroupOptionDto(string TypeId, string Label);

/// <summary>What a parameter can be bound to in the open document.</summary>
/// <param name="Categories">The categories that accept bound parameters.</param>
/// <param name="Groups">The groups a parameter can be placed in.</param>
/// <param name="DefaultGroup">The group to preselect (Data).</param>
public sealed record BindingOptionsResult(IReadOnlyList<BindableCategoryDto> Categories, IReadOnlyList<ParameterGroupOptionDto> Groups, string DefaultGroup);

/// <summary>A parameter that could not be added, and why.</summary>
/// <param name="Name">The parameter name or GUID.</param>
/// <param name="Reason">Why it was not added.</param>
public sealed record BindProblemDto(string Name, string Reason);

/// <summary>What <c>BindSharedParameters</c> did.</summary>
/// <param name="Added">Parameters newly added to the project.</param>
/// <param name="Updated">Parameters already in the project that got the categories added.</param>
/// <param name="Problems">Parameters that could not be added, with the reason.</param>
public sealed record BindResult(IReadOnlyList<string> Added, IReadOnlyList<string> Updated, IReadOnlyList<BindProblemDto> Problems);

/// <summary>Filled against total for one category or level.</summary>
/// <param name="Name">The category or level name.</param>
/// <param name="Total">Elements that carry the parameter.</param>
/// <param name="Filled">Of those, how many have a value.</param>
public sealed record ReportCountDto(string Name, int Total, int Filled);

/// <summary>A value and how often it occurs.</summary>
/// <param name="Value">The value as Revit displays it.</param>
/// <param name="Count">How many elements have it.</param>
public sealed record ReportValueDto(string Value, int Count);

/// <summary>How well one parameter is filled.</summary>
public sealed record ParameterReportDto
{
    /// <summary>The parameter name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The GUID of a shared parameter.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Guid { get; init; }

    /// <summary>Instance or type parameter; a type parameter is counted per instance with its type's value.</summary>
    public bool IsInstance { get; init; }

    /// <summary>Elements that carry the parameter.</summary>
    public int Total { get; init; }

    /// <summary>Of those, how many have a value.</summary>
    public int Filled { get; init; }

    /// <summary>Filled against total per category.</summary>
    public IReadOnlyList<ReportCountDto> Categories { get; init; } = [];

    /// <summary>Filled against total per level, bottom to top; elements without a level last.</summary>
    public IReadOnlyList<ReportCountDto> Levels { get; init; } = [];

    /// <summary>The most frequent values, most frequent first.</summary>
    public IReadOnlyList<ReportValueDto> Values { get; init; } = [];

    /// <summary>Elements whose value is not among the listed ones.</summary>
    public int OtherValuesCount { get; init; }

    /// <summary>How many different values occur.</summary>
    public int DistinctValues { get; init; }

    /// <summary>Why the parameter could not be evaluated, if it could not.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

/// <summary>The report numbers of a document.</summary>
/// <param name="DocumentTitle">The title of the evaluated document.</param>
/// <param name="Parameters">One entry per requested parameter, in request order.</param>
public sealed record ParameterReportResult(string DocumentTitle, IReadOnlyList<ParameterReportDto> Parameters);
