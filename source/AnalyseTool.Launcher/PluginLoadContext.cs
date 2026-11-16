using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Launcher;

/// <summary>
///     The isolated context of the add-in.
/// </summary>
/// <remarks>
///     Assemblies listed in the Launcher's <c>deps.json</c> — the add-in, Microsoft.Extensions.*, Nice3point libraries,
///     WebView2, ... — load here, so other add-ins cannot clash with their versions. Everything else — the Revit API and
///     the shared framework — falls back to Revit's context, which keeps type identity for <c>UIApplication</c> and friends.
/// </remarks>
/// <param name="name">The context name, shown in diagnostics.</param>
/// <param name="componentPath">The assembly whose <c>deps.json</c> describes the dependencies: the Launcher itself.</param>
internal sealed class PluginLoadContext(string name, string componentPath) : AssemblyLoadContext(name)
{
    private readonly AssemblyDependencyResolver _resolver = new(componentPath);

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    /// <inheritdoc />
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null ? nint.Zero : LoadUnmanagedDllFromPath(libraryPath);
    }
}
