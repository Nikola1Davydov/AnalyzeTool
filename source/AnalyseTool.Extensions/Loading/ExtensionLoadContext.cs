using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using AnalyseTool.Sdk;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Extensions.Loading;

/// <summary>
///     The load context of one extension: collectible, so a reload replaces it; its private dependencies come from the
///     extension folder, the contract from AnalyseTool.
/// </summary>
/// <remarks>
///     <para>
///         <c>AnalyseTool.Sdk</c> and <c>Newtonsoft.Json</c> resolve by simple name to AnalyseTool's own copies, whatever version
///         the extension was built against: <c>IRevitTask</c> and <c>JToken</c> must have one identity on both sides, and an
///         extension built against an older minor of the Sdk must still bind. The Revit API comes from Revit.
///     </para>
///     <para>Assemblies load from bytes, so the files are never locked: the author rebuilds while Revit runs.</para>
/// </remarks>
internal sealed class ExtensionLoadContext(string name, string? entryAssemblyPath) : AssemblyLoadContext(name, isCollectible: true)
{
    private readonly AssemblyDependencyResolver? _resolver = entryAssemblyPath is null ? null : new AssemblyDependencyResolver(entryAssemblyPath);
    private readonly string? _directory = entryAssemblyPath is null ? null : Path.GetDirectoryName(entryAssemblyPath);

    /// <summary>Gets further folders to look in for dependencies: the extension's <c>lib\</c>.</summary>
    public List<string> ProbingFolders { get; } = [];

    /// <summary>Loads an assembly from its file without locking it; the PDB next to it comes along.</summary>
    public Assembly LoadFromFileBytes(string path)
    {
        using var assembly = new MemoryStream(File.ReadAllBytes(path));
        var pdbPath = Path.ChangeExtension(path, ".pdb");
        if (!File.Exists(pdbPath))
        {
            return LoadFromStream(assembly);
        }

        using var pdb = new MemoryStream(File.ReadAllBytes(pdbPath));
        return LoadFromStream(assembly, pdb);
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        switch (assemblyName.Name)
        {
            case "AnalyseTool.Sdk":
                return typeof(IRevitTask).Assembly;
            case "Newtonsoft.Json":
                return typeof(JToken).Assembly;
            case "RevitAPI" or "RevitAPIUI" or null:
                return null;
        }

        var path = _resolver?.ResolveAssemblyToPath(assemblyName);
        if (path is null)
        {
            path = new[] { _directory }.Concat(ProbingFolders)
                .OfType<string>()
                .Select(folder => Path.Combine(folder, assemblyName.Name + ".dll"))
                .FirstOrDefault(File.Exists);
        }

        // Unknown here: the default context (the framework) or AnalyseTool's context through the fallback
        return path is not null ? LoadFromFileBytes(path) : null;
    }
}
