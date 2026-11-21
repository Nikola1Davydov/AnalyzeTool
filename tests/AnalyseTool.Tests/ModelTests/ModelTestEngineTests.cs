using System.IO;
using AnalyseTool.Extensions.Testing;
using AnalyseTool.Sdk.Testing;
using Autodesk.Revit.DB;

namespace AnalyseTool.Tests.ModelTests;

/// <summary>The fluent builder, the engine, the baseline and the trend, on a model without Revit.</summary>
public sealed class ModelTestEngineTests
{
    private static readonly ModelTestRunOptions All = new();

    [Test]
    public async Task Builder_Rule_NamesItselfFromWhatItChecks()
    {
        var suite = Define(s => s.Elements(BuiltInCategory.OST_Doors).Parameter("Fire Rating").NotEmpty().FixValue("EI30"));

        var test = suite.Tests.Single();
        using (Assert.Multiple())
        {
            await Assert.That(test.Name).IsEqualTo("Doors · Fire Rating · not empty");
            await Assert.That(test.Id).IsEqualTo("bep.Suite/doors-fire-rating-not-empty");
            await Assert.That(test.Kind).IsEqualTo(ModelTestKind.Rule);
            await Assert.That(test.FixLabel).IsEqualTo("Set to 'EI30'");
            await Assert.That(test.File).IsEqualTo(Path.GetFileName(ThisFile()));
            await Assert.That(test.Line).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task Describe_NamesCategoryAndParameterAsRevitDoes_KeepsTheApiNamesAndTheId()
    {
        var suite = Define(s => s.Elements(BuiltInCategory.OST_Walls).Parameter(BuiltInParameter.ALL_MODEL_MARK).NotEmpty().Unique());
        var german = new RevitNames(category => category.ToString() == "OST_Walls" ? "Wände" : "?", parameter => parameter.ToString() == "ALL_MODEL_MARK" ? "Kennzeichen" : "?");

        var info = ModelTestCatalog.Describe(suite.Tests.Single(), german);

        using (Assert.Multiple())
        {
            await Assert.That(info.Name).IsEqualTo("Wände · Kennzeichen · not empty, unique");
            await Assert.That(info.Id).IsEqualTo("bep.Suite/walls-all-model-mark-not-empty-unique");
            await Assert.That(info.Categories.Single()).IsEqualTo(new RevitName("Wände", "OST_Walls"));
            await Assert.That(info.Parameter).IsEqualTo(new RevitName("Kennzeichen", "ALL_MODEL_MARK"));
            await Assert.That(info.Checks).IsEqualTo("not empty, unique");
            await Assert.That(info.TypeParameter).IsFalse();
        }
    }

    [Test]
    public async Task Builder_SameNameTwice_GetsANumber_ExplicitIdWins()
    {
        var suite = Define(s =>
        {
            s.Test("Rooms", _ => { });
            s.Test("Rooms", _ => { });
            s.Test("Rooms", _ => { }).Id("Rooms placed");
        });

        await Assert.That(suite.Tests.Select(test => test.LocalId)).IsEquivalentTo(new string?[] { "rooms", "rooms-2", "rooms-placed" });
    }

    [Test]
    public async Task Builder_SameExplicitIdTwice_SecondGetsANumberAndIsReported()
    {
        var suite = new SuiteDefinition { Id = "bep.Suite", ExtensionId = "bep", ClassName = "Suite", Directory = TempDirectory(), BaselineDirectory = TempDirectory() };
        var builder = new SuiteBuilder(suite, suite.Directory);
        builder.Test("A", _ => { }).Id("walls");
        builder.Test("B", _ => { }).Id("walls");

        var problems = builder.Complete();

        using (Assert.Multiple())
        {
            await Assert.That(suite.Tests.Select(test => test.LocalId)).IsEquivalentTo(new string?[] { "walls", "walls-2" });
            await Assert.That(problems.Single()).Contains("'walls-2'");
        }
    }

    [Test]
    public async Task Builder_TraitsOfTheSuite_ReachEveryTest()
    {
        var suite = Define(s =>
        {
            s.Trait("Discipline", "ARC");
            s.Test("A", _ => { }).Trait("Speed", "fast").Severity(TestSeverity.Warning);
        });

        var info = ModelTestCatalog.Describe(suite.Tests.Single());
        using (Assert.Multiple())
        {
            await Assert.That(info.Traits).IsEquivalentTo(["Discipline=ARC", "Speed=fast"]);
            await Assert.That(info.Severity).IsEqualTo("Warning");
        }
    }

    [Test]
    public async Task Rule_NotEmpty_FailsMissingAndBlank_PassesTheRest()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Fire Rating", "EI30"));
        model.Add(2, BuiltInCategory.OST_Doors, parameters: ("Fire Rating", " "));
        model.Add(3, BuiltInCategory.OST_Doors);
        model.Add(4, BuiltInCategory.OST_Walls, parameters: ("Fire Rating", null));
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors).Parameter("Fire Rating").NotEmpty()).Tests.Single();

