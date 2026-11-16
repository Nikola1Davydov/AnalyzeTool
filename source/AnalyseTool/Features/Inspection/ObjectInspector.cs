using System.Collections;
using System.Reflection;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Features.Inspection;

/// <summary>
///     Reads the public properties of a Revit object by reflection and turns their values into plain data: numbers and
///     strings as they are, elements as references by id, other objects as paths to read next.
/// </summary>
/// <remarks>
///     <para>
///         One level per call: a nested object is not expanded but gets a path, an element gets its id. Methods are never
///         called — they may change the model or take long; the few safe ones a caller needs are members of their own
///         (<c>BoundingBox</c>, <c>Geometry</c>). A property that throws becomes a note, not a failure.
///     </para>
///     <para>Revit thread only. Reflection costs little next to Revit itself: about 10 % over calling the property directly.</para>
/// </remarks>
internal sealed class ObjectInspector(Document document, int limit)
{
    // Expensive or noisy: Geometry is read only by path, parameters are listed on their own
    private static readonly HashSet<string> ElementSkipped = ["Geometry", "Parameters", "ParametersMap", "Document"];

    /// <summary>Follows <paramref name="path" /> from <paramref name="element" />: property names, and indexes into collections.</summary>
    /// <exception cref="CommandException">A step of the path does not exist or leads to nothing.</exception>
    public object Resolve(Element element, string? path)
    {
        object current = element;
        var walked = new List<string>();

        foreach (var step in (path ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            walked.Add(step);
            var next = Step(current, step);
            current = next ?? throw new CommandException(CommandErrorCodes.BadRequest, $"'{string.Join('.', walked)}' is null.");
        }

        return current;
    }

    /// <summary>The members of <paramref name="target" />, or the items of a collection.</summary>
    /// <param name="target">What <see cref="Resolve" /> returned.</param>
    /// <param name="path">The path of the target, prefixed to the paths of its nested objects.</param>
    public IReadOnlyList<InspectedMember> Members(object target, string path)
    {
        if (target is IEnumerable items and not string)
        {
            return items.Cast<object?>()
                .Take(limit)
                .Select((item, index) => Describe($"[{index}]", item?.GetType() ?? typeof(object), () => item, Join(path, index.ToString())))
                .ToList();
        }

        var element = target as Element;
        var members = target.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is { IsPublic: true })
            .Where(property => property.GetCustomAttribute<ObsoleteAttribute>() is null)
            .Where(property => element is null || !ElementSkipped.Contains(property.Name))
            .Select(property => Describe(property.Name, property.PropertyType, () => property.GetValue(target), Join(path, property.Name)))
            .ToList();

        if (element is not null)
        {
            // Indexed in the API (get_BoundingBox(view), get_Geometry(options)): no property to find by reflection
            members.Add(Describe("BoundingBox", typeof(BoundingBoxXYZ), () => element.get_BoundingBox(null), Join(path, "BoundingBox")));
            members.Add(new InspectedMember("Geometry", nameof(GeometryElement), null, null, Join(path, "Geometry"), null,
                "Not read: computing it takes time. Read it with this path."));
        }

