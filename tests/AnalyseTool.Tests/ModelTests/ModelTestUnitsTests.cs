using System.IO;
using AnalyseTool.Extensions.Testing;
using AnalyseTool.Sdk.Testing;
using Autodesk.Revit.DB;

namespace AnalyseTool.Tests.ModelTests;

/// <summary>
///     Units of the rules: <c>InRange</c> compares in Revit's internal units (feet), so a suite checks the same in a project in
///     millimetres and one in metres; the failures speak the units of the project, exactly.
/// </summary>
public sealed class ModelTestUnitsTests
{
    private const string Height = "Unconnected Height";
    private static readonly ModelTestRunOptions All = new();

    [Test]
    public async Task Units_ConvertToFeet()
    {
        using (Assert.Multiple())
        {
            await Assert.That(304.8.Mm()).IsEqualTo(1.0).Within(1e-12);
            await Assert.That(1.M()).IsEqualTo(1000.Mm()).Within(1e-12);
            await Assert.That(900.Mm()).IsEqualTo(0.9.M()).Within(1e-12);
            await Assert.That(3210.Mm()).IsEqualTo(3.21 / 0.3048).Within(1e-12);
        }
    }

    [Test]
    [Arguments("mm")]
    [Arguments("m")]
    public async Task InRange_InFeet_SameResultWhateverTheProjectUnit(string unit)
    {
        var model = Walls(unit, 3210.Mm(), 2400.Mm(), 25.M(), 50.Mm());
        var result = Run(model, s => s.Elements(BuiltInCategory.OST_Walls).Parameter(Height).InRange(100.Mm(), 20000.Mm()));

        using (Assert.Multiple())
        {
            await Assert.That(result.Checked).IsEqualTo(4);
            await Assert.That(result.Cases.Select(item => item.ElementId ?? 0)).IsEquivalentTo([3L, 4L]);
        }
    }

    [Test]
    public async Task InRange_Failure_InMillimetres_WhenTheProjectIsInMillimetres()
    {
        var model = Walls("mm", 25.M());
        var result = Run(model, s => s.Elements(BuiltInCategory.OST_Walls).Parameter(Height).InRange(100.Mm(), 20000.Mm()));

        using (Assert.Multiple())
        {
            await Assert.That(result.Cases.Single().Message).IsEqualTo("«Unconnected Height» is 25000 mm, expected 100 mm…20000 mm");
            await Assert.That(result.Cases.Single().Value).IsEqualTo("25000 mm");
        }
    }

    [Test]
    public async Task InRange_Failure_InMetres_WhenTheProjectIsInMetres()
    {
        var model = Walls("m", 25.M());
        var result = Run(model, s => s.Elements(BuiltInCategory.OST_Walls).Parameter(Height).InRange(100.Mm(), 20000.Mm()));

        await Assert.That(result.Cases.Single().Message).IsEqualTo("«Unconnected Height» is 25 m, expected 0.1 m…20 m");
    }

    [Test]
    public async Task Value_IsExact_NotRoundedAsTheProjectShowsIt()
    {
        // The project shows whole metres («3 m»); the result must not say 3 where the wall is 3.21
        var model = Walls("m", 32.1.M());
        var result = Run(model, s => s.Elements(BuiltInCategory.OST_Walls).Parameter(Height).InRange(0, 20.M()));

        using (Assert.Multiple())
        {
            await Assert.That(result.Cases.Single().Value).IsEqualTo("32.1 m");
            await Assert.That(result.Cases.Single().Message).IsEqualTo("«Unconnected Height» is 32.1 m, expected 0 m…20 m");
        }
    }

    [Test]
    public async Task InRange_BoundsAreInclusive_AfterConversion()
    {
        var model = Walls("mm", 100.Mm(), 20000.Mm(), 99.Mm(), 20001.Mm());
        var result = Run(model, s => s.Elements(BuiltInCategory.OST_Walls).Parameter(Height).InRange(100.Mm(), 20000.Mm()));

        await Assert.That(result.Cases.Select(item => item.ElementId ?? 0)).IsEquivalentTo([3L, 4L]);
    }

