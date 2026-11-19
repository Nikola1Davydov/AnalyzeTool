using System.Globalization;
using Autodesk.Revit.DB;

namespace AnalyseTool.Extensions.Testing;

/// <summary>The model of a Revit document for the engine. Used on the Revit thread only.</summary>
internal sealed class RevitModelAccess(Document document) : IModelAccess
{
    public Document? Document => document;

    public string ModelKey
    {
        get
        {
            // The project information survives Save As and copies: a copy of the model keeps its baseline
            var info = document.ProjectInformation;
            return info is not null && !string.IsNullOrEmpty(info.UniqueId) ? info.UniqueId : document.Title;
        }
    }

    public string ModelTitle => document.Title;

    public IEnumerable<Element> Collect(ElementSelector selector)
    {
        if (selector.ElementClass is null)
        {
            return new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(selector.Categories.ToList()));
        }

        var collector = new FilteredElementCollector(document);
        if (!typeof(ElementType).IsAssignableFrom(selector.ElementClass))
        {
            collector = collector.WhereElementIsNotElementType();
        }

        // OfClass takes only the classes Revit stores (not Room, not FamilyInstance subclasses): the nearest base it takes
        for (var type = selector.ElementClass; type is not null && type != typeof(Element); type = type.BaseType)
        {
            try
            {
                return collector.OfClass(type).Where(selector.ElementClass.IsInstanceOfType);
            }
            catch (ArgumentException)
            {
            }
        }

        return collector.Where(selector.ElementClass.IsInstanceOfType);
    }

    public ElementInfo Describe(Element element)
    {
        var category = element.Category?.Name;
        var typeName = element.GetTypeId() is { } typeId && typeId != ElementId.InvalidElementId ? document.GetElement(typeId)?.Name : null;
        var name = element.Name;
        var label = string.Join(" · ", new[] { category, name, typeName != name ? typeName : null }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return new ElementInfo(element.Id.Value, element.UniqueId, label.Length == 0 ? element.Id.Value.ToString(CultureInfo.InvariantCulture) : label);
    }

    public ModelValue Read(Element element, ValueSource source, string name, BuiltInParameter? builtIn = null)
    {
        switch (source)
        {
            case ValueSource.TypeName:
                return element.GetTypeId() is { } id && document.GetElement(id) is { } type ? ModelValue.Of(type.Name) : ModelValue.Missing;
            case ValueSource.TypeParameter:
                return element.GetTypeId() is { } typeId && document.GetElement(typeId) is { } elementType ? Value(Lookup(elementType, name, builtIn)) : ModelValue.Missing;
            default:
                return Value(Lookup(element, name, builtIn));
        }
    }

    public void Write(Element element, ValueSource source, string name, string value, BuiltInParameter? builtIn = null)
    {
        var target = source == ValueSource.TypeParameter ? document.GetElement(element.GetTypeId()) : element;
        var parameter = (target is null ? null : Lookup(target, name, builtIn)) ?? throw new InvalidOperationException($"has no parameter «{name}»");
        if (parameter.IsReadOnly)
        {
            throw new InvalidOperationException($"«{name}» is read-only");
        }

        var done = parameter.StorageType switch
        {
            StorageType.String => parameter.Set(value),
            StorageType.Integer when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => parameter.Set(number),
            StorageType.Integer or StorageType.Double => parameter.SetValueString(value),
            _ => throw new InvalidOperationException($"«{name}» holds an element reference: write the repair with Fix(...)")
        };
        if (!done)
        {
            throw new InvalidOperationException($"Revit did not take '{value}' for «{name}»");
        }
    }

    public string Label(BuiltInParameter parameter)
    {
        return LabelUtils.GetLabelFor(parameter);
    }

    public Element? Find(string uniqueId)
    {
        return document.GetElement(uniqueId);
    }

    public double[]? Point(XYZ? point)
    {
        return point is null ? null : [point.X, point.Y, point.Z];
    }

    public double[]? Box(Outline? box)
    {
        return box is null ? null : [box.MinimumPoint.X, box.MinimumPoint.Y, box.MinimumPoint.Z, box.MaximumPoint.X, box.MaximumPoint.Y, box.MaximumPoint.Z];
    }

    private static Parameter? Lookup(Element element, string name, BuiltInParameter? builtIn)
    {
        return builtIn is { } id ? element.get_Parameter(id) : element.LookupParameter(name);
    }

    private static ModelValue Value(Parameter? parameter)
    {
        if (parameter is null)
        {
            return ModelValue.Missing;
        }

        if (!parameter.HasValue)
        {
            return new ModelValue(true, null, null);
        }

        var text = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString() ?? parameter.AsString();
        double? number = parameter.StorageType switch
        {
            StorageType.Integer => parameter.AsInteger(),
            StorageType.Double => Displayed(parameter),
            _ => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null
        };
        return new ModelValue(true, text, number);
    }

    /// <summary>A length, an area… in the units the model shows, as InRange expects.</summary>
    private static double Displayed(Parameter parameter)
    {
        try
        {
            return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), parameter.GetUnitTypeId());
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return parameter.AsDouble();
        }
    }
}
