using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace AnalyseTool.Hosting.Scripting.Reference;

/// <summary>
///     The XML documentation of an assembly — the <c>RevitAPI.xml</c> next to <c>RevitAPI.dll</c> — by documentation id.
/// </summary>
/// <remarks>Read once, as a stream: the file of the Revit API is about 12 MB; only the summary and the remarks are kept.</remarks>
internal sealed partial class XmlDocumentation
{
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);

    private XmlDocumentation()
    {
    }

    /// <summary>The documentation of the assembly at <paramref name="assemblyPath" />, empty when it has no XML file.</summary>
    public static XmlDocumentation For(string assemblyPath)
    {
        var documentation = new XmlDocumentation();
        var path = Path.ChangeExtension(assemblyPath, ".xml");
        if (File.Exists(path))
        {
            documentation.Read(path);
        }

        return documentation;
    }

    /// <summary>The summary and the remarks of a member, or <see langword="null" />.</summary>
    public string? Find(string documentationId)
    {
        return _texts.GetValueOrDefault(documentationId);
    }

    private void Read(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
        while (reader.ReadToFollowing("member"))
        {
            var name = reader.GetAttribute("name");
            if (name is null)
            {
                continue;
            }

            var text = new StringBuilder();
            using var member = reader.ReadSubtree();
            member.Read();
            while (!member.EOF)
            {
                if (member is { NodeType: XmlNodeType.Element, Name: "summary" or "remarks" })
                {
                    // ReadInnerXml moves on to the next node itself: a Read() after it would skip the remarks
                    var part = Clean(member.ReadInnerXml());
                    if (part.Length > 0)
                    {
                        text.Append(text.Length > 0 ? " " : "").Append(part);
                    }

                    continue;
                }

                member.Read();
            }

            if (text.Length > 0)
            {
                _texts[name] = text.ToString();
            }
        }
    }

    /// <summary>The text of a documentation fragment: references by their short name, no markup, single spaces.</summary>
    private static string Clean(string xml)
    {
        var text = Reference().Replace(xml, match => ShortName(match.Groups["id"].Value));
        text = Keyword().Replace(text, match => match.Groups["word"].Value);
        text = Markup().Replace(text, "");
        return Whitespace().Replace(System.Net.WebUtility.HtmlDecode(text), " ").Trim();
    }

    private static string ShortName(string id)
    {
        // T:Autodesk.Revit.DB.ElementId → ElementId; M:Autodesk.Revit.DB.Document.GetElement(…) → Document.GetElement
        var name = id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
        var parenthesis = name.IndexOf('(');
        if (parenthesis >= 0)
        {
            name = name[..parenthesis];
        }

        var parts = name.Split('.');
        return id.StartsWith("T:", StringComparison.Ordinal) || parts.Length < 2 ? parts[^1] : $"{parts[^2]}.{parts[^1]}";
    }

    /// <summary>
    ///     The documentation id of a member, as the C# compiler writes it into the XML file: <c>M:Ns.Type.Method(Ns.Arg)</c>.
    /// </summary>
    public static string IdOf(MemberInfo member)
    {
        return member switch
        {
            Type type => $"T:{TypeName(type)}",
            FieldInfo field => $"F:{TypeName(field.DeclaringType!)}.{field.Name}",
            PropertyInfo property => $"P:{TypeName(property.DeclaringType!)}.{property.Name}{Parameters(property.GetIndexParameters())}",
            EventInfo @event => $"E:{TypeName(@event.DeclaringType!)}.{@event.Name}",
            ConstructorInfo constructor => $"M:{TypeName(constructor.DeclaringType!)}.#ctor{Parameters(constructor.GetParameters())}",
            MethodInfo method => $"M:{TypeName(method.DeclaringType!)}.{method.Name}"
                                 + (method.IsGenericMethodDefinition ? $"``{method.GetGenericArguments().Length}" : "")
                                 + Parameters(method.GetParameters()),
            _ => member.Name
        };
    }

    private static string Parameters(ParameterInfo[] parameters)
    {
        return parameters.Length == 0 ? "" : $"({string.Join(",", parameters.Select(parameter => ParameterType(parameter.ParameterType)))})";
    }

    /// <summary>A type as it appears in a member id: generic arguments in braces, <c>`0</c> and <c>``0</c> for type parameters.</summary>
    private static string ParameterType(Type type)
    {
        if (type.IsByRef)
        {
            return ParameterType(type.GetElementType()!) + "@";
        }

        if (type.IsArray)
        {
            return ParameterType(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsGenericParameter)
        {
            return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;
        }

        if (type.IsGenericType)
        {
            var definition = TypeName(type.GetGenericTypeDefinition());
            return $"{definition[..definition.LastIndexOf('`')]}{{{string.Join(",", type.GetGenericArguments().Select(ParameterType))}}}";
        }

        return TypeName(type);
    }

    /// <summary>The full name of a type with nested types joined by a dot, as in documentation ids.</summary>
    private static string TypeName(Type type)
    {
        return (type.FullName ?? $"{type.Namespace}.{type.Name}").Replace('+', '.');
    }

    [GeneratedRegex("""<see\s+cref="(?<id>[^"]+)"\s*/>""")]
    private static partial Regex Reference();

    [GeneratedRegex("""<see\s+langword="(?<word>[^"]+)"\s*/>""")]
    private static partial Regex Keyword();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
