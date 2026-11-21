using System.Globalization;
using Autodesk.Revit.DB;

namespace AnalyseTool.Extensions.Testing;

/// <summary>The model of a Revit document for the engine. Used on the Revit thread only.</summary>
internal sealed class RevitModelAccess(Document document) : IModelAccess
{
    /// <summary>Categories and built-in parameters as this Revit names them, in its language. On the Revit thread.</summary>
    public static RevitNames Names { get; } = RevitNames.Safe(LabelUtils.GetLabelFor, LabelUtils.GetLabelFor);

    public Document? Document => document;

    public string ModelKey
    {
        get
        {
            // Made when the document was created and kept by Save As and copies: a copy keeps its baseline, while two projects
            // started from one template differ (the unique ids of their elements, ProjectInformation's too, do not)
            var created = document.CreationGUID;
            return created != Guid.Empty ? created.ToString("N") : document.Title;
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

        var stored = StoredClass(selector.ElementClass);
        return (stored == typeof(Element) ? collector : collector.OfClass(stored)).Where(selector.ElementClass.IsInstanceOfType);
    }

    /// <summary>
    ///     The class OfClass can filter by: Revit stores rooms, areas and spaces as <c>SpatialElement</c>, and refuses the
    ///     subclasses it does not store (with its own ArgumentException, not .NET's) — then the nearest base it takes.
    /// </summary>
    internal static Type StoredClass(Type elementClass)
    {
        if (typeof(SpatialElement).IsAssignableFrom(elementClass))
        {
            return typeof(SpatialElement);
        }

        for (var type = elementClass; type is not null && type != typeof(Element); type = type.BaseType)
        {
            try
            {
                _ = new ElementClassFilter(type);
                return type;
            }
            catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ArgumentException or ArgumentException)
            {
            }
        }

        return typeof(Element);
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
            StorageType.Double => parameter.AsDouble(),
            _ => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null
        };
        return new ModelValue(true, text, number) { Display = parameter.StorageType == StorageType.Double ? Display(parameter) : null };
    }

    /// <summary>
    ///     Shows a number of the parameter's kind (internal units) in the units of the project, exactly — «3.21 m», not the
    ///     rounded «3 m» of the project's format; <see langword="null" /> for a number without units.
    /// </summary>
    private static Func<double, string>? Display(Parameter parameter)
    {
        try
        {
            var spec = parameter.Definition.GetDataType();
            if (!UnitUtils.IsMeasurableSpec(spec))
            {
                return null;
            }

            var options = parameter.Element.Document.GetUnits().GetFormatOptions(spec);
            var unit = options.GetUnitTypeId();
            var symbol = options.CanHaveSymbol() && !options.GetSymbolTypeId().Empty() ? LabelUtils.GetLabelForSymbol(options.GetSymbolTypeId()) : "";
            return number =>
            {
                var shown = UnitUtils.ConvertFromInternalUnits(number, unit).ToString("0.###", CultureInfo.InvariantCulture);
                return symbol.Length == 0 ? shown : $"{shown} {symbol}";
            };
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }
}
