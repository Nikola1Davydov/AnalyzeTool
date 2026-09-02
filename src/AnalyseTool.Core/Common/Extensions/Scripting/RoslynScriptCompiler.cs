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

            // A body that starts with `using X;` meant a DIRECTIVE, not a using statement — and spliced
            // into a method it becomes the latter, which the compiler reports as twenty errors that never
            // mention `using` (#101). The directives are lifted above the class, and each one leaves an
            // empty line behind so the #line mapping below still points at the author's own lines.
            (IReadOnlyList<string> lifted, string bodyWithoutUsings) = LiftLeadingUsings(body);
            body = bodyWithoutUsings;
            string extraUsings = lifted.Count == 0 ? string.Empty : string.Join("\n", lifted) + "\n";

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
                "using AnalyseTool.Sdk;\n" +
                extraUsings + "\n" +
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

        /// <summary>
        /// Splits the `using` directives an author put at the top of a bare body from the body itself.
        /// Only the LEADING run counts — blank lines and comments may sit between them, but the first
        /// statement ends it; a `using (var t = …)` statement further down is code and stays. Every
        /// lifted line is replaced by an empty one, so line numbers in diagnostics are unchanged.
        /// Public and pure so the Revit-free tests can hold it to that.
        /// </summary>
        public static (IReadOnlyList<string> Usings, string Body) LiftLeadingUsings(string body)
        {
            string[] lines = body.Split('\n');
            List<string> usings = new();
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                if (!UsingDirective.IsMatch(trimmed)) break;
                usings.Add(trimmed);
                lines[i] = string.Empty;
            }
            return (usings, string.Join("\n", lines));
        }

        // `using X;`, `using static X.Y;`, `using Alias = X.Y;` — one per line, as an author writes them.
        // A using STATEMENT has a parenthesis or a declaration after the keyword and does not match.
        private static readonly System.Text.RegularExpressions.Regex UsingDirective = new(
            @"^using\s+(static\s+)?[A-Za-z_][\w.]*(\s*=\s*[A-Za-z_][\w.<>,\s]*)?\s*;\s*(//.*)?$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

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

            // The wrapper's own header names the Sdk, so the Sdk must be referenced whether or not the
            // host has touched it yet — the CLR loads assemblies lazily, and a process that compiles a
            // script before it ever executed one (the in-Revit tests do) had no Sdk in the domain.
            TryAddAssembly(refs, () => typeof(Sdk.IRevitTask).Assembly);
            // Same for RevitAPI, resolved the way the runtime resolves it (a typeof, not a name), because
            // a test host finds it by its own resolver, not by probing. Not RevitAPIUI: inside Revit it is
            // always loaded already, and the UI-less test engine has none — asking for it there crashes
            // the host. Outside Revit the typeof throws and the compiler stays usable for what it can do.
            TryAddAssembly(refs, () => typeof(Autodesk.Revit.DB.Document).Assembly);

            _references = refs.Values.ToList();
            return _references;
        }

        private static void TryAddAssembly(Dictionary<string, MetadataReference> refs, Func<Assembly> load)
        {
            try
            {
                Assembly assembly = load();
                string name = assembly.GetName().Name ?? string.Empty;
                if (refs.ContainsKey(name)) return;
                if (!assembly.IsDynamic && File.Exists(assembly.Location))
                    refs[name] = MetadataReference.CreateFromFile(assembly.Location);
            }
            catch { /* not loadable in this process — the loaded-assembly scan above is the fallback */ }
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
