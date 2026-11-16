using System.Text.Json.Serialization;
using AnalyseTool.Services.Parameters;

namespace AnalyseTool.Services.Elements;

/// <summary>A parameter of a type with its display value.</summary>
/// <param name="Name">The localised name.</param>
/// <param name="Value">The value as Revit displays it; empty when it has none.</param>
/// <param name="Id">The parameter id — tells two parameters of one name apart; what <c>SetDataToParameters</c> takes.</param>
/// <param name="BuiltInParameter">The <c>BuiltInParameter</c> name; absent for shared and project parameters.</param>
/// <param name="Spec">What the value means: <c>length</c>, <c>area</c>, <c>number</c>, <c>string</c>…</param>
/// <param name="Unit">The document unit of a measurable spec; absent otherwise.</param>
public sealed record FamilyParameterInfo(
    string Name,
    string Value,
    long Id = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BuiltInParameter = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Spec = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Unit = null);

/// <summary>The parameters of one type.</summary>
/// <param name="TypeId">The type id.</param>
/// <param name="Parameters">All its parameters, empty ones included, by name.</param>
public sealed record TypeParametersInfo(long TypeId, IReadOnlyList<FamilyParameterInfo> Parameters);

/// <summary>The parameters of a batch of types.</summary>
/// <param name="Types">One entry per type found; unknown ids are skipped.</param>
public sealed record TypeParametersResult(IReadOnlyList<TypeParametersInfo> Types);

/// <summary>A user workset.</summary>
/// <param name="Id">The workset id.</param>
/// <param name="Name">The workset name.</param>
/// <param name="IsOpen">Whether it is open.</param>
/// <param name="IsEditable">Whether the current user owns it.</param>
/// <param name="Owner">The user who owns it; empty when nobody does.</param>
public sealed record WorksetInfo(int Id, string Name, bool IsOpen, bool IsEditable, string Owner);

/// <summary>The user worksets of a document.</summary>
/// <param name="IsWorkshared">Whether the document is workshared.</param>
/// <param name="Worksets">The user worksets by name; empty for a non-workshared document.</param>
public sealed record WorksetsResult(bool IsWorkshared, IReadOnlyList<WorksetInfo> Worksets);

/// <summary>
///     Two document-wide reads: the workset table, and the type parameters of a batch of types. Call them on the Revit thread.
/// </summary>
public sealed class TypeAndWorksetService
{
    /// <summary>All parameters, empty ones included, of the types with these ids; ids that are not types are skipped.</summary>
    public TypeParametersResult GetTypeParameters(Document document, IReadOnlyList<long> typeIds)
    {
        var types = new List<TypeParametersInfo>();
        foreach (var id in typeIds)
        {
            if (document.GetElement(new ElementId(id)) is not ElementType type)
            {
                continue;
            }

            types.Add(new TypeParametersInfo(id, ReadTypeParameters(type)));
        }

        return new TypeParametersResult(types);
    }

    /// <summary>The user worksets; none for a non-workshared document.</summary>
    public WorksetsResult GetWorksets(Document document)
    {
        if (!document.IsWorkshared)
        {
            return new WorksetsResult(false, []);
        }

        var worksets = new FilteredWorksetCollector(document)
            .OfKind(WorksetKind.UserWorkset)
            .Select(workset => new WorksetInfo(workset.Id.IntegerValue, workset.Name, workset.IsOpen, workset.IsEditable, workset.Owner ?? string.Empty))
            .OrderBy(workset => workset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WorksetsResult(true, worksets);
    }

    private static List<FamilyParameterInfo> ReadTypeParameters(Element type)
    {
        var list = new List<FamilyParameterInfo>();
        foreach (Parameter parameter in type.Parameters)
        {
            if (parameter?.Definition is null)
            {
                continue;
            }

            var value = ReadParameterValue(type.Document, parameter);
            var (spec, unit) = parameter.DescribeUnits();
            list.Add(new FamilyParameterInfo(
                parameter.Definition.Name,
                value,
                parameter.Id.Value,
                ParameterUtils.IsBuiltInParameter(parameter.Id) ? ((BuiltInParameter)parameter.Id.Value).ToString() : null,
                spec,
                unit));
        }

        return list.OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ReadParameterValue(Document document, Parameter parameter)
    {
        if (!parameter.HasValue)
        {
            return string.Empty;
        }

        switch (parameter.StorageType)
        {
            case StorageType.String:
                return parameter.AsString() ?? string.Empty;
            case StorageType.Integer: // Yes/No included: AsValueString renders it
            case StorageType.Double: // in the project's display units
                return parameter.AsValueString() ?? string.Empty;
            case StorageType.ElementId:
                // AsValueString on ElementId parameters is a known crash path: resolve the name ourselves
                var id = parameter.AsElementId();
                return id == ElementId.InvalidElementId ? string.Empty : document.GetElement(id)?.Name ?? string.Empty;
            default: // None: nothing to read, and AsValueString can crash Revit here
                return string.Empty;
        }
    }
}
