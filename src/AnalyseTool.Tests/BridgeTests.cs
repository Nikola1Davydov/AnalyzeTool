using AnalyseTool.Mcp.Bridge;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Tests;

/// <summary>The two files in the bridge that exist only because the caller is a language model.</summary>
public class BridgeTests
{
    private const string Schema = """{"type":"object","properties":{"category":{"type":["string","null"]},"limit":{"type":["integer","null"]},"parameterNames":{"type":["array","null"],"items":{"type":"string"}}}}""";

    [Test]
    public async Task A_valid_payload_raises_no_complaint()
    {
        string? complaint = PayloadValidator.Validate("GetElements", Schema,
            JObject.Parse("""{"category":"Wände","limit":5,"parameterNames":["Kommentare"]}"""));
        await Assert.That(complaint).IsNull();
    }

    [Test]
    public async Task A_misspelled_parameter_is_named_with_the_declared_ones()
    {
        // Newtonsoft would drop "catgory" silently and the agent would build on an unfiltered answer.
        string? complaint = PayloadValidator.Validate("GetElements", Schema, JObject.Parse("""{"catgory":"Wände"}"""));
        await Assert.That(complaint).IsNotNull();
        await Assert.That(complaint!).Contains("catgory");
        await Assert.That(complaint!).Contains("category");
    }

    [Test]
    public async Task Property_names_bind_case_insensitively_like_newtonsoft()
    {
        string? complaint = PayloadValidator.Validate("GetElements", Schema, JObject.Parse("""{"Category":"Wände"}"""));
        await Assert.That(complaint).IsNull();
    }

    [Test]
    public async Task A_non_object_payload_for_a_command_with_parameters_is_refused()
    {
        string? complaint = PayloadValidator.Validate("GetElements", Schema, new JArray("Wände"));
        await Assert.That(complaint).IsNotNull();
    }

    [Test]
    public async Task No_schema_or_no_payload_means_no_opinion()
    {
        // Fail-open by design: the validator only speaks about what it is sure of.
        await Assert.That(PayloadValidator.Validate("X", null, JObject.Parse("{\"a\":1}"))).IsNull();
        await Assert.That(PayloadValidator.Validate("X", Schema, null)).IsNull();
        await Assert.That(PayloadValidator.Validate("X", """{"type":"object","properties":{}}""", JObject.Parse("{\"a\":1}"))).IsNull();
    }

    [Test]
    [Arguments("GetElement", "GetElements")]
    [Arguments("getmodeloverview", "GetModelOverview")]
    [Arguments("SetDataToParameter", "SetDataToParameters")]
    public async Task A_near_miss_maps_to_the_real_command(string typed, string expected)
    {
        string[] candidates = ["GetElements", "GetModelOverview", "SetDataToParameters", "GetCategoriesInRevit"];
        await Assert.That(NearestName.Closest(typed, candidates)).IsEqualTo(expected);
    }

    [Test]
    public async Task Nonsense_maps_to_nothing()
    {
        string[] candidates = ["GetElements", "GetModelOverview"];
        await Assert.That(NearestName.Closest("PurgeUniverse", candidates)).IsNull();
    }
}
