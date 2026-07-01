using AnalyseTool.Sdk;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Common.Extensions
{
    /// <summary>
    /// Isolated load context for one extension. Its private dependencies are loaded from the
    /// extension folder, but the contract assemblies resolve to the HOST's already-loaded copy so
    /// that types crossing the boundary (IRevitTask, Revit API types, JToken) share one identity —
    /// otherwise <c>is IRevitTask</c> would fail and Revit API types would not match.
    ///
    /// For OUR shared assemblies we return the host instance BY SIMPLE NAME (ignoring the version the
    /// extension was compiled against). Returning null instead routes the request to the default
    /// context, which binds by EXACT version — so bumping AnalyseTool.Sdk's AssemblyVersion (even a
    /// backward-compatible minor) would make older extensions fail to bind. Handing back the host copy
    /// makes sharing version-agnostic, so the SDK AssemblyVersion can move within a major freely.
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

        private readonly AssemblyDependencyResolver? _resolver;

        public ExtensionLoadContext(string entryAssemblyPath, string name)
            : base(name, isCollectible: true) // collectible so Reload can unload + replace the DLL
        {
            _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        }

        /// <summary>For in-memory assemblies (e.g. a Roslyn-compiled ad-hoc snippet) that have no file on
        /// disk and no private dependencies — everything they reference is shared with the host.</summary>
        public ExtensionLoadContext(string name)
            : base(name, isCollectible: true)
        {
            _resolver = null;
        }

        /// <summary>Loads the extension's entry assembly without locking the file on disk.</summary>
        public Assembly LoadEntry(string path) => LoadFromBytes(path);

        /// <summary>Loads an assembly straight from its compiled bytes (+ optional PDB), no file involved.</summary>
        public Assembly LoadImage(byte[] assembly, byte[]? pdb)
        {
            using MemoryStream assemblyStream = new(assembly);
            if (pdb is not null)
            {
                using MemoryStream pdbStream = new(pdb);
                return LoadFromStream(assemblyStream, pdbStream);
            }

            return LoadFromStream(assemblyStream);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null && SharedWithHost.Contains(assemblyName.Name))
                // Host copy by simple name (version-agnostic). For Revit API names this is null, which
                // falls through to the default context — Revit provides those, so exact binding is fine.
                return ResolveSharedFromHost(assemblyName.Name);

            string? path = _resolver?.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromBytes(path) : null;
        }

        /// <summary>Returns the host's already-loaded instance of a shared assembly by simple name, so a
        /// crossing type keeps one identity regardless of the version the extension referenced. Revit API
        /// assemblies return null (deferred to the default context, which Revit populates).</summary>
        private static Assembly? ResolveSharedFromHost(string name) => name switch
        {
            "AnalyseTool.Sdk" => typeof(IRevitTask).Assembly,           // the public contract — host's copy
            "AnalyseTool" => typeof(ExtensionLoadContext).Assembly,     // host assembly (this type lives in it)
            "Newtonsoft.Json" => typeof(JToken).Assembly,               // payload JToken crosses the boundary
            _ => null,                                                   // RevitAPI / RevitAPIUI → default context
        };

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
