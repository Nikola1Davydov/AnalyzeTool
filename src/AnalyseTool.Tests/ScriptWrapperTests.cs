using AnalyseTool.Core.Common.Extensions.Scripting;

namespace AnalyseTool.Tests;

/// <summary>
/// The bare-body form of ExecuteRevitCode / SaveAsCommand: a snippet spliced into a method. #101 —
/// an author's `using Autodesk.Revit.DB;` on line 1 became a using STATEMENT there and produced
/// twenty errors that never said why. Now leading directives are lifted above the class.
/// </summary>
public class ScriptWrapperTests
{
    [Test]
    public async Task Leading_using_directives_are_lifted_and_leave_their_lines_blank()
    {
        const string body = "using System.Text;\nusing static System.Math;\n\n// a comment between\nusing Autodesk.Revit.DB.Architecture;\nvar sb = new StringBuilder();\nreturn sb.Length;";

        (IReadOnlyList<string> usings, string rest) = RoslynScriptCompiler.LiftLeadingUsings(body);

        using (Assert.Multiple())
        {
            await Assert.That(usings).IsEquivalentTo(new[] { "using System.Text;", "using static System.Math;", "using Autodesk.Revit.DB.Architecture;" });
            // Same number of lines, so `#line 1 "script.cs"` still maps diagnostics onto the author's numbering.
            await Assert.That(rest.Split('\n').Length).IsEqualTo(body.Split('\n').Length);
            await Assert.That(rest.Split('\n')[5]).IsEqualTo("var sb = new StringBuilder();");
            await Assert.That(rest.Split('\n')[0]).IsEqualTo(string.Empty);
        }
    }

    [Test]
    public async Task A_using_statement_in_the_code_is_not_a_directive()
    {
        const string body = "using (var t = new Transaction(doc, \"x\"))\n{\n    t.Start();\n}\nreturn null;";

        (IReadOnlyList<string> usings, string rest) = RoslynScriptCompiler.LiftLeadingUsings(body);

        await Assert.That(usings).IsEmpty();
        await Assert.That(rest).IsEqualTo(body);
    }

    [Test]
    public async Task A_directive_after_the_first_statement_is_left_alone()
    {
        // Not leading: the compiler will complain, and rightly — a directive in the middle of a method
        // is not something a lift should silently repair.
        const string body = "var x = 1;\nusing System.Text;\nreturn x;";

        (IReadOnlyList<string> usings, string rest) = RoslynScriptCompiler.LiftLeadingUsings(body);

        await Assert.That(usings).IsEmpty();
        await Assert.That(rest).IsEqualTo(body);
    }

    [Test]
    public async Task Wrapped_source_places_the_lifted_directives_above_the_class_and_keeps_line_mapping()
    {
        string wrapped = RoslynScriptCompiler.WrapBody("using System.Text;\nreturn new StringBuilder().Length;", "probe");

        string[] lines = wrapped.Split('\n');
        int classLine = Array.FindIndex(lines, l => l.StartsWith("public sealed class Script", StringComparison.Ordinal));
        int liftedLine = Array.IndexOf(lines, "using System.Text;");
        int lineDirective = Array.FindIndex(lines, l => l.StartsWith("#line 1", StringComparison.Ordinal));

        using (Assert.Multiple())
        {
            await Assert.That(liftedLine).IsGreaterThan(-1);
            await Assert.That(liftedLine).IsLessThan(classLine);
            // Line 1 of the author's body is the (now blank) line where the directive stood — line 2 is the code.
            await Assert.That(lines[lineDirective + 1]).IsEqualTo(string.Empty);
            await Assert.That(lines[lineDirective + 2]).IsEqualTo("return new StringBuilder().Length;");
        }
    }

    [Test]
    public async Task A_body_without_directives_is_wrapped_unchanged()
    {
        string wrapped = RoslynScriptCompiler.WrapBody("return doc.Title;", null);
        await Assert.That(wrapped).Contains("#line 1 \"script.cs\"\nreturn doc.Title;\n#line default");
    }
}