        var result = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo("failed");
            await Assert.That(result.Checked).IsEqualTo(3);
            await Assert.That(result.New).IsEqualTo(2);
            await Assert.That(result.Cases.Select(item => item.Message)).IsEquivalentTo(["«Fire Rating» is empty", "«Fire Rating» is missing"]);
            await Assert.That(result.Cases.Select(item => item.Key)).IsEquivalentTo(["uid-2||", "uid-3||"]);
            await Assert.That(result.Cases.Select(item => item.ElementId ?? 0)).IsEquivalentTo([2L, 3L]);
            await Assert.That(result.Cases.Select(item => item.ValueState)).IsEquivalentTo(new string?[] { "empty", "missing" });
        }
    }

    [Test]
    public async Task Rule_Conditions_TheFirstThatFailsIsTheMessage()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Width", "1200"));
        model.Add(2, BuiltInCategory.OST_Doors, parameters: ("Width", "wide"));
        model.Add(3, BuiltInCategory.OST_Doors, parameters: ("Width", "800"));
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors).Parameter("Width").NotEmpty().InRange(800, 1000)).Tests.Single();

        var result = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(result.Cases.Select(item => item.Message)).IsEquivalentTo(["«Width» is 1200, expected 800…1000", "«Width» 'wide' is not a number"]);
            await Assert.That(result.Cases.Select(item => item.Value)).IsEquivalentTo(new string?[] { "1200", "wide" });
            await Assert.That(result.Cases.All(item => item.ValueState == "set")).IsTrue();
        }
    }

    [Test]
    public async Task Rule_BuiltInParameter_ReadsByIdAndNamesItAsRevitShowsIt()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("ALL_MODEL_MARK", ""));
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors).Parameter(BuiltInParameter.ALL_MODEL_MARK).NotEmpty()).Tests.Single();

        var result = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None);

        await Assert.That(result.Cases.Single().Message).IsEqualTo("«Mark» is empty");
    }

    [Test]
    public async Task Rule_Unique_FailsEveryElementThatSharesAValue()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Rooms, parameters: ("Number", "0.14"));
        model.Add(2, BuiltInCategory.OST_Rooms, parameters: ("Number", "0.14 "));
        model.Add(3, BuiltInCategory.OST_Rooms, parameters: ("Number", "0.15"));
        model.Add(4, BuiltInCategory.OST_Rooms, parameters: ("Number", ""));
        model.Add(5, BuiltInCategory.OST_Rooms, parameters: ("Number", ""));
        var test = Define(s => s.Elements(BuiltInCategory.OST_Rooms).Parameter("Number").Unique()).Tests.Single();

        var result = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsEqualTo(2);
            await Assert.That(result.Cases.Select(item => item.ElementId ?? 0)).IsEquivalentTo([1L, 2L]);
            await Assert.That(result.Cases.First(item => item.ElementId == 1).Related).IsEquivalentTo([2L]);
            await Assert.That(result.Cases.First().Message).IsEqualTo("«Number» '0.14' is used by 1 other element");
        }
    }

    [Test]
    public async Task Rule_Unique_CaseKeys_DoNotDependOnWhoElseSharesTheValue()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Rooms, parameters: ("Number", "0.14"));
        model.Add(2, BuiltInCategory.OST_Rooms, parameters: ("Number", "0.14"));
        var test = Define(s => s.Elements(BuiltInCategory.OST_Rooms).Parameter("Number").Unique()).Tests.Single();

        var two = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None);
        var accepted = two.Cases.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        model.Add(3, BuiltInCategory.OST_Rooms, parameters: ("Number", "0.14"));
        var three = ModelTestEngine.Run(test, model, accepted, All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(two.Cases.Select(item => item.Key)).IsEquivalentTo(["uid-1||unique", "uid-2||unique"]);
            // A third element joining the duplicates is the only new failure: the accepted two keep their keys
            await Assert.That(three.New).IsEqualTo(1);
            await Assert.That(three.Cases.Single(item => !item.InBaseline).ElementId).IsEqualTo(3L);
        }
    }

    [Test]
    public async Task Baseline_AcceptedFailures_DoNotFail_AndFixedOnesCountAsResolved()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Mark", ""));
        model.Add(2, BuiltInCategory.OST_Doors, parameters: ("Mark", "D2"));
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors).Parameter("Mark").NotEmpty()).Tests.Single();

        var result = ModelTestEngine.Run(test, model, new HashSet<string> { "uid-1||", "uid-2||" }, All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo("passed");
            await Assert.That(result.Baseline).IsEqualTo(1);
            await Assert.That(result.New).IsEqualTo(0);
            await Assert.That(result.Resolved).IsEqualTo(1);
            await Assert.That(result.Cases.Single().InBaseline).IsTrue();
        }
    }

    [Test]
    public async Task Baseline_AnAcceptedCaseThatNowErrors_IsNotResolved()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Mark", ""));
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors).Check("Mark ok", (_, _) => throw new InvalidOperationException("bad"))).Tests.Single();

        var result = ModelTestEngine.Run(test, model, new HashSet<string> { "uid-1||" }, All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(result.Errors).IsEqualTo(1);
            // The element was not checked, only broken: its accepted failure is not resolved
            await Assert.That(result.Resolved).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Check_SoftAssertions_EveryFailureIsACase_AndAThrowIsAnError()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Mark", ""));
        model.Add(2, BuiltInCategory.OST_Doors, parameters: ("Mark", "boom"));
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors).Check("Two things", (door, m) =>
        {
            m.Assert.NotEmpty(door, "Mark");
            m.Assert.True(false, "Always wrong");
            if (model.Value(door, "Mark") == "boom")
            {
                throw new InvalidOperationException("bad door");
            }
        })).Tests.Single();

        var result = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo("failed");
            await Assert.That(result.Failed).IsEqualTo(3);
            await Assert.That(result.Errors).IsEqualTo(1);
            await Assert.That(result.Cases.Where(item => item.ElementId == 1).Select(item => item.Key)).IsEquivalentTo(["uid-1||", "uid-1||#2"]);
            await Assert.That(result.Cases.Single(item => item.Status == "error").Message).Contains("bad door");
        }
    }

    [Test]
    public async Task ModelTest_Empty_EachItemFails_AndItSkipsOnSelectedElements()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Rooms, parameters: ("Placed", "no"));
        model.Add(2, BuiltInCategory.OST_Rooms, parameters: ("Placed", "yes"));
        var test = Define(s => s.Test("No unplaced rooms", m =>
            m.Assert.Empty(m.Elements(BuiltInCategory.OST_Rooms).Where(room => model.Value(room, "Placed") == "no"), "The room is not placed"))).Tests.Single();

        var full = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None);
        var onSelection = ModelTestEngine.Run(test, model, new HashSet<string>(), new ModelTestRunOptions(ElementIds: [2]), CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(full.Cases.Single().ElementId).IsEqualTo(1L);
            await Assert.That(full.Cases.Single().Message).IsEqualTo("The room is not placed");
            await Assert.That(onSelection.Status).IsEqualTo("skipped");
        }
    }

    [Test]
    public async Task Run_SlowRunsByDefault_AQuickRunLeavesItOut_SkippedNever()
    {
        var model = new FakeModel();
        var suite = Define(s =>
        {
            s.Test("Slow one", m => m.Assert.Fail("ran")).Slow();
            s.Test("Off", m => m.Assert.Fail("ran")).Skip("Not ready");
        });
        var slow = suite.Tests[0];

        // Run means every test: a slow one too, unless a quick run leaves it out; named, it runs anyway
        var byDefault = ModelTestEngine.Run(slow, model, new HashSet<string>(), All, CancellationToken.None);
        var quick = ModelTestEngine.Run(slow, model, new HashSet<string>(), new ModelTestRunOptions(IncludeSlow: false), CancellationToken.None);
        var named = ModelTestEngine.Run(slow, model, new HashSet<string>(), new ModelTestRunOptions(Tests: [slow.Id], IncludeSlow: false), CancellationToken.None);
        var skipped = ModelTestEngine.Run(suite.Tests[1], model, new HashSet<string>(), All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(byDefault.Status).IsEqualTo("failed");
            await Assert.That(quick.Status).IsEqualTo("skipped");
            await Assert.That(named.Status).IsEqualTo("failed");
            await Assert.That(skipped.Skip).IsEqualTo("Not ready");
        }
    }

    [Test]
    public async Task Where_ThatThrows_IsAnErrorOfThatElement()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Mark", "A"));
        model.Add(2, BuiltInCategory.OST_Doors, parameters: ("Mark", "B"));
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors)
            .Where(door => model.Value(door, "Mark") == "A" ? true : throw new InvalidOperationException("filter broke"))
            .Parameter("Mark").NotEmpty()).Tests.Single();

        var result = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(result.Checked).IsEqualTo(1);
            await Assert.That(result.Status).IsEqualTo("error");
            await Assert.That(result.Cases.Single().Message).Contains("Where threw");
        }
    }

    [Test]
    public async Task Fix_WritesTheValue_AndReportsWhatItCouldNot()
    {
        var model = new FakeModel();
        var door = model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Fire Rating", ""));
        model.Add(2, BuiltInCategory.OST_Doors);
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors).Parameter("Fire Rating").NotEmpty().FixValue("EI30")).Tests.Single();
        var keys = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None).Cases.Select(item => item.Key).Append("uid-99||").ToList();

        var (fixedCount, problems) = ModelTestEngine.Fix(test, model, keys, CancellationToken.None);
        var after = ModelTestEngine.Run(test, model, new HashSet<string>(), All, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(fixedCount).IsEqualTo(1);
            await Assert.That(model.Value(door, "Fire Rating")).IsEqualTo("EI30");
            await Assert.That(problems.Count).IsEqualTo(2);
            await Assert.That(problems.Any(problem => problem.Contains("no longer in the model"))).IsTrue();
            await Assert.That(after.Cases.Single().ElementId).IsEqualTo(2L);
        }
    }

    [Test]
    public async Task Fix_ACodeRepair_RunsForEachElement()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Mark", ""));
        var repaired = new List<Element>();
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors).Check("Mark set", (door, m) => m.Assert.NotEmpty(door, "Mark")).Fix("Mark it", door => repaired.Add(door))).Tests.Single();

        var (fixedCount, _) = ModelTestEngine.Fix(test, model, ["uid-1||", "uid-1||#2"], CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(fixedCount).IsEqualTo(1);
            await Assert.That(repaired).HasSingleItem();
            await Assert.That(ModelTestEngine.ElementOf("model||x")).IsNull();
        }
    }

    [Test]
    public async Task BaselineFile_AcceptAndTakeBack_EmptyFileIsDeleted()
    {
        var directory = TempDirectory();
        var cases = new[] { Case("uid-1||"), Case("uid-2||") };

        var accepted = ModelTestBaseline.Change(directory, "model:1", "Model", "doors", cases, [], "me");
        var path = ModelTestBaseline.PathOf(directory, "model:1");
        var keys = ModelTestBaseline.Accepted(ModelTestBaseline.Load(directory, "model:1"), "doors");
        var left = ModelTestBaseline.Change(directory, "model:1", "Model", "doors", [], ["uid-1||", "uid-2||"], "me");

        using (Assert.Multiple())
        {
            await Assert.That(accepted).IsEqualTo(2);
            await Assert.That(Path.GetFileName(path)).IsEqualTo("model_1.json");
            await Assert.That(keys).IsEquivalentTo(["uid-1||", "uid-2||"]);
            await Assert.That(left).IsEqualTo(0);
            await Assert.That(File.Exists(path)).IsFalse();
        }
    }

    [Test]
    public async Task Execute_UsesTheSuiteBaseline_AndOnlyFullRunsMakeTheTrend()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Mark", ""));
        model.Add(2, BuiltInCategory.OST_Doors, parameters: ("Mark", ""));
        var suite = Define(s => s.Elements(BuiltInCategory.OST_Doors).Parameter("Mark").NotEmpty());
        var test = suite.Tests.Single();
        ModelTestBaseline.Change(suite.BaselineDirectory, model.ModelKey, model.ModelTitle, test.BaselineId, [Case("uid-1||")], [], "me");
        var history = TempDirectory();
        var tests = ModelTestService.Select([suite], All);

        var first = ModelTestService.Execute(model, tests, All, "page", history, CancellationToken.None);
        var partial = ModelTestService.Execute(model, tests, new ModelTestRunOptions(ElementIds: [2]), "page", history, CancellationToken.None);
        var second = ModelTestService.Execute(model, tests, All, "watch", history, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(first.Totals.NewCases).IsEqualTo(1);
            await Assert.That(first.Totals.BaselineCases).IsEqualTo(1);
            await Assert.That(first.Full).IsTrue();
            await Assert.That(partial.Full).IsFalse();
            await Assert.That(partial.Trend.Count).IsEqualTo(1);
            await Assert.That(second.Trend.Count).IsEqualTo(2);
            await Assert.That(second.Trigger).IsEqualTo("watch");
        }
    }

    [Test]
    public async Task Execute_TrendOfAFullRun_LeavesTheSlowTestsOut()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Mark", ""));
        var suite = Define(s =>
        {
            s.Elements(BuiltInCategory.OST_Doors).Parameter("Mark").NotEmpty();
            s.Test("Slow sweep", m => m.Assert.Fail("heavy")).Slow();
        });
        var history = TempDirectory();
        var tests = ModelTestService.Select([suite], All);

        var run = ModelTestService.Execute(model, tests, new ModelTestRunOptions(IncludeSlow: true), "page", history, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(run.Full).IsTrue();
            await Assert.That(run.Totals.NewCases).IsEqualTo(2);
            // The slow test's failure stays out of the trend: it compares the default runs with each other
            await Assert.That(run.Trend.Single().NewCases).IsEqualTo(1);
            await Assert.That(run.Trend.Single().Failed).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Baseline_TwoSuitesOfOneExtension_KeepTheirEntriesApart()
    {
        var directory = TempDirectory();
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Mark", ""));
        SuiteDefinition Make(string className)
        {
            var suite = new SuiteDefinition { Id = $"bep.{className}", ExtensionId = "bep", ClassName = className, Directory = directory, BaselineDirectory = directory };
            var builder = new SuiteBuilder(suite, directory);
            builder.Elements(BuiltInCategory.OST_Doors).Parameter("Mark").NotEmpty();
            builder.Complete();
            return suite;
        }

        var a = Make("SuiteA");
        var b = Make("SuiteB");
        ModelTestBaseline.Change(directory, model.ModelKey, model.ModelTitle, a.Tests.Single().BaselineId, [Case("uid-1||")], [], "me");

        var run = ModelTestService.Execute(model, ModelTestService.Select([a, b], All), All, "page", null, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(a.Tests.Single().BaselineId).IsEqualTo("SuiteA/doors-mark-not-empty");
            // The suites share the baseline file (one folder), not the entries: accepting in A says nothing about B
            await Assert.That(run.Results[0].Status).IsEqualTo("passed");
            await Assert.That(run.Results[1].Status).IsEqualTo("failed");
        }
    }

    [Test]
    public async Task Select_BySuiteExtensionTraitAndId()
    {
        var a = Define(s =>
        {
            s.Trait("Discipline", "ARC");
            s.Test("One", _ => { });
        });
        var b = Define(s => s.Test("Two", _ => { }).Trait("Discipline", "MEP"), "other.Suite", "other");

        using (Assert.Multiple())
        {
            await Assert.That(ModelTestService.Select([a, b], new ModelTestRunOptions(Suite: "other")).Single().Test.Name).IsEqualTo("Two");
            await Assert.That(ModelTestService.Select([a, b], new ModelTestRunOptions(Trait: "discipline=arc")).Single().Test.Name).IsEqualTo("One");
            await Assert.That(ModelTestService.Select([a, b], new ModelTestRunOptions(Trait: "Discipline")).Count).IsEqualTo(2);
            await Assert.That(ModelTestService.Select([a, b], new ModelTestRunOptions(Tests: ["other.Suite/two"])).Single().Test.Name).IsEqualTo("Two");
        }
    }

    private static SuiteDefinition Define(Action<ISuite> define, string id = "bep.Suite", string extension = "bep")
    {
        var directory = Path.GetDirectoryName(ThisFile())!;
        var suite = new SuiteDefinition
        {
            Id = id,
            ExtensionId = extension,
            ClassName = "Suite",
            Directory = directory,
            BaselineDirectory = TempDirectory()
        };
        var builder = new SuiteBuilder(suite, directory);
        define(builder);
        builder.Complete();
        return suite;
    }

    private static ModelTestCase Case(string key)
    {
        return new ModelTestCase(key, "failed", "«Mark» is empty", 1, "Doors", [], false, null, null);
    }

    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AnalyseTool.Tests", "model-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string file = "")
    {
        return file;
    }
}
