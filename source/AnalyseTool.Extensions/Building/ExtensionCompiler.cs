using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace AnalyseTool.Extensions.Building;

/// <summary>A compiler error or warning, with where it is.</summary>
/// <param name="File">The file, relative to the extension folder.</param>
/// <param name="Line">The line, from 1.</param>
/// <param name="Column">The column, from 1.</param>
/// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
/// <param name="Code">The compiler code, e.g. CS0103.</param>
/// <param name="Message">The message.</param>
public sealed record CompileDiagnostic(string File, int Line, int Column, string Severity, string Code, string Message)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"{File}({Line},{Column}): {Severity} {Code}: {Message}";
    }
}

/// <summary>What a compilation produced.</summary>
/// <param name="Assembly">The image; <see langword="null" /> when the sources have errors.</param>
/// <param name="Pdb">The portable PDB with the sources embedded, for debugging.</param>
/// <param name="Diagnostics">Errors and warnings.</param>
public sealed record CompileResult(byte[]? Assembly, byte[]? Pdb, IReadOnlyList<CompileDiagnostic> Diagnostics)
{
    /// <summary>Gets a value indicating whether the sources compiled.</summary>
    public bool Success => Assembly is not null;

    /// <summary>Gets the errors only.</summary>
    public IEnumerable<CompileDiagnostic> Errors => Diagnostics.Where(diagnostic => diagnostic.Severity == "error");
}

/// <summary>
///     Compiles the C# sources of an extension with Roslyn, against the assemblies AnalyseTool runs with: the Revit API of the
///     running Revit, <c>AnalyseTool.Sdk</c>, Newtonsoft.Json, the framework — and the DLLs in the extension's <c>lib\</c>.
/// </summary>
/// <remarks>The PDB embeds the sources, so a debugger attached to Revit stops in them.</remarks>
public static class ExtensionCompiler
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);
    private static readonly Lazy<IReadOnlyList<MetadataReference>> HostReferences = new(CollectHostReferences);

    private const string ImplicitUsings = """
                                          global using System;
                                          global using System.Collections.Generic;
                                          global using System.IO;
                                          global using System.Linq;
                                          global using System.Net.Http;
                                          global using System.Threading;
                                          global using System.Threading.Tasks;
                                          """;

    /// <summary>Extra reference files for tests, where the Revit API is not loaded.</summary>
    internal static IReadOnlyList<string> ExtraReferenceFiles { get; set; } = [];

    /// <summary>Compiles the sources into one assembly.</summary>
    /// <param name="sources">Path relative to the extension folder, and text.</param>
    /// <param name="assemblyName">The assembly name, without <c>.dll</c>.</param>
    /// <param name="libraries">Further DLLs to reference: the extension's <c>lib\</c>.</param>
    public static CompileResult Compile(IReadOnlyList<(string Path, string Text)> sources, string assemblyName, IEnumerable<string>? libraries = null)
    {
        if (sources.Count == 0)
        {
            return new CompileResult(null, null, [new CompileDiagnostic(".", 1, 1, "error", "AT0001", "There is no C# source to compile.")]);
        }

        var trees = sources.Select(source => Parse(source.Text, source.Path)).ToList();
        // The implicit usings of an SDK-style project, so code written in an editor with the .editor project compiles here too
        trees.Add(Parse(ImplicitUsings, "ImplicitUsings.g.cs"));

        var references = HostReferences.Value.ToList();
        foreach (var library in libraries ?? [])
        {
            if (HasMetadata(library))
            {
                references.Add(MetadataReference.CreateFromFile(library));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                nullableContextOptions: NullableContextOptions.Enable,
                platform: Platform.X64));

        using var assembly = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = compilation.Emit(
            assembly,
            pdb,
            embeddedTexts: trees.Select(tree => EmbeddedText.FromSource(tree.FilePath, tree.GetText())),
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

        var diagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning && diagnostic.Id != "CS1701" && diagnostic.Id != "CS1702")
            .Select(ToDiagnostic)
            .OrderBy(diagnostic => diagnostic.Severity == "error" ? 0 : 1)
            .ThenBy(diagnostic => diagnostic.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.Line)
            .ToList();

        return result.Success
            ? new CompileResult(assembly.ToArray(), pdb.ToArray(), diagnostics)
            : new CompileResult(null, null, diagnostics);
    }

    private static SyntaxTree Parse(string text, string path)
    {
        return CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), ParseOptions, path);
    }

    private static CompileDiagnostic ToDiagnostic(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetMappedLineSpan();
        var file = span.HasMappedPath || !string.IsNullOrEmpty(span.Path) ? span.Path : "";
        return new CompileDiagnostic(
            file,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning",
            diagnostic.Id,
            diagnostic.GetMessage());
    }

    /// <summary>
    ///     The assemblies loaded where AnalyseTool runs — its own load context first (its versions of Newtonsoft and the Sdk),
    ///     then the default one (the Revit API) — and the framework for what nobody loaded yet.
    /// </summary>
    private static IReadOnlyList<MetadataReference> CollectHostReferences()
    {
        var owner = AssemblyLoadContext.GetLoadContext(typeof(ExtensionCompiler).Assembly) ?? AssemblyLoadContext.Default;
        // Load what extensions reference by name before collecting, so they are there even if nothing touched them yet
        _ = typeof(Sdk.IRevitTask).Assembly;
        _ = typeof(Newtonsoft.Json.JsonConvert).Assembly;

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
        foreach (var path in platform.Concat(ExtraReferenceFiles))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && HasMetadata(path))
            {
                paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }
        }

        return paths.Values.Where(HasMetadata).Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
    }

    /// <summary>A native DLL next to managed ones breaks a compilation as a reference.</summary>
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
