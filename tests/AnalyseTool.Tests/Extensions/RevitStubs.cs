using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AnalyseTool.Tests.Extensions;

/// <summary>
///     Empty stand-ins for RevitAPI and RevitAPIUI, so the types of the Sdk contract (<c>IRevitContext</c> names
///     <c>UIApplication</c>) load without Revit. Only types are needed: the tests never call into Revit.
/// </summary>
internal static class RevitStubs
{
    private static readonly Lazy<Dictionary<string, Assembly>> Stubs = new(Build);
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name is { } simple && Stubs.Value.TryGetValue(simple, out var stub) ? stub : null;
    }

    private static Dictionary<string, Assembly> Build()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll")
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        // The model tests also name categories, parameters, points and rooms; enum members carry Revit's values, so ToString
        // gives the names the real enums give
        const string api = """
                           namespace Autodesk.Revit.DB
                           {
                               public class Document {}
                               public class Element {}
                               public class SpatialElement : Element {}
                               public class ElementType : Element {}
                               public class XYZ {}
                               public class Outline {}
                               public class Parameter {}
                               public enum BuiltInCategory { INVALID = -1, OST_Walls = -2000011, OST_Doors = -2000023, OST_Windows = -2000014, OST_Rooms = -2000160, OST_StructuralFraming = -2001320 }
                               public enum BuiltInParameter { INVALID = -1, ALL_MODEL_MARK = -1001203, ROOM_NUMBER = -1006901 }
                           }
                           namespace Autodesk.Revit.DB.Architecture
                           {
                               public class Room : Autodesk.Revit.DB.SpatialElement {}
                           }
                           """;
        var apiAssembly = Emit("RevitAPI", api, references, out var apiReference);
        var ui = Emit("RevitAPIUI", "namespace Autodesk.Revit.UI { public class UIApplication {} public class UIDocument {} }", [.. references, apiReference], out _);
        return new Dictionary<string, Assembly> { ["RevitAPI"] = apiAssembly, ["RevitAPIUI"] = ui };
    }

    private static Assembly Emit(string name, string source, IEnumerable<MetadataReference> references, out MetadataReference reference)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithAssemblyName(name);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        reference = MetadataReference.CreateFromImage(stream.ToArray());
        return AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(stream.ToArray()));
    }
}