        return members.OrderBy(member => member.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>How many items a collection has; <see langword="null" /> for anything else.</summary>
    public static int? CountOf(object target)
    {
        return target switch
        {
            string => null,
            ICollection collection => collection.Count,
            IEnumerable items => items.Cast<object?>().Count(),
            _ => null
        };
    }

    /// <summary>The parameters of an element, by name, with their values as the user sees them.</summary>
    public IReadOnlyList<InspectedParameter> Parameters(Element element)
    {
        return element.Parameters
            .Cast<Parameter>()
            .Select(parameter =>
            {
                var reference = parameter.StorageType == StorageType.ElementId ? Reference(parameter.AsElementId()) : null;
                return new InspectedParameter(
                    parameter.Definition.Name,
                    parameter.HasValue ? ParameterValue(parameter) : null,
                    parameter.StorageType.ToString(),
                    parameter.IsReadOnly,
                    reference);
            })
            .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static object? Step(object current, string step)
    {
        if (current is Element element && string.Equals(step, "Geometry", StringComparison.OrdinalIgnoreCase))
        {
            return element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        }

        if (current is Element owner && string.Equals(step, "BoundingBox", StringComparison.OrdinalIgnoreCase))
        {
            return owner.get_BoundingBox(null);
        }

        if (int.TryParse(step, out var index) && current is IEnumerable items and not string)
        {
            return items.Cast<object?>().ElementAtOrDefault(index)
                   ?? throw new CommandException(CommandErrorCodes.BadRequest, $"There is no item {index}.");
        }

        var property = current.GetType().GetProperty(step, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property is null || property.GetIndexParameters().Length > 0)
        {
            throw new CommandException(CommandErrorCodes.BadRequest, $"{current.GetType().Name} has no property '{step}'.");
        }

        try
        {
            return property.GetValue(current);
        }
        catch (TargetInvocationException exception)
        {
            throw new CommandException(CommandErrorCodes.BadRequest, $"'{step}' throws: {exception.InnerException?.Message}");
        }
    }

    private InspectedMember Describe(string name, Type declared, Func<object?> read, string path)
    {
        object? value;
        try
        {
            value = read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var reason = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            return new InspectedMember(name, TypeName(declared), null, null, null, null, $"Throws {reason.GetType().Name}: {reason.Message}");
        }

        var type = TypeName(value?.GetType() ?? declared);
        return value switch
        {
            null => new InspectedMember(name, type, null, null, null, null, null),
            string or bool or char or Guid => new InspectedMember(name, type, value is Guid guid ? guid.ToString() : value, null, null, null, null),
            Enum => new InspectedMember(name, type, value.ToString(), null, null, null, null),
            _ when value.GetType().IsPrimitive || value is decimal => new InspectedMember(name, type, value, null, null, null, null),
            ElementId id when id == ElementId.InvalidElementId => new InspectedMember(name, type, null, null, null, null, "Invalid id: none"),
            ElementId id => new InspectedMember(name, type, null, Reference(id), null, null, null),
            Element element => new InspectedMember(name, type, null, Reference(element), null, null, null),
            WorksetId workset => new InspectedMember(name, type, workset.IntegerValue, null, null, null, null),
            Category category => new InspectedMember(name, type, null, new InspectedReference(category.Id.Value, nameof(Category), category.Name), null, null, null),
            XYZ point => new InspectedMember(name, type, Coordinates(point), null, null, null, null),
            UV uv => new InspectedMember(name, type, new[] { uv.U, uv.V }, null, null, null, null),
            Color color => new InspectedMember(name, type, color.IsValid ? $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}" : null, null, null, null, null),
            Document owner => new InspectedMember(name, type, owner.Title, null, null, null, null),
            BoundingBoxXYZ box => new InspectedMember(name, type, new { min = Coordinates(box.Min), max = Coordinates(box.Max) }, null, path, null, null),
            IEnumerable items => Collection(name, type, items, path),
            _ => new InspectedMember(name, type, null, null, path, null, null)
        };
    }

    // A collection of ids or elements lists its first references; any other one only its count
    private InspectedMember Collection(string name, string type, IEnumerable items, string path)
    {
        var all = items.Cast<object?>().ToList();
        var references = all.Count > 0 && all.All(item => item is ElementId or Element)
            ? all.Take(limit).Select(item => item is Element element ? Reference(element) : Reference((ElementId)item!)).OfType<InspectedReference>().ToList()
            : null;

        return new InspectedMember(name, type, references, null, path, all.Count, null);
    }

    private InspectedReference? Reference(ElementId id)
    {
        if (id == ElementId.InvalidElementId)
        {
            return null;
        }

        // A built-in category has a negative id and is not an element
        return document.GetElement(id) is { } element ? Reference(element)
            : Category.GetCategory(document, id) is { } category ? new InspectedReference(id.Value, nameof(Category), category.Name)
            : new InspectedReference(id.Value, null, null);
    }

    private static InspectedReference Reference(Element element)
    {
        return new InspectedReference(element.Id.Value, element.GetType().Name, element.Name);
    }

    private static object? ParameterValue(Parameter parameter)
    {
        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.ElementId => parameter.AsValueString(),
            _ => (object?)parameter.AsValueString() ?? (parameter.StorageType == StorageType.Integer ? parameter.AsInteger() : parameter.AsDouble())
        };
    }

    private static double[] Coordinates(XYZ point)
    {
        return [point.X, point.Y, point.Z];
    }

    private static string Join(string path, string step)
    {
        return path.Length == 0 ? step : $"{path}.{step}";
    }

    /// <summary>A type as C# writes it, short: <c>ICollection&lt;ElementId&gt;</c>.</summary>
    private static string TypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }
}
