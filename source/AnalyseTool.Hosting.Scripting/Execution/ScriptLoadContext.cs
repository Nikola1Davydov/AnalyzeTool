using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Hosting.Scripting.Execution;

/// <summary>
///     A collectible load context for one script run: unloaded afterwards, so a thousand runs do not keep a thousand assemblies.
/// </summary>
/// <remarks>
///     A dependency of the script resolves in the add-in's context — the same assembly the add-in uses, not a second copy;
///     the Revit API and the framework come through it from the default context.
/// </remarks>
internal sealed class ScriptLoadContext(AssemblyLoadContext owner) : AssemblyLoadContext($"Script {Guid.NewGuid():N}", isCollectible: true)
{
    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        try
        {
            return owner.LoadFromAssemblyName(assemblyName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
