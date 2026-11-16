using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Threading;
using AnalyseTool.Infrastructure;
using AnalyseTool.Services.Actions;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;
using AnalyseTool.Services.Parameters;
using AnalyseTool.Services.SharedParameters;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnalyseTool.Tests.Parameters;

/// <summary>
///     The contract of the commands ported from 1.x (Elements, Actions, SharedParameters): the wire names the frontend
///     maps 1:1, which of them are MCP tools, request schemas an MCP client can call, constructors the container can
///     satisfy, and the JSON the pipeline writes.
/// </summary>
public sealed class ToolCommandsContractTests
{
    private static readonly string[] SliceNamespaces =
    [
        "AnalyseTool.Features.Elements",
        "AnalyseTool.Features.Actions",
        "AnalyseTool.Features.SharedParameters"
    ];

    /// <summary>The 1.x command names and whether 1.x showed them to MCP.</summary>
    private static readonly Dictionary<string, bool> Expected = new()
    {
        ["GetCadImports"] = true,
        ["GetCategoriesInRevit"] = true,
        ["GetCategoryParameters"] = true,
        ["GetDataByCategoryName"] = false,
        ["GetDocumentData"] = true,
        ["GetElements"] = true,
        ["GetLinksInRevit"] = true,
        ["GetModelOverview"] = true,
        ["GetTypeParameters"] = true,
        ["GetViewsAndSheets"] = true,
        ["GetWarningsInRevit"] = true,
        ["GetWorksets"] = true,
        ["IsolationInRevit"] = true,
        ["SelectionInRevit"] = true,
        ["SetDataToParameters"] = true,
        ["BindSharedParameters"] = true,
        ["GetBindingOptions"] = true,
        ["GetParameterReport"] = true,
        ["GetParameterSets"] = false,
        ["GetProjectParameters"] = true,
        ["GetSharedParameterFile"] = true,
        ["SaveParameterSets"] = false,
        ["SaveSharedParameterFile"] = true,
        ["SetSharedParameterFile"] = false
    };

