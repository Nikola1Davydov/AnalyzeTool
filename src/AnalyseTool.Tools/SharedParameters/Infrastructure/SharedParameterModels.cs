using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Tools.SharedParameters
{
    // What the shared-parameter commands send and take. The file shape is the same both ways: the
    // editor reads a file, changes it and sends the whole of it back.

    /// <summary>The shared parameter file as the editor sees it.</summary>
    public sealed record SharedParameterFileData
    {
        [JsonProperty("path")]
        [Description("Full path of the file; null when Revit has no shared parameter file set.")]
        public string? Path { get; init; }

        [JsonProperty("exists")]
        [Description("Whether the file exists on disk.")]
        public bool Exists { get; init; }

        [JsonProperty("isRevitCurrent")]
        [Description("Whether this is the file Revit currently uses (Manage → Shared Parameters).")]
        public bool IsRevitCurrent { get; init; }

        [JsonProperty("stamp")]
        [Description("Version fingerprint of the file; pass it back to SaveSharedParameterFile.")]
        public string Stamp { get; init; } = string.Empty;

        [JsonProperty("groups")]
        [Description("Parameter groups of the file.")]
        public List<SharedParameterGroupDto> Groups { get; init; } = new();

        [JsonProperty("parameters")]
        [Description("Parameter definitions of the file.")]
        public List<SharedParameterDto> Parameters { get; init; } = new();

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        [Description("Why the file could not be read, if it could not.")]
        public string? Error { get; init; }
    }

    public sealed record SharedParameterGroupDto
    {
        [JsonProperty("id")]
        [Description("Group number, unique in the file.")]
        public int Id { get; init; }

        [JsonProperty("name")]
        [Description("Group name.")]
        public string Name { get; init; } = string.Empty;
    }

    public sealed record SharedParameterDto
    {
        [JsonProperty("guid")]
        [Description("Parameter GUID — its identity; never changes once created.")]
        public string Guid { get; init; } = string.Empty;

        [JsonProperty("name")]
        [Description("Parameter name.")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("dataType")]
        [Description("Data type as the file spells it: TEXT, INTEGER, NUMBER, LENGTH, AREA, VOLUME, ANGLE, YESNO, URL, MATERIAL…")]
        public string DataType { get; init; } = string.Empty;

        [JsonProperty("dataCategory")]
        [Description("For FAMILYTYPE parameters the category id; empty otherwise.")]
        public string DataCategory { get; init; } = string.Empty;

        [JsonProperty("groupId")]
        [Description("Id of the group the parameter belongs to.")]
        public int GroupId { get; init; }

        [JsonProperty("visible")]
        [Description("Whether the parameter is visible in Revit.")]
        public bool Visible { get; init; } = true;

        [JsonProperty("description")]
        [Description("Tooltip text.")]
        public string Description { get; init; } = string.Empty;

        [JsonProperty("userModifiable")]
        [Description("Whether users may edit the value.")]
        public bool UserModifiable { get; init; } = true;

        [JsonProperty("hideWhenNoValue")]
        [Description("Whether Revit hides the parameter when it has no value.")]
        public bool HideWhenNoValue { get; init; }
    }

    /// <summary>A parameter the open project has: bound to categories, or only carried by families.</summary>
    public sealed record ProjectParameterDto
    {
        [JsonProperty("id")]
        [Description("ElementId of the ParameterElement.")]
        public long Id { get; init; }

        [JsonProperty("guid", NullValueHandling = NullValueHandling.Ignore)]
        [Description("GUID for a shared parameter; absent for a project parameter.")]
        public string? Guid { get; init; }

        [JsonProperty("name")]
        [Description("Parameter name.")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("isShared")]
        [Description("Shared (has a GUID) or project parameter.")]
        public bool IsShared { get; init; }

        [JsonProperty("bound")]
        [Description("Bound to categories in the project; false = only carried by loaded families.")]
        public bool Bound { get; init; }

        [JsonProperty("isInstance")]
        [Description("Instance binding (true) or type binding (false); false when not bound.")]
        public bool IsInstance { get; init; }

        [JsonProperty("dataType")]
        [Description("Data type, as Revit labels it.")]
        public string DataType { get; init; } = string.Empty;

        [JsonProperty("group")]
        [Description("Properties-palette group, as Revit labels it.")]
        public string Group { get; init; } = string.Empty;

        [JsonProperty("categories")]
        [Description("Names of the categories the parameter is bound to.")]
        public List<string> Categories { get; init; } = new();
    }

    public sealed record ProjectParametersResult
    {
        [JsonProperty("parameters")]
        [Description("Shared and project parameters of the open document.")]
        public List<ProjectParameterDto> Parameters { get; init; } = new();
    }

    public sealed record BindableCategoryDto
    {
        [JsonProperty("id")]
        [Description("Category ElementId.")]
        public long Id { get; init; }

        [JsonProperty("name")]
        [Description("Localised category name.")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("type")]
        [Description("Model or Annotation.")]
        public string Type { get; init; } = string.Empty;
    }

    public sealed record ParameterGroupOptionDto
    {
        [JsonProperty("typeId")]
        [Description("GroupTypeId, e.g. autodesk.parameter.group:data.")]
        public string TypeId { get; init; } = string.Empty;

        [JsonProperty("label")]
        [Description("Localised group label.")]
        public string Label { get; init; } = string.Empty;
    }

    public sealed record BindingOptionsResult
    {
        [JsonProperty("categories")]
        [Description("Categories that accept bound parameters.")]
        public List<BindableCategoryDto> Categories { get; init; } = new();

        [JsonProperty("groups")]
        [Description("Properties-palette groups a parameter can be placed in.")]
        public List<ParameterGroupOptionDto> Groups { get; init; } = new();

        [JsonProperty("defaultGroup")]
        [Description("The group to preselect (Data).")]
        public string DefaultGroup { get; init; } = string.Empty;
    }

    public sealed record BindProblemDto
    {
        [JsonProperty("name")]
        [Description("Parameter name or GUID.")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("reason")]
        [Description("Why it was not added.")]
        public string Reason { get; init; } = string.Empty;
    }

    public sealed record BindResult
    {
        [JsonProperty("added")]
        [Description("Parameters newly added to the project.")]
        public List<string> Added { get; init; } = new();

        [JsonProperty("updated")]
        [Description("Parameters that were already in the project and got the categories added.")]
        public List<string> Updated { get; init; } = new();

        [JsonProperty("problems")]
        [Description("Parameters that could not be added, with the reason.")]
        public List<BindProblemDto> Problems { get; init; } = new();
    }

    // ---- The report ----------------------------------------------------------------------------

    public sealed record ReportCountDto
    {
        [JsonProperty("name")]
        [Description("Category or level name.")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("total")]
        [Description("Elements that carry the parameter.")]
        public int Total { get; init; }

        [JsonProperty("filled")]
        [Description("Of those, how many have a value.")]
        public int Filled { get; init; }
    }

    public sealed record ReportValueDto
    {
        [JsonProperty("value")]
        [Description("A value as Revit displays it.")]
        public string Value { get; init; } = string.Empty;

        [JsonProperty("count")]
        [Description("How many elements have it.")]
        public int Count { get; init; }
    }

    public sealed record ParameterReportDto
    {
        [JsonProperty("name")]
        [Description("Parameter name.")]
        public string Name { get; init; } = string.Empty;

        [JsonProperty("guid", NullValueHandling = NullValueHandling.Ignore)]
        [Description("GUID for a shared parameter.")]
        public string? Guid { get; init; }

        [JsonProperty("isInstance")]
        [Description("Instance or type parameter. For a type parameter every instance is counted with its type's value.")]
        public bool IsInstance { get; init; }

        [JsonProperty("total")]
        [Description("Elements that carry the parameter.")]
        public int Total { get; init; }

        [JsonProperty("filled")]
        [Description("Of those, how many have a value.")]
        public int Filled { get; init; }

        [JsonProperty("categories")]
        [Description("Filled vs total per category.")]
        public List<ReportCountDto> Categories { get; init; } = new();

        [JsonProperty("levels")]
        [Description("Filled vs total per level, bottom to top; elements without a level last.")]
        public List<ReportCountDto> Levels { get; init; } = new();

        [JsonProperty("values")]
        [Description("The most frequent values, most frequent first.")]
        public List<ReportValueDto> Values { get; init; } = new();

        [JsonProperty("otherValuesCount")]
        [Description("Elements whose value is not among the listed ones.")]
        public int OtherValuesCount { get; init; }

        [JsonProperty("distinctValues")]
        [Description("How many different values occur.")]
        public int DistinctValues { get; init; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        [Description("Why the parameter could not be evaluated, if it could not.")]
        public string? Error { get; init; }
    }

    public sealed record ParameterReportResult
    {
        [JsonProperty("documentTitle")]
        [Description("Title of the evaluated document.")]
        public string DocumentTitle { get; init; } = string.Empty;

        [JsonProperty("parameters")]
        [Description("One entry per requested parameter, in request order.")]
        public List<ParameterReportDto> Parameters { get; init; } = new();
    }
}
