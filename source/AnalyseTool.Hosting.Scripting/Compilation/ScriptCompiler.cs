using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AnalyseTool.Hosting.Scripting.Compilation;

/// <summary>
///     The outcome of a compilation: the assembly and its PDB, or the errors.
/// </summary>
/// <param name="Assembly">The emitted assembly; <see langword="null" /> when the code does not compile.</param>
/// <param name="Pdb">The symbols, so an exception names the line of the script.</param>
/// <param name="Errors">The compiler errors, with lines of the script as the author wrote it.</param>
internal sealed record ScriptCompilation(byte[]? Assembly, byte[]? Pdb, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Assembly is not null;
}

/// <summary>
///     Compiles a C# method body into an assembly with one static method, <c>Script.Execute(uiapp, cancellationToken)</c>.
/// </summary>
/// <remarks>
///     <para>
///         The body sees <c>uiapp</c>, <c>uidoc</c>, <c>doc</c> and <c>cancellationToken</c> and may <c>return</c> any object;
///         without a <c>return</c> the result is <c>null</c>. <c>using</c> directives on its first lines are lifted above the
///         class — spliced into a method they would be read as using statements and fail with errors that never say why.
///     </para>
///     <para>
///         <c>#line</c> maps the body to <c>script.cs</c> from line 1, so compiler errors and exception stack traces point at the
///         author's own lines.
///     </para>
/// </remarks>
internal sealed partial class ScriptCompiler(IReadOnlyList<MetadataReference> references)
{
    /// <summary>The file name of the body in errors and stack traces.</summary>
    public const string FileName = "script.cs";

    /// <summary>The class and the method the wrapper declares.</summary>
    public const string TypeName = "Script";

    /// <inheritdoc cref="TypeName" />
    public const string MethodName = "Execute";

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private const string Usings =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.UI;
        """;

    public ScriptCompilation Compile(string body)
    {
        var (directives, statements) = LiftUsings(body);

        // The header has no line of the body; #line 1 starts the body at line 1 of script.cs
        var source =
            $$"""
              {{Usings}}
              {{string.Join("\n", directives)}}

              public static class {{TypeName}}
              {
                  public static object {{MethodName}}(UIApplication uiapp, CancellationToken cancellationToken)
                  {
                      var uidoc = uiapp.ActiveUIDocument;
                      var doc = uidoc?.Document;
              #line 1 "{{FileName}}"
              {{statements}}
              #line default
                      return null;
                  }
              }
              """;

        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, FileName, Encoding.UTF8);
        var compilation = CSharpCompilation.Create(
            $"Script_{Guid.NewGuid():N}",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));

        using var assembly = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = compilation.Emit(assembly, pdb);

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())
                .ToList();
            return new ScriptCompilation(null, null, errors);
        }

        return new ScriptCompilation(assembly.ToArray(), pdb.ToArray(), []);
    }

    /// <summary>
    ///     Splits the leading <c>using</c> directives from the body. Blank lines and comments may sit between them; the first
    ///     statement ends them, so a <c>using (var transaction = …)</c> statement further down stays code. Every lifted line
    ///     leaves an empty one behind, so the line numbers of the body do not change.
    /// </summary>
    public static (IReadOnlyList<string> Directives, string Statements) LiftUsings(string body)
    {
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        var directives = new List<string>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (!UsingDirective().IsMatch(line))
            {
                break;
            }

            directives.Add(line);
            lines[index] = string.Empty;
        }

        return (directives, string.Join("\n", lines));
    }

    // using X; using static X.Y; using Alias = X.Y; — a using statement has a parenthesis or a declaration and does not match
    [GeneratedRegex(@"^using\s+(static\s+)?[A-Za-z_][\w.]*(\s*=\s*[A-Za-z_][\w.<>,\s]*)?\s*;\s*(//.*)?$")]
    private static partial Regex UsingDirective();
}