    /// <summary>The options of <c>ConfigureJsonSerializer</c> in ServiceDefaults.</summary>
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    private static List<Type> SliceCommands()
    {
        return LoadableTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && SliceNamespaces.Contains(type.Namespace))
            .Where(type => CommandInterface(type) is not null)
            .ToList();
    }

    private static Type? CommandInterface(Type type)
    {
        return type.GetInterfaces().FirstOrDefault(item =>
            item.IsGenericType && item.GetGenericTypeDefinition() is var definition &&
            (definition == typeof(ICommand<>) || definition == typeof(ICommand<,>)));
    }

    [Test]
    public async Task Commands_HaveThe1xNamesAndMcpVisibility()
    {
        var actual = SliceCommands().ToDictionary(CommandNaming.GetName, type => type.GetCustomAttribute<McpToolAttribute>() is not null);

        await Assert.That(actual).IsEquivalentTo(Expected);
    }

    [Test]
    public async Task Commands_NamesAreUniqueInTheAddIn()
    {
        var names = LoadableTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && CommandInterface(type) is not null)
            .Select(CommandNaming.GetName)
            .ToList();

        await Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()).IsEqualTo(names.Count);
    }

    [Test]
    public async Task Commands_HaveADescription()
    {
        var missing = SliceCommands()
            .Where(type => string.IsNullOrWhiteSpace(type.GetCustomAttribute<CommandAttribute>()?.Description))
            .Select(type => type.Name)
            .ToList();

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task McpTools_RequestSchemasAreObjectsWithProperties()
    {
        var broken = new List<string>();
        foreach (var type in SliceCommands().Where(type => type.GetCustomAttribute<McpToolAttribute>() is not null))
        {
            var arguments = CommandInterface(type)!.GetGenericArguments();
            if (arguments.Length != 2)
            {
                continue;
            }

            var schema = AIJsonUtilities.CreateJsonSchema(arguments[0], serializerOptions: WireOptions);
            if (!schema.TryGetProperty("type", out var kind) || kind.GetString() != "object" ||
                !schema.TryGetProperty("properties", out var properties) || !properties.EnumerateObject().Any())
            {
                broken.Add(type.Name);
            }
        }

        await Assert.That(broken).IsEmpty();
    }

    [Test]
    public async Task McpTools_WritingCommandsAreNotReadOnly()
    {
        var writing = new[] { "SetDataToParameters", "BindSharedParameters", "SaveSharedParameterFile", "IsolationInRevit", "SelectionInRevit" };
        var tools = SliceCommands().ToDictionary(CommandNaming.GetName, type => type.GetCustomAttribute<McpToolAttribute>());

        foreach (var name in writing)
        {
            await Assert.That(tools[name]!.ReadOnly).IsFalse();
        }

        await Assert.That(tools["SetDataToParameters"]!.Destructive).IsTrue();
        await Assert.That(tools["BindSharedParameters"]!.Destructive).IsTrue();
        await Assert.That(tools["SaveSharedParameterFile"]!.Destructive).IsTrue();
        await Assert.That(tools["GetElements"]!.ReadOnly).IsTrue();
    }

    [Test]
    public async Task Commands_ResolveFromTheContainer()
    {
        var services = new ServiceCollection()
            .AddTools()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
            // A dispatcher type would load RevitAPIUI, which this test host has not got; the commands only store it
            .AddSingleton<IRevitDispatcher>(_ => null!)
            .AddScoped<ActiveDocument>();
        foreach (var type in SliceCommands())
        {
            services.AddScoped(type);
        }

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        foreach (var type in SliceCommands())
        {
            await Assert.That(scope.ServiceProvider.GetRequiredService(type)).IsNotNull();
        }
    }

    [Test]
    public async Task Requests_WithoutPayloadGetTheDefaultsOf1x()
    {
        var elements = JsonSerializer.Deserialize<Features.Elements.GetElementsRequest>("{}", WireOptions)!;
        var report = JsonSerializer.Deserialize<Features.SharedParameters.GetParameterReportRequest>("{}", WireOptions)!;
        var bind = JsonSerializer.Deserialize<Features.SharedParameters.BindSharedParametersRequest>("""{"guids":["x"]}""", WireOptions)!;
        var write = JsonSerializer.Deserialize<Features.Actions.SetDataToParametersRequest>(
            """{"items":[{"elementId":1,"id":-1001,"value":"a"}],"mode":"OnlyIfEmpty"}""", WireOptions)!;

        await Assert.That(elements.Limit).IsNull();
        await Assert.That(report.TopValues).IsEqualTo(10);
        await Assert.That(bind.IsInstance).IsTrue();
        await Assert.That(write.Mode).IsEqualTo(SetDataMode.OnlyIfEmpty);
        await Assert.That(write.Items.Single()!.Id).IsEqualTo(-1001);
    }

    [Test]
    public async Task Results_AreWrittenAs1xWroteThem()
    {
        var result = new SetDataResult(true, 1, 0, null, [new TransactionWarning("w", [5])]);
        var item = new ElementItem(1, "Wall", "Walls", null, "", null, false, null, "Basiswand", "Typ", null);
        var sharedFile = new SharedParameterFileData { Path = "a.txt", Exists = true };

        var resultJson = JsonSerializer.Serialize(result, WireOptions);
        var itemJson = JsonSerializer.Serialize(item, WireOptions);
        var fileJson = JsonSerializer.Serialize(sharedFile, WireOptions);

        await Assert.That(resultJson).IsEqualTo("""{"ok":true,"written":1,"skipped":0,"error":null,"warnings":[{"description":"w","elementIds":[5]}]}""");
        await Assert.That(itemJson).IsEqualTo("""{"id":1,"name":"Wall","category":"Walls","level":"","isType":false,"familyName":"Basiswand","typeName":"Typ"}""");
        await Assert.That(fileJson).DoesNotContain("\"error\"");
        await Assert.That(JsonSerializer.Serialize(ParameterOrigin.BuiltIn, WireOptions)).IsEqualTo("\"BuiltIn\"");
    }

    [Test]
    public async Task ModelOverview_CategoryCountKeysKeepTheLocalisedNames()
    {
        var overview = new ModelOverview("t", "2025", "German", "millimeters", false, null, [], new Dictionary<string, int> { ["Wände"] = 4 }, [], false);

        var json = JsonSerializer.Serialize(overview, WireOptions);

        await Assert.That(json).Contains("\"categoryCounts\":{\"W\\u00E4nde\":4}");
    }

    /// <summary>The types of the add-in that load without the Revit API, which this test host has not got.</summary>
    private static IEnumerable<Type> LoadableTypes()
    {
        Type[] types;
        try
        {
            types = typeof(ToolsRegistration).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types.OfType<Type>().ToArray();
        }

        // A type nested in one that needs the Revit API fails on its first touch
        return types.Where(Loads);
    }

    private static bool Loads(Type type)
    {
        try
        {
            _ = type.Namespace;
            _ = type.GetInterfaces();
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (TypeLoadException)
        {
            return false;
        }
    }
}