    [Test]
    public async Task InRange_ExactBound_AsRevitStoresIt_Passes()
    {
        // Revit holds 600 mm a few last bits off 600.Mm(): a pipe of exactly 600 mm fits «10…600 mm»
        var stored = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(600.Mm()) + 2);
        var below = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(10.Mm()) - 2);
        var model = Walls("mm", stored, below, 601.Mm());
        var result = Run(model, s => s.Elements(BuiltInCategory.OST_Walls).Parameter(Height).InRange(10.Mm(), 600.Mm()));

        using (Assert.Multiple())
        {
            await Assert.That(stored).IsNotEqualTo(600.Mm());
            await Assert.That(result.Cases.Select(item => item.ElementId ?? 0)).IsEquivalentTo([3L]);
        }
    }

    [Test]
    public async Task InRange_Bounds_EitherOrder()
    {
        var model = Walls("mm", 3.M());
        var result = Run(model, s => s.Elements(BuiltInCategory.OST_Walls).Parameter(Height).InRange(20.M(), 0.1.M()));

        await Assert.That(result.Status).IsEqualTo("passed");
    }

    [Test]
    public async Task InRange_MillimetresWithoutMm_IsFeet_AndFailsInAnyProject()
    {
        // The trap the internal units remove: 3 m is 9.8 ft, never «100…20000»; the message shows why in the project's unit
        var model = Walls("m", 3.M());
        var result = Run(model, s => s.Elements(BuiltInCategory.OST_Walls).Parameter(Height).InRange(100, 20000));

        await Assert.That(result.Cases.Single().Message).IsEqualTo("«Unconnected Height» is 3 m, expected 30.48 m…6096 m");
    }

    [Test]
    public async Task InRange_PlainNumbers_StayAsTheyAre()
    {
        var model = new FakeModel();
        model.Add(1, BuiltInCategory.OST_Doors, parameters: ("Count", "12"));
        var result = Run(model, s => s.Elements(BuiltInCategory.OST_Doors).Parameter("Count").InRange(1, 10));

        using (Assert.Multiple())
        {
            await Assert.That(result.Cases.Single().Message).IsEqualTo("«Count» is 12, expected 1…10");
            await Assert.That(result.Cases.Single().Value).IsEqualTo("12");
        }
    }

    [Test]
    public async Task Describe_KeepsTheBoundsAsWritten_SoTheIdStaysTheSame()
    {
        var test = Define(s => s.Elements(BuiltInCategory.OST_Doors).Parameter("Width").InRange(800, 1000)).Tests.Single();

        await Assert.That(test.Id).IsEqualTo("bep.Suite/doors-width-in-800-1000");
    }

    private static FakeModel Walls(string unit, params double[] feet)
    {
        var model = new FakeModel { LengthUnit = unit };
        for (var index = 0; index < feet.Length; index++)
        {
            var wall = model.Add(index + 1, BuiltInCategory.OST_Walls, parameters: (Height, "0"));
            model.Length(wall, Height, feet[index]);
        }

        return model;
    }

    private static ModelTestResult Run(FakeModel model, Action<ISuite> define)
    {
        return ModelTestEngine.Run(Define(define).Tests.Single(), model, new HashSet<string>(), All, CancellationToken.None);
    }

    private static SuiteDefinition Define(Action<ISuite> define)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AnalyseTool.Tests", "model-tests", Guid.NewGuid().ToString("N"));
        var suite = new SuiteDefinition { Id = "bep.Suite", ExtensionId = "bep", ClassName = "Suite", Directory = directory, BaselineDirectory = directory };
        var builder = new SuiteBuilder(suite, directory);
        define(builder);
        builder.Complete();
        return suite;
    }
}
