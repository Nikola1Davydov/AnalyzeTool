using Autodesk.Revit.DB;

namespace AnalyseTool.Extensions.Testing;

/// <summary>An element as the results show it.</summary>
/// <param name="Id">The element id.</param>
/// <param name="UniqueId">The id that survives file copies: what the baseline keeps.</param>
/// <param name="Label">Category, name and type, for people.</param>
public sealed record ElementInfo(long Id, string UniqueId, string Label);

/// <summary>
///     Everything the engine needs from the model, so the engine runs without Revit in the tests:
///     <see cref="RevitModelAccess" /> in Revit, a fake in the tests.
/// </summary>
internal interface IModelAccess
{
    /// <summary>Gets the model; <see langword="null" /> outside Revit.</summary>
    Document? Document { get; }

    /// <summary>Gets the key the baseline and the history of this model are filed under.</summary>
    string ModelKey { get; }

    /// <summary>Gets the model's title.</summary>
    string ModelTitle { get; }

    /// <summary>The elements a selector picks.</summary>
    IEnumerable<Element> Collect(ElementSelector selector);

    /// <summary>Describes an element.</summary>
    ElementInfo Describe(Element element);

    /// <summary>Reads a value of an element: a parameter by its built-in id when given, otherwise by name.</summary>
    ModelValue Read(Element element, ValueSource source, string name, BuiltInParameter? builtIn = null);

    /// <summary>Writes a parameter, as typed in Revit; throws with the reason when it cannot.</summary>
    void Write(Element element, ValueSource source, string name, string value, BuiltInParameter? builtIn = null);

    /// <summary>The name of a built-in parameter as this Revit shows it.</summary>
    string Label(BuiltInParameter parameter);

    /// <summary>Finds an element by its unique id.</summary>
    Element? Find(string uniqueId);

    /// <summary>A point as [x, y, z] in feet, as <c>ShowGeometry</c> takes it; <see langword="null" /> without Revit.</summary>
    double[]? Point(XYZ? point);

    /// <summary>A box as [minX, minY, minZ, maxX, maxY, maxZ] in feet.</summary>
    double[]? Box(Outline? box);
}
