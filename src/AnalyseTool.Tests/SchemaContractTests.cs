using System.Reflection;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using NJsonSchema;

namespace AnalyseTool.Tests;

/// <summary>
/// The contract that #98 broke for three weeks: what a command PUBLISHES as its output schema must
/// accept what the host actually WRITES for that type. The generator marked nullable properties
/// required while Newtonsoft omitted them when null; a validating MCP client rejected every answer of
/// GetElements, and no test could have said so because the two halves were never held side by side.
///
/// For every command with a declared OutputType (and every InputType), the schema the dispatcher would
/// register is generated the same way it is at run time, a sparse and a dense sample of the type are
/// serialized with Newtonsoft exactly as the bridge does, and the JSON is validated against the schema.
/// </summary>
public class SchemaContractTests
{
    /// <summary>Every [RevitCommand] in the platform assemblies that load without Revit.</summary>
    public static IEnumerable<(string Command, Type DeclaredType, string Role)> DeclaredTypes()
    {
        Assembly[] assemblies =
        [
            typeof(AnalyseTool.Core.Common.Dispatch.CommandDispatcher).Assembly,   // Core
            typeof(AnalyseTool.Tools.Elements.ElementsResult).Assembly,            // Tools
            typeof(AnalyseTool.Mcp.Bridge.McpBridgeServer).Assembly,               // Mcp.Bridge
        ];

        foreach (Assembly assembly in assemblies)
        foreach (Type type in LoadableTypes(assembly))
        {
            if (!typeof(IRevitTask).IsAssignableFrom(type) || type.IsAbstract) continue;
            RevitCommandAttribute? attr = type.GetCustomAttribute<RevitCommandAttribute>();
            if (attr is null) continue;
            if (attr.OutputType is not null) yield return (type.Name, attr.OutputType, "output");
            if (attr.InputType is not null) yield return (type.Name, attr.InputType, "input");
        }
    }

    [Test]
    [MethodDataSource(nameof(DeclaredTypes))]
    public async Task Schema_accepts_what_the_host_serializes(string command, Type declaredType, string role)
    {
        string schemaJson = CommandDispatcher.BuildSchema(declaredType);
        JsonSchema schema = await JsonSchema.FromJsonAsync(schemaJson);

        foreach (bool dense in new[] { false, true })
        {
            object? sample;
            string json;
            try
            {
                sample = SampleFactory.Build(declaredType, dense);
                // The bridge writes JToken.FromObject(result) with default settings — the same
                // serializer, the same settings, so an omission here is an omission on the wire.
                json = JsonConvert.SerializeObject(sample);
            }
            catch (FileNotFoundException ex) when (ex.Message.Contains("RevitAPI", StringComparison.Ordinal))
            {
                // A DTO that carries a Revit type (DataElement wraps an Element; the AI analysis request
                // carries parameter objects) cannot even be instantiated here. That is tier 3 — the
                // in-Revit project — and the skip says so instead of counting as a contract failure.
                Skip.Test($"{declaredType.Name} references Revit API types; check it in AnalyseTool.Test (inside Revit).");
                return;
            }
            ICollection<NJsonSchema.Validation.ValidationError> errors = schema.Validate(json);

            await Assert.That(errors).IsEmpty()
                .Because($"{command} ({role}, {(dense ? "dense" : "sparse")} sample of {declaredType.Name}) — " +
                         $"schema rejects the host's own JSON:\n{string.Join("\n", errors.Select(e => $"  {e.Kind} at {e.Path}"))}\n" +
                         $"json: {json}\nschema: {schemaJson}");
        }
    }

    [Test]
    public async Task Relaxing_drops_nullable_properties_from_required_recursively()
    {
        const string schema = """
            {"type":"object","properties":{
               "a":{"type":"string"},
               "b":{"type":["string","null"]},
               "nested":{"type":"object","properties":{"x":{"type":["integer","null"]},"y":{"type":"integer"}},"required":["x","y"]}
             },"required":["a","b","nested"]}
            """;

        string relaxed = CommandDispatcher.RelaxNullableRequired(schema);
        var root = Newtonsoft.Json.Linq.JObject.Parse(relaxed);

        await Assert.That(root["required"]!.Select(t => (string)t!)).IsEquivalentTo(new[] { "a", "nested" });
        await Assert.That(root["properties"]!["nested"]!["required"]!.Select(t => (string)t!)).IsEquivalentTo(new[] { "y" });
    }

    [Test]
    public async Task Relaxing_removes_an_emptied_required_list()
    {
        const string schema = """{"type":"object","properties":{"a":{"type":["string","null"]}},"required":["a"]}""";
        var root = Newtonsoft.Json.Linq.JObject.Parse(CommandDispatcher.RelaxNullableRequired(schema));
        await Assert.That(root["required"]).IsNull();
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        // A platform assembly holds a few types whose base or interface is a Revit UI type
        // (RevitTaskHub : IExternalEventHandler); those fail to load without Revit and are not commands.
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
