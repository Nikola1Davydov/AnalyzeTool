namespace AnalyseTool.Services.Parameters;

/// <summary>Where a parameter comes from.</summary>
public enum ParameterOrigin
{
    /// <summary>A shared parameter: it has a GUID from a shared parameter file.</summary>
    Shared,

    /// <summary>A project parameter: a <c>ParameterElement</c> without a GUID.</summary>
    Project,

    /// <summary>A built-in parameter of Revit.</summary>
    BuiltIn
}

/// <summary>One parameter of an element, as plain data.</summary>
/// <param name="Name">The parameter name, localised.</param>
/// <param name="Id">The parameter id: a negative <c>BuiltInParameter</c> value or the <c>ParameterElement</c> id.</param>
/// <param name="Level">The name of the element's level; empty when it has none.</param>
/// <param name="ElementId">The id of the element that carries the parameter.</param>
/// <param name="IsTypeParameter">Whether the element is a type.</param>
/// <param name="Origin">Built-in, project or shared.</param>
/// <param name="Value">The value in the document's display units, as text.</param>
/// <param name="StorageType">The storage type: <c>String</c>, <c>Integer</c>, <c>Double</c>, <c>ElementId</c>, <c>None</c>.</param>
/// <param name="IsReadOnly">Whether the parameter can be written.</param>
public sealed record ParameterData(
    string Name,
    long Id,
    string Level,
    long ElementId,
    bool IsTypeParameter,
    ParameterOrigin Origin,
    string Value,
    string StorageType,
    bool IsReadOnly)
{
    /// <summary>Describes a parameter; call it on the Revit thread.</summary>
    public static ParameterData Of(Parameter parameter, long elementId, string level, bool isTypeParameter)
    {
        return new ParameterData(
            parameter.Definition.Name,
            parameter.Id.Value,
            level,
            elementId,
            isTypeParameter,
            parameter.GetParameterOrigin(),
            parameter.GetParameterValue(),
            parameter.StorageType.ToString(),
            parameter.IsReadOnly);
    }
}
