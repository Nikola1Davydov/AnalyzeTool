using AnalyseTool.Extensions.Testing;
using AnalyseTool.Tests.Extensions;
using Autodesk.Revit.DB;

namespace AnalyseTool.Tests.ModelTests;

/// <summary>
///     A model without Revit for the engine: elements are instances of the stub <c>Element</c> (<see cref="RevitStubs" />),
///     their categories, types and parameters live here.
/// </summary>
internal sealed class FakeModel : IModelAccess
{
    private readonly Dictionary<Element, Item> _items = new(ReferenceEqualityComparer.Instance);
    private readonly List<Element> _order = [];
    private readonly Dictionary<(Element, string), double> _lengths = [];

    /// <summary>The length unit of the project, as Revit's project units would set it: «mm» or «m».</summary>
    public string LengthUnit { get; set; } = "mm";

    static FakeModel()
    {
        RevitStubs.Install();
    }

    public Document? Document => null;

    public string ModelKey { get; init; } = "model-1";

    public string ModelTitle => "Test model";

    /// <summary>Adds an element; a parameter with a null value exists but has none, an absent one is missing.</summary>
    public Element Add(long id, BuiltInCategory category, string type = "Type A", params (string Name, string? Value)[] parameters)
    {
        var element = (Element)Activator.CreateInstance(typeof(Element), nonPublic: true)!;
        _items[element] = new Item(id, $"uid-{id}", category, type, parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase));
        _order.Add(element);
        return element;
    }

    public Element Remove(long id)
    {
        var element = _order.Single(item => _items[item].Id == id);
        _order.Remove(element);
        _items.Remove(element);
        return element;
    }

    /// <summary>
    ///     Gives an element a length parameter as Revit holds it: in feet, its text rounded the way the project shows it (whole
    ///     units), its exact value shown in the project's unit.
    /// </summary>
    public void Length(Element element, string name, double feet)
    {
        _lengths[(element, name)] = feet;
    }

    private double PerFoot => LengthUnit == "m" ? 0.3048 : 304.8;

    public string? Value(Element element, string name)
    {
        return _items[element].Parameters.GetValueOrDefault(name);
    }

    public Element ById(long id)
    {
        return _order.Single(item => _items[item].Id == id);
    }

    public IEnumerable<Element> Collect(ElementSelector selector)
    {
        return _order.Where(element => selector.ElementClass is not null || selector.Categories.Contains(_items[element].Category)).ToList();
    }

    public ElementInfo Describe(Element element)
    {
        var item = _items[element];
        return new ElementInfo(item.Id, item.UniqueId, $"{ElementSelector.CategoryName(item.Category)} · {item.Type} · {item.Id}");
    }

    public ModelValue Read(Element element, ValueSource source, string name, BuiltInParameter? builtIn = null)
    {
        var item = _items[element];
        if (source == ValueSource.TypeName)
        {
            return ModelValue.Of(item.Type);
        }

        var key = builtIn?.ToString() ?? name;
        if (_lengths.TryGetValue((element, key), out var feet))
        {
            var unit = LengthUnit;
            var perFoot = PerFoot;
            return new ModelValue(true, $"{Math.Round(feet * perFoot)} {unit}", feet)
            {
                Display = number => $"{(number * perFoot).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} {unit}"
            };
        }

        return item.Parameters.TryGetValue(key, out var value) ? value is null ? new ModelValue(true, null, null) : ModelValue.Of(value) : ModelValue.Missing;
    }

    public void Write(Element element, ValueSource source, string name, string value, BuiltInParameter? builtIn = null)
    {
        var item = _items[element];
        var key = builtIn?.ToString() ?? name;
        if (!item.Parameters.ContainsKey(key))
        {
            throw new InvalidOperationException($"has no parameter «{name}»");
        }

        item.Parameters[key] = value;
    }

    public string Label(BuiltInParameter parameter)
    {
        // By name: a switch over the constants of the reference assembly does not meet the values of the stub enum
        return parameter.ToString() switch
        {
            "ALL_MODEL_MARK" => "Mark",
            "ROOM_NUMBER" => "Number",
            var other => other
        };
    }

    public Element? Find(string uniqueId)
    {
        return _order.FirstOrDefault(element => _items[element].UniqueId == uniqueId);
    }

    public double[]? Point(XYZ? point)
    {
        return null;
    }

    public double[]? Box(Outline? box)
    {
        return null;
    }

    private sealed record Item(long Id, string UniqueId, BuiltInCategory Category, string Type, Dictionary<string, string?> Parameters);
}
