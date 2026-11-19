using System.IO;
using AnalyseTool.Extensions.Authoring;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Tests.Extensions;

namespace AnalyseTool.Tests.ModelTests;

/// <summary>Suites written as C# in an extension folder: compiled, declared and reloaded with the extension.</summary>
[NotInParallel("extensions")]
public sealed class ModelTestLoadingTests
{
    [Test]
    public async Task CreateTestSuite_CompilesAndDeclaresTheExamples()
    {
        using var environment = new ExtensionTestEnvironment();

        var state = await environment.Manager.CreateTestSuiteAsync("BEP Project X", null, null, null, CancellationToken.None);

        var suite = environment.Tests.GetSuites().Single();
        using (Assert.Multiple())
        {
            await Assert.That(state.Errors).IsEmpty();
            await Assert.That(state.Diagnostics.Where(diagnostic => diagnostic.Severity == "error")).IsEmpty();
            await Assert.That(suite.Id).IsEqualTo("bep-project-x.BEPProjectX");
            await Assert.That(suite.Name).IsEqualTo("BEP Project X");
            await Assert.That(suite.Tests.Select(test => test.Kind)).IsEquivalentTo(["Rule", "Rule", "Model"]);
            await Assert.That(suite.Tests[0].Name).IsEqualTo("Doors · ALL_MODEL_MARK · not empty");
            await Assert.That(suite.Tests[0].File).IsEqualTo(Path.Combine("src", "BEPProjectX.cs"));
            await Assert.That(environment.History.GetHistory(state.Descriptor.Directory)).HasSingleItem();
        }
    }

    [Test]
    public async Task SavedSuite_ReloadsItsTests_AndADefineThatThrowsIsAnError()
    {
        using var environment = new ExtensionTestEnvironment();
        var state = await environment.Manager.CreateTestSuiteAsync("Checks", null, null, null, CancellationToken.None);

        await environment.Manager.WriteFileAsync(state.Descriptor.Id, "src/Checks.cs", """
            using AnalyseTool.Sdk.Testing;
            using Autodesk.Revit.DB;

            public sealed class Checks : IModelTestSuite
            {
                public void Define(ISuite s)
                {
                    s.Elements(BuiltInCategory.OST_Walls).Parameter("Fire Rating").OneOf("EI30", "EI60").Id("walls-fire");
                }
            }

            public sealed class Broken : IModelTestSuite
            {
                public void Define(ISuite s) => throw new InvalidOperationException("not yet");
            }
            """, CancellationToken.None);

        var after = environment.Host.GetState(state.Descriptor.Id)!;
        var suites = environment.Tests.GetSuites();
        using (Assert.Multiple())
        {
            await Assert.That(suites).HasSingleItem();
            await Assert.That(suites[0].Tests.Single().Id).IsEqualTo("checks.Checks/walls-fire");
            await Assert.That(after.Errors.Any(error => error.Contains("Broken.Define threw InvalidOperationException: not yet"))).IsTrue();
        }
    }

    [Test]
    public async Task AddTestSuite_PutsASecondSuiteIntoTheSameFolder()
    {
        using var environment = new ExtensionTestEnvironment();
        var state = await environment.Manager.CreateTestSuiteAsync("Project A", null, null, null, CancellationToken.None);

        await environment.Manager.AddTestSuiteAsync(state.Descriptor.Id, "Sheets", CancellationToken.None);
        var again = async () => await environment.Manager.AddTestSuiteAsync(state.Descriptor.Id, "Sheets", CancellationToken.None);

        var suites = environment.Tests.GetSuites();
        using (Assert.Multiple())
        {
            await Assert.That(suites.Select(suite => suite.Name)).IsEquivalentTo(["Project A", "Sheets"]);
            await Assert.That(suites.Select(suite => suite.ExtensionName).Distinct()).IsEquivalentTo(["Project A"]);
            await Assert.That(environment.History.GetHistory(state.Descriptor.Directory).Count).IsEqualTo(2);
            await Assert.That(again).Throws<CommandException>();
        }
    }

    [Test]
    public async Task SuitesOfOneName_InTwoNamespaces_GetTwoIds()
    {
        using var environment = new ExtensionTestEnvironment();
        var state = await environment.Manager.CreateTestSuiteAsync("Checks", null, null, null, CancellationToken.None);

        await environment.Manager.WriteFileAsync(state.Descriptor.Id, "src/Checks.cs", """
            using AnalyseTool.Sdk.Testing;

            namespace Arc { public sealed class Rules : IModelTestSuite { public void Define(ISuite s) => s.Test("A", _ => { }); } }
            namespace Mep { public sealed class Rules : IModelTestSuite { public void Define(ISuite s) => s.Test("B", _ => { }); } }
            """, CancellationToken.None);

        await Assert.That(environment.Tests.GetSuites().Select(suite => suite.Id)).IsEquivalentTo(["checks.Arc.Rules", "checks.Mep.Rules"]);
    }

    [Test]
    public async Task RemovedExtension_TakesItsSuitesAlong()
    {
        using var environment = new ExtensionTestEnvironment();
        var state = await environment.Manager.CreateTestSuiteAsync("Checks", null, null, null, CancellationToken.None);

        await environment.Host.UnloadAsync(state.Descriptor.Id);

        await Assert.That(environment.Tests.GetSuites()).IsEmpty();
    }

    [Test]
    public async Task TestSuiteTemplate_NamesItsClassAfterTheSuite()
    {
        var source = ExtensionScaffold.TestSuite("Fire \"safety\"");

        using (Assert.Multiple())
        {
            await Assert.That(source).Contains("public sealed class FireSafety : IModelTestSuite");
            await Assert.That(source).Contains("s.Name(\"Fire \\\"safety\\\"\");");
        }
    }
}
