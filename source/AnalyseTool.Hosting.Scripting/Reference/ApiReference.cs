using System.Collections.Concurrent;
using System.Reflection;

namespace AnalyseTool.Hosting.Scripting.Reference;

/// <summary>
///     The answer of <see cref="DescribeApi" />: a type with its members, or the types whose names match.
/// </summary>
/// <param name="Type">The full name of the type; <see langword="null" /> when <paramref name="Matches" /> is the answer.</param>
/// <param name="Kind">class, struct, interface or enum.</param>
/// <param name="BaseType">The full name of the base type; its members are described by its own call.</param>
/// <param name="Documentation">The summary and remarks of the type.</param>
/// <param name="Members">The public members declared by the type itself.</param>
/// <param name="Matches">The full names of the types that match the name, when there is no single match.</param>
public sealed record ApiDescription(
    string? Type,
    string? Kind,
    string? BaseType,
    string? Documentation,
    IReadOnlyList<ApiMember>? Members,
    IReadOnlyList<string>? Matches);

/// <summary>
///     A member of an API type.
/// </summary>
/// <param name="Signature">The member as C# declares it: <c>Element GetElement(ElementId id)</c>.</param>
/// <param name="Documentation">The summary and remarks.</param>
/// <param name="Obsolete">The message of <see cref="ObsoleteAttribute" />, when the member is obsolete in this version.</param>
public sealed record ApiMember(string Signature, string? Documentation, string? Obsolete);

/// <summary>
///     Describes the public types of a set of assemblies — the Revit API of the running Revit version — by reflection, with
///     the text of their XML documentation.
/// </summary>
/// <remarks>Reading metadata needs no Revit thread. The documentation of an assembly is read at its first use.</remarks>
internal sealed class ApiReference(IReadOnlyList<Assembly> assemblies)
{
    /// <summary>The most matching type names returned.</summary>
    public const int MatchLimit = 30;

    /// <summary>The most members returned.</summary>
    public const int MemberLimit = 300;

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private readonly Lazy<IReadOnlyList<Type>> _types = new(() => assemblies.SelectMany(PublicTypes).ToList());
    private readonly ConcurrentDictionary<Assembly, XmlDocumentation> _documentation = new();

    public ApiDescription Describe(string name, string? member)
    {
        var types = _types.Value;
        var candidates = types.Where(type => string.Equals(type.FullName, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0)
        {
            candidates = types.Where(type => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (candidates.Count == 1)
        {
            return Describe(candidates[0], member);
        }

        var matches = candidates.Count > 1
            ? candidates
            : types.Where(type => type.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).OrderBy(type => type.Name.Length).ToList();

        return new ApiDescription(null, null, null, null, null, matches.Take(MatchLimit).Select(type => type.FullName!).ToList());
    }

    private ApiDescription Describe(Type type, string? filter)
    {
        var documentation = _documentation.GetOrAdd(type.Assembly, assembly => XmlDocumentation.For(assembly.Location));

        var members = type.GetMembers(Declared)
            .Where(member => member is not MethodInfo { IsSpecialName: true } && member.MemberType != MemberTypes.NestedType)
            .Where(member => !(type.IsEnum && member.Name == "value__"))
            .Where(member => filter is null || member.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(member => member.MemberType)
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .Take(MemberLimit)
            .Select(member => new ApiMember(
                Signature(member),
                documentation.Find(XmlDocumentation.IdOf(member)),
                member.GetCustomAttribute<ObsoleteAttribute>() is { } obsolete ? obsolete.Message ?? "obsolete" : null))
            .ToList();

        return new ApiDescription(
            type.FullName,
            Kind(type),
            type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(Enum) && baseType != typeof(ValueType) ? baseType.FullName : null,
            documentation.Find(XmlDocumentation.IdOf(type)),
            members,
            null);
    }

    private static string Kind(Type type)
    {
        return type switch
        {
            { IsEnum: true } => "enum",
            { IsInterface: true } => "interface",
            { IsValueType: true } => "struct",
            _ => "class"
        };
    }

    private static string Signature(MemberInfo member)
    {
        return member switch
        {
            ConstructorInfo constructor => $"{TypeName(constructor.DeclaringType!)}({Parameters(constructor.GetParameters())})",
            MethodInfo method => $"{Static(method.IsStatic)}{TypeName(method.ReturnType)} {method.Name}{GenericArguments(method)}({Parameters(method.GetParameters())})",
            PropertyInfo property => $"{Static(property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic ?? false)}{TypeName(property.PropertyType)} {property.Name} {{ {Accessors(property)}}}",
            FieldInfo { DeclaringType.IsEnum: true } field => field.Name,
            FieldInfo field => $"{Static(field.IsStatic)}{(field.IsLiteral ? "const " : "")}{TypeName(field.FieldType)} {field.Name}",
            EventInfo @event => $"event {TypeName(@event.EventHandlerType!)} {@event.Name}",
            _ => member.Name
        };
    }

    private static string Static(bool isStatic)
    {
        return isStatic ? "static " : "";
    }

    private static string Accessors(PropertyInfo property)
    {
        return (property.GetMethod?.IsPublic == true ? "get; " : "") + (property.SetMethod?.IsPublic == true ? "set; " : "");
    }

    private static string GenericArguments(MethodInfo method)
    {
        return method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(TypeName))}>" : "";
    }

    private static string Parameters(ParameterInfo[] parameters)
    {
        return string.Join(", ", parameters.Select(parameter =>
            (parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : "") + $"{TypeName(parameter.ParameterType)} {parameter.Name}"));
    }

    /// <summary>A type as C# writes it: short names, generic arguments in angle brackets.</summary>
    private static string TypeName(Type type)
    {
        if (type.IsByRef)
        {
            return TypeName(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!) + "[]";
        }

        if (type == typeof(void))
        {
            return "void";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }

    private static IEnumerable<Type> PublicTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>().Where(type => type.IsPublic || type.IsNestedPublic);
        }
    }
}
