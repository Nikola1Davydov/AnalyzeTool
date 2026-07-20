using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System.IO;
using System.Reflection;
using System.Text;

namespace AnalyseTool.Core.Common.Extensions.Scripting
{
    /// <summary>Result of a Roslyn compilation: the emitted assembly bytes (+ PDB) on success, or the
    /// list of compiler error messages on failure.</summary>
    internal sealed record ScriptCompileResult(byte[]? Assembly, byte[]? Pdb, IReadOnlyList<string> Errors)
    {
        public bool Success => Assembly is not null;
    }

    /// <summary>
    /// Compiles user/AI-authored C# into an in-memory assembly that registers exactly like a prebuilt
    /// extension DLL (same <see cref="Sdk.IRevitTask"/> contract). References are the host's own loaded
    /// assemblies, so a script sees the identical Revit API, SDK and Newtonsoft the host runs against.
    ///
    /// Two source shapes are accepted (the "hybrid" format):
    ///   • a full class implementing <c>IRevitTask</c> (compiled as-is — first-class metadata), or
    ///   • a bare pyRevit-style body (auto-wrapped into an IRevitTask running inside RunInRevitAsync,
    ///     with <c>uiapp</c>/<c>uidoc</c>/<c>doc</c> in scope).
    /// </summary>
    internal static class RoslynScriptCompiler
    {
        private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);
        private static IReadOnlyList<MetadataReference>? _references;

        /// <summary>Compiles all of a script extension's source files into one assembly. A single
        /// body-style file is auto-wrapped; otherwise files are compiled as-is (full classes).</summary>
        public static ScriptCompileResult CompileFiles(IReadOnlyList<string> filePaths, string assemblyName)
        {
            List<(string Path, string Text)> sources;
            try
            {
                sources = filePaths.Select(p => (p, File.ReadAllText(p))).ToList();
            }
            catch (Exception ex)
            {
                return new ScriptCompileResult(null, null, new[] { $"Cannot read script files: {ex.Message}" });
            }

            List<SyntaxTree> trees = new();
            if (sources.Count == 1 && !IsFullCommand(sources[0].Text))
                trees.Add(Parse(WrapBody(sources[0].Text, null), "script.cs"));
            else
                trees.AddRange(sources.Select(s => Parse(s.Text, s.Path)));

            return Emit(trees, assemblyName);
        }

        /// <summary>Compiles a single in-memory snippet (used by the AI's ephemeral ExecuteRevitCode and by
        /// SaveAsCommand). Body-style snippets are wrapped with the given command description.</summary>
        public static ScriptCompileResult CompileSnippet(string source, string assemblyName, string? description)
        {
            SyntaxTree tree = IsFullCommand(source)
                ? Parse(source, "script.cs")
                : Parse(WrapBody(source, description), "script.cs");
            return Emit(new[] { tree }, assemblyName);
        }

        /// <summary>Wraps a bare statement body into a complete IRevitTask. The body runs on the Revit
        /// thread inside RunInRevitAsync and may <c>return</c> any object; a trailing <c>return null</c>
        /// makes a body without an explicit return valid.</summary>
        public static string WrapBody(string body, string? description)
        {
            string descLiteral = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                string.IsNullOrWhiteSpace(description) ? "Script command." : description!, quote: true);

            // Built with explicit newlines so the #line directive sits at column 0 and body diagnostics
            // map back to the user's own line numbers (script.cs:1+).
            string header =
                "using System;\n" +
                "using System.Collections;\n" +
                "using System.Collections.Generic;\n" +
                "using System.Linq;\n" +
                "using System.Threading;\n" +
                "using System.Threading.Tasks;\n" +
                "using Autodesk.Revit.DB;\n" +
                "using Autodesk.Revit.UI;\n" +
                "using AnalyseTool.Sdk;\n\n" +
                "[RevitCommand(Description = " + descLiteral + ")]\n" +
                "public sealed class Script : IRevitTask\n" +
                "{\n" +
                "    public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken) =>\n" +
                "        revitContext.RunInRevitAsync<object?>(uiapp =>\n" +
                "        {\n" +
                "            var uidoc = uiapp.ActiveUIDocument;\n" +
                "            var doc = uidoc != null ? uidoc.Document : null;\n";

            string footer =
                "\n            return null;\n" +
                "        });\n" +
                "}\n";

            return header + "#line 1 \"script.cs\"\n" + body + "\n#line default\n" + footer;
        }

        // Encoding is required: emitting a PDB for a source whose tree has no encoding fails (CS8055).
        /// <summary>True when the source already declares an IRevitTask class (so it should be saved/compiled
        /// as-is); false for a bare body that needs wrapping. Public for SaveAsCommand.</summary>
        public static bool LooksLikeFullCommand(string source) => IsFullCommand(source);

        private static SyntaxTree Parse(string text, string path) =>
            CSharpSyntaxTree.ParseText(text, ParseOptions, path, Encoding.UTF8);

        /// <summary>True when the source already declares a type implementing IRevitTask (class form);
        /// false for a bare body that needs wrapping.</summary>
        private static bool IsFullCommand(string source)
        {
            try
            {
                SyntaxNode root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();
                return root.DescendantNodes()
                    .OfType<BaseTypeDeclarationSyntax>()
                    .Any(t => t.BaseList?.Types.Any(bt => bt.Type.ToString().Contains("IRevitTask")) == true);
            }
            catch
            {
                return false; // unparseable → treat as a body, let the real compile surface errors
            }
        }

        private static ScriptCompileResult Emit(IEnumerable<SyntaxTree> trees, string assemblyName)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                trees,
                GetReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Annotations,
                    allowUnsafe: false));

            using MemoryStream assemblyStream = new();
            using MemoryStream pdbStream = new();
            EmitResult result = compilation.Emit(assemblyStream, pdbStream);

            if (!result.Success)
            {
                List<string> errors = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString())
                    .ToList();
                return new ScriptCompileResult(null, null, errors);
            }

            return new ScriptCompileResult(assemblyStream.ToArray(), pdbStream.ToArray(), Array.Empty<string>());
        }

        /// <summary>Metadata references = every non-dynamic assembly currently loaded in the host process,
        /// so scripts compile against the exact Revit API / SDK / Newtonsoft the host runs.</summary>
        private static IReadOnlyList<MetadataReference> GetReferences()
        {
            if (_references is not null) return _references;

            Dictionary<string, MetadataReference> refs = new(StringComparer.OrdinalIgnoreCase);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;

                string location;
                try { location = assembly.Location; }
                catch { continue; }
                if (string.IsNullOrEmpty(location) || !File.Exists(location)) continue;

                string name = assembly.GetName().Name ?? location;
                if (refs.ContainsKey(name)) continue;

                try { refs[name] = MetadataReference.CreateFromFile(location); }
                catch { /* skip references that can't be read */ }
            }

            // These facades aren't always pre-loaded but Roslyn needs them when compiling against the
            // runtime assemblies (rather than ref assemblies).
            TryAddByName(refs, "netstandard");
            TryAddByName(refs, "System.Runtime");

            _references = refs.Values.ToList();
            return _references;
        }

        private static void TryAddByName(Dictionary<string, MetadataReference> refs, string simpleName)
        {
            if (refs.ContainsKey(simpleName)) return;
            try
            {
                Assembly assembly = Assembly.Load(simpleName);
                if (!assembly.IsDynamic && File.Exists(assembly.Location))
                    refs[simpleName] = MetadataReference.CreateFromFile(assembly.Location);
            }
            catch { /* facade not available — most scripts compile without it */ }
        }
    }
}
