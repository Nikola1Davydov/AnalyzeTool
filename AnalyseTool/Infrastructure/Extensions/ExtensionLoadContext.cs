using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Infrastructure.Extensions
{
    /// <summary>
    /// Isolated load context for one extension. Its private dependencies are loaded from the
    /// extension folder, but the contract assemblies are deferred to the host's default context so
    /// that types crossing the boundary (IRevitTask, Revit API types, JToken) share one identity —
    /// otherwise <c>is IRevitTask</c> would fail and Revit API types would not match.
    /// </summary>
    internal sealed class ExtensionLoadContext : AssemblyLoadContext
    {
        private static readonly HashSet<string> SharedWithHost = new(StringComparer.OrdinalIgnoreCase)
        {
            "AnalyseTool.Sdk",   // the public contract — MUST be the host's copy
            "AnalyseTool",       // host assembly (extensions shouldn't reference it, but be safe)
            "RevitAPI",
            "RevitAPIUI",
            "Newtonsoft.Json",   // payload JToken crosses the boundary
        };

        private readonly AssemblyDependencyResolver _resolver;

        public ExtensionLoadContext(string entryAssemblyPath, string name)
            : base(name, isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // null => fall through to the default (host) context, preserving type identity.
            if (assemblyName.Name is not null && SharedWithHost.Contains(assemblyName.Name))
                return null;

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }
    }
}
