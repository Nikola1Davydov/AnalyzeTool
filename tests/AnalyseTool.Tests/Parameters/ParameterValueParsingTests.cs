using System.Globalization;
using AnalyseTool.Services.Parameters;

namespace AnalyseTool.Tests.Parameters;

/// <summary>
///     Text written into Integer, Double and ElementId parameters: 64-bit ids, and the same answer on a German machine as
///     on an English one.
/// </summary>
public sealed class ParameterValueParsingTests
{
    [Test]
    [Arguments("5000000000", 5_000_000_000L)]
    [Arguments(" 42 ", 42L)]
    [Arguments("-1", -1L)]
    public async Task TryParseElementId_Number_Parses64Bit(string text, long expected)
    {
        await Assert.That(ParameterValues.TryParseElementId(text, out var id)).IsTrue();
        await Assert.That(id).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("MCP-Test")]
    [Arguments("1.5")]
    public async Task TryParseElementId_NoNumber_IsRefused(string text)
    {
        await Assert.That(ParameterValues.TryParseElementId(text, out _)).IsFalse();
    }

    [Test]
    [Arguments("2400", 2400d)]
    [Arguments(" 2,5 ", 2.5d)]
    [Arguments("1.25", 1.25d)]
    public async Task TryParseDouble_CommaOrPoint_ParsesInvariant(string text, double expected)
    {
        await Assert.That(ParameterValues.TryParseDouble(text, out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async Task TryParseInteger_AnyCulture_ParsesInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // A culture whose negative sign is not '-' refused "-5" with a culture-bound parse
            CultureInfo.CurrentCulture = new CultureInfo("en-US") { NumberFormat = { NegativeSign = "~" } };

            await Assert.That(int.TryParse("-5", out _)).IsFalse();
            await Assert.That(ParameterValues.TryParseInteger("-5", out var value)).IsTrue();
            await Assert.That(value).IsEqualTo(-5);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    [Arguments("autodesk.spec.aec:length-2.0.0", "length")]
    [Arguments("autodesk.spec:spec.string-2.0.0", "string")]
    [Arguments("autodesk.unit.unit:millimeters-1.0.1", "millimeters")]
    public async Task ShortForgeId_TypeId_IsTheReadablePart(string typeId, string expected)
    {
        await Assert.That(ParameterValues.ShortForgeId(typeId)).IsEqualTo(expected);
    }
}
