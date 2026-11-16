using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace AnalyseTool.Hosting.Scripting.Compilation;

/// <summary>
///     The assemblies a script compiles against: the same ones the add-in runs with.
/// </summary>
/// <remarks>
///     By simple name, the first source wins:
///     <list type="number">
///         <item>the assemblies loaded in the add-in's own load context — its versions of Microsoft.Extensions.* and the rest;</item>
///         <item>the assemblies loaded in the default context — the Revit API;</item>
///         <item>the framework (<c>TRUSTED_PLATFORM_ASSEMBLIES</c>), for what nobody has loaded yet;</item>
///         <item>extra files, for tests that have no Revit API loaded.</item>
///     </list>
///     Another add-in's load context is never used: its versions are not the ones the script runs with.
/// </remarks>
internal static class ScriptReferences
{
    public static IReadOnlyList<MetadataReference> Collect(AssemblyLoadContext owner, IEnumerable<string>? extraFiles = null)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in owner.Assemblies.Concat(AssemblyLoadContext.Default.Assemblies))
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
            {
                continue;
            }

            paths.TryAdd(assembly.GetName().Name!, assembly.Location);
        }

        var platform = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?.Split(Path.PathSeparator) ?? [];
        foreach (var path in platform.Concat(extraFiles ?? []))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && HasMetadata(path))
            {
                paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }
        }

        return paths.Values.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
    }

    /// <summary>A native DLL next to managed ones (WebView2Loader) breaks the compilation as a reference.</summary>
    private static bool HasMetadata(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.HasMetadata;
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
