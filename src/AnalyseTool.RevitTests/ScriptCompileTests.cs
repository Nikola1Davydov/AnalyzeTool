using AnalyseTool.Core.Common.Extensions.Scripting;
using Nice3point.TUnit.Revit;

namespace AnalyseTool.RevitTests;

/// <summary>
/// The bare-body script compiler against a LIVE RevitAPI — the reference the Revit-free tier cannot
/// supply, because the wrapper's own header names Autodesk.Revit.DB. #101 was found here: a body that
/// opened with `using Autodesk.Revit.DB;` failed with twenty errors, none of which said that the
/// directive had become a using statement inside a method.
///
/// One thing this host does not have is RevitAPIUI (a UI-less Revit engine runs the tests), and the
/// wrapper's ExecuteAsync takes a UIApplication — so the one error every compile here reports is
/// "UIApplication is defined in an assembly that is not referenced". The assertions therefore say:
/// nothing but that. A directive left in the body would add its own errors (CS1001, CS0118, CS0210,
/// and CS0246 for the type the directive was meant to bring in).
/// </summary>
public sealed class ScriptCompileTests : RevitApiTest
{
    private static bool IsOnlyTheMissingUi(string error) => error.Contains("RevitAPIUI", StringComparison.Ordinal);

    [Test]
    public async Task A_body_that_starts_with_using_directives_compiles()
    {
        const string body = "using System.Text;\nusing static System.Math;\nvar sb = new StringBuilder();\nsb.Append(Round(2.5));\nreturn sb.ToString() + (doc?.Title ?? \"\");";

        ScriptCompileResult result = RoslynScriptCompiler.CompileSnippet(body, "test_101", "probe");

        await Assert.That(result.Errors.Where(e => !IsOnlyTheMissingUi(e))).IsEmpty();
    }

    [Test]
    public async Task A_body_without_directives_compiles_the_same_way()
    {
        ScriptCompileResult result = RoslynScriptCompiler.CompileSnippet("return doc?.Title;", "test_plain", null);

        await Assert.That(result.Errors.Where(e => !IsOnlyTheMissingUi(e))).IsEmpty();
    }
}
