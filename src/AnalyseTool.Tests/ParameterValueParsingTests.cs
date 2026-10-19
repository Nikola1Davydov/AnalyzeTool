using AnalyseTool.Tools.Shared;
using System.Globalization;

namespace AnalyseTool.Tests;

/// <summary>Text written into Integer and ElementId parameters: 64-bit ids, and the same answer on a
/// German machine as on an English one.</summary>
public class ParameterValueParsingTests
{
    [Test]
    [Arguments("5000000000", 5_000_000_000L)]
    [Arguments(" 42 ", 42L)]
    [Arguments("-1", -1L)]
    public async Task ElementId_text_parses_as_64_bit(string text, long expected)
    {
        await Assert.That(ParameterExtensions.TryParseElementId(text, out long id)).IsTrue();
        await Assert.That(id).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("MCP-Test")]
    [Arguments("1.5")]
    public async Task ElementId_text_that_is_no_number_is_refused(string text)
    {
        await Assert.That(ParameterExtensions.TryParseElementId(text, out _)).IsFalse();
    }

    [Test]
    public async Task Integer_text_ignores_the_current_culture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            // A culture whose negative sign is not '-' would have refused "-5" before.
            CultureInfo.CurrentCulture = new CultureInfo("en-US") { NumberFormat = { NegativeSign = "~" } };
            await Assert.That(int.TryParse("-5", out _)).IsFalse(); // what the old culture-bound parse did
            await Assert.That(ParameterExtensions.TryParseInteger("-5", out int value)).IsTrue();
            await Assert.That(value).IsEqualTo(-5);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
