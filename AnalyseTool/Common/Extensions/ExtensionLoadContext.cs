using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Common.Extensions
{
    /// <summary>
    /// Isolated load context for one extension. Its private dependencies are loaded from the
    /// extension folder, but the contract assemblies are deferred to the host's default context so
    /// that types crossing the boundary (IRevitTask, Revit API types, JToken) share one identity —
    /// otherwise <c>is IRevitTask</c> would fail and Revit API types would not match.
    ///
    /// Assemblies are loaded from a byte copy (LoadFromStream), NOT LoadFromAssemblyPath, so the
    /// files on disk are never locked. That lets the author rebuild/overwrite an extension while
    /// Revit is running; Reload then re-reads the new bytes into a fresh context.
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
            : base(name, isCollectible: true) // collectible so Reload can unload + replace the DLL
        {
            _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        }

        /// <summary>Loads the extension's entry assembly without locking the file on disk.</summary>
        public Assembly LoadEntry(string path) => LoadFromBytes(path);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // null => fall through to the default (host) context, preserving type identity.
            if (assemblyName.Name is not null && SharedWithHost.Contains(assemblyName.Name))
                return null;

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromBytes(path) : null;
        }

        /// <summary>Reads the assembly (and its PDB, if present) into memory and loads from there, so
        /// the on-disk files stay unlocked.</summary>
        private Assembly LoadFromBytes(string path)
        {
            byte[] assemblyBytes = File.ReadAllBytes(path);

            string pdbPath = Path.ChangeExtension(path, ".pdb");
            if (File.Exists(pdbPath))
            {
                using MemoryStream assemblyStream = new(assemblyBytes);
                using MemoryStream pdbStream = new(File.ReadAllBytes(pdbPath));
                return LoadFromStream(assemblyStream, pdbStream);
            }

            using MemoryStream stream = new(assemblyBytes);
            return LoadFromStream(stream);
        }
    }
}
