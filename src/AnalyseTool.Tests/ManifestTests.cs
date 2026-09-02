using AnalyseTool.Core.Common.Extensions;
using Newtonsoft.Json;

namespace AnalyseTool.Tests;

/// <summary>plugin.json as the host reads it: the parts an author or the Edit form can get wrong.</summary>
public class ManifestTests
{
    [Test]
    public async Task Schema_defaults_to_1_and_the_singular_button_still_works()
    {
        var m = Parse("""{"id":"acme.x","version":"1.0.0","ui":{"button":{"name":"X"}}}""")!;

        await Assert.That(m.Schema).IsEqualTo(1);
        await Assert.That(m.Ui!.EffectiveButtons().Select(b => b.Name)).IsEquivalentTo(new[] { "X" });
    }

    [Test]
    public async Task Buttons_win_over_button_when_both_are_present()
    {
        var m = Parse("""{"id":"acme.x","version":"1.0.0","ui":{"button":{"name":"Old"},"buttons":[{"name":"A"},{"name":"B"}]}}""")!;

        await Assert.That(m.Ui!.EffectiveButtons().Select(b => b.Name)).IsEquivalentTo(new[] { "A", "B" });
    }

    [Test]
    public async Task Order_sorts_lower_first_and_unset_after_every_numbered_one()
    {
        // "0 = no preference" must go LAST: a person who types 1 into one button expects it first,
        // not behind everything that never said (the 2026-09-02 field report on the Edit form).
        var m = Parse("""{"id":"acme.x","version":"1.0.0","ui":{"buttons":[{"name":"unset-1"},{"name":"third","order":3},{"name":"first","order":1},{"name":"unset-2"},{"name":"second","order":2}]}}""")!;

        await Assert.That(m.Ui!.EffectiveButtons().Select(b => b.Name))
            .IsEquivalentTo(new[] { "first", "second", "third", "unset-1", "unset-2" });
    }

    [Test]
    [Arguments("push", "Push")]
    [Arguments("STACKED", "Stacked")]
    [Arguments(" pulldown ", "Pulldown")]
    [Arguments("hexagonal", "Push")]
    [Arguments(null, "Push")]
    public async Task Kind_is_case_insensitive_and_unknown_falls_back_to_push(string? kind, string expected)
    {
        // An unknown kind must still yield a usable ribbon: a manifest written against a later host
        // falls back to a large button instead of vanishing. (Compared as text: the enum is internal,
        // and a public test signature may not expose it.)
        var button = new ExtensionButton { Name = "x", Kind = kind };
        await Assert.That(button.ResolvedKind.ToString()).IsEqualTo(expected);
    }

    private static ExtensionManifest? Parse(string json) => JsonConvert.DeserializeObject<ExtensionManifest>(json);
}
