using System.Collections;
using System.Reflection;

namespace AnalyseTool.Tests;

/// <summary>
/// Builds a plausible instance of any result type by reflection, so the schema contract can be checked
/// against what the host would actually serialize — without a Revit session to produce the real thing.
///
/// Two shapes matter, and both are generated: the SPARSE one (every nullable member null, every
/// collection empty) is the #98 case — properties the serializer OMITS must not be required; the DENSE
/// one (every collection holding one element, every nullable filled) is what exercises the item schemas
/// of nested lists and dictionaries.
/// </summary>
internal static class SampleFactory
{
    // One context per Build call, never shared: NullabilityInfoContext caches per member and is not
    // thread-safe, and TUnit runs the contract test in parallel over every command.
    [ThreadStatic] private static NullabilityInfoContext? _nullability;
    private static NullabilityInfoContext Nullability => _nullability ??= new NullabilityInfoContext();

    public static object? Build(Type type, bool dense, int depth = 0)
    {
        if (depth > 8) return null; // recursive DTOs are not a thing here; a guard, not a feature

        Type? underlying = System.Nullable.GetUnderlyingType(type);
        if (underlying is not null) return dense ? Build(underlying, dense, depth + 1) : null;

        if (type == typeof(string)) return "sample";
        if (type == typeof(bool)) return dense;
        if (type.IsPrimitive || type == typeof(decimal)) return Activator.CreateInstance(type);
        if (type.IsEnum) return Enum.GetValues(type).Cast<object>().First();
        if (type == typeof(DateTime)) return new DateTime(2026, 9, 2);
        if (type == typeof(Guid)) return Guid.Empty;
        if (type == typeof(object)) return new Dictionary<string, object>(); // "any": an empty object is valid everywhere

        if (TryBuildDictionary(type, dense, depth, out object? dictionary)) return dictionary;
        if (TryBuildCollection(type, dense, depth, out object? collection)) return collection;

        return BuildObject(type, dense, depth);
    }

    private static bool TryBuildDictionary(Type type, bool dense, int depth, out object? result)
    {
        result = null;
        Type? iface = FindGenericInterface(type, typeof(IDictionary<,>)) ?? FindGenericInterface(type, typeof(IReadOnlyDictionary<,>));
        if (iface is null) return false;
        Type[] args = iface.GetGenericArguments();
        if (args[0] != typeof(string)) return false;

        IDictionary dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args))!;
        if (dense) dict["key"] = Build(args[1], dense, depth + 1);
        result = dict;
        return true;
    }

    private static bool TryBuildCollection(Type type, bool dense, int depth, out object? result)
    {
        result = null;
        if (type == typeof(string)) return false;
        Type? elementType = type.IsArray ? type.GetElementType()
            : FindGenericInterface(type, typeof(IEnumerable<>))?.GetGenericArguments()[0];
        if (elementType is null) return false;

        Array array = Array.CreateInstance(elementType, dense ? 1 : 0);
        if (dense) array.SetValue(Build(elementType, dense, depth + 1), 0);
        // An array satisfies T[], IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>, IList<T>;
        // a concrete List<T> is asked for by name only.
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            IList list = (IList)Activator.CreateInstance(type)!;
            foreach (object? item in array) list.Add(item);
            result = list;
            return true;
        }
        result = array;
        return true;
    }

    private static object? BuildObject(Type type, bool dense, int depth)
    {
        if (type.IsAbstract || type.IsInterface) return null;

        // The public constructor with the most parameters is the positional-record one, when there is
        // one. A record also carries a protected COPY constructor (one parameter of its own type) —
        // never that: it dereferences its argument.
        ConstructorInfo? ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(c => !(c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == type))
            .OrderByDescending(c => c.IsPublic)
            .ThenByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null) return null;

        object?[] args = ctor.GetParameters()
            .Select(p => IsNullable(p) && !dense ? null : Build(p.ParameterType, dense, depth + 1))
            .ToArray();
        object instance = ctor.Invoke(args);

        // Properties the constructor did not cover: init/settable ones on records with property syntax.
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0) continue;
            if (ctor.GetParameters().Any(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase))) continue;
            object? value = IsNullable(property) && !dense ? null : Build(property.PropertyType, dense, depth + 1);
            property.SetValue(instance, value);
        }
        return instance;
    }

    private static bool IsNullable(ParameterInfo parameter)
    {
        if (System.Nullable.GetUnderlyingType(parameter.ParameterType) is not null) return true;
        return Nullability.Create(parameter).WriteState == NullabilityState.Nullable;
    }

    private static bool IsNullable(PropertyInfo property)
    {
        if (System.Nullable.GetUnderlyingType(property.PropertyType) is not null) return true;
        return Nullability.Create(property).WriteState == NullabilityState.Nullable;
    }

    private static Type? FindGenericInterface(Type type, Type definition)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == definition) return type;
        return type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == definition);
    }
}
