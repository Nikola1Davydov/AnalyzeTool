using System.Reflection;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Sdk;
using AnalyseTool.Sdk.Underlays;
using AnalyseTool.Tools.Underlays;
using Newtonsoft.Json.Linq;
using NJsonSchema;

namespace AnalyseTool.Tests.Mcp;

/// <summary>
/// #136 over MCP, minus Revit: an agent asked "what is in the DWG under the plan?" must get there with
/// ONE call and no prompt of its own. Everything the agent sees is checked on the real exe — the tool it
/// would pick (its description), the schema it may validate against, the answer's shape, and the PDF
/// page arriving as an image it can look at. The commands' own descriptions and schemas are taken from
/// the built Tools assembly, exactly as the dispatcher registers them; only the Revit answer is canned,
/// built from the Sdk's result types and serialized the way the bridge serializes it.
/// </summary>
[NotInParallel("mcp-exe")]
public class UnderlayMcpTests
{
    private static readonly string[] UnderlayCommands = ["GetUnderlays", "GetCadLayers", "GetCadGeometry", "GetPdfPageAsImage"];

    /// <summary>A registered built-in command as the bridge would list it.</summary>
    private static FakeBridge.CannedCommand Registered(string name)
    {
        RevitCommandAttribute attr = CommandType(name).GetCustomAttribute<RevitCommandAttribute>()!;
        return new FakeBridge.CannedCommand(
            name,
            attr.Description!,
            // What the bridge puts on the wire: the registered schema, compacted for the listing.
            SchemaListing.Compact(CommandDispatcher.BuildSchema(attr.InputType)),
            SchemaListing.Compact(CommandDispatcher.BuildSchema(attr.OutputType)),
            ReadOnly: attr.ReadOnly,
            Destructive: attr.Destructive);
    }

    // By full name: GetTypes() on the Tools assembly trips over the few types that need RevitAPI to load.
    private static Type CommandType(string name) =>
        typeof(UnderlayImageService).Assembly.GetType($"AnalyseTool.Tools.Underlays.{name}")
        ?? typeof(UnderlayImageService).Assembly.GetType($"AnalyseTool.Tools.Elements.{name}", throwOnError: true)!;

    /// <summary>The model of the issue: a linked DWG on the ground-floor plan, a PDF on an elevation —
    /// and a DWG that is not ours at all but sits inside the MEP consultant's linked Revit model.</summary>
    internal static UnderlaysResult Scenario() => new()
    {
        Count = 3,
        Underlays =
        [
            new UnderlayInfo
            {
                Id = 4711, Name = "A-101.dwg", Kind = "dwg", Source = "link",
                FilePath = @"C:\Projekt\CAD\A-101.dwg", FileStatus = "loaded",
                ViewSpecific = true, OwnerViewId = 311, OwnerViewName = "EG Grundriss", OwnerViewType = "FloorPlan", OwnerViewScale = 100,
                Pinned = true, DrawLayer = "background",
                Origin = [0, 0, 0], RotationDegrees = 0,
                BboxMin = [0, 0, 0], BboxMax = [48000, 32000, 0],
                ImportUnits = "Millimeter",
                LayerCount = 3, PrimitiveCount = 288,
                Layers =
                [
                    new CadLayerSummary { Name = "A-DOOR", Color = "#FFFF00", PrimitiveCount = 36, Primitives = new Dictionary<string, int> { ["block"] = 36 } },
                    new CadLayerSummary { Name = "A-GRID", Color = "#FF0000", PrimitiveCount = 12, Primitives = new Dictionary<string, int> { ["line"] = 12 } },
                    new CadLayerSummary { Name = "A-WALL", Color = "#FFFFFF", PrimitiveCount = 240, Primitives = new Dictionary<string, int> { ["polyline"] = 240 } },
                ],
                Summary = "Linked DWG 'A-101.dwg' on view 'EG Grundriss' only, 48 × 32 m, 3 layers, 288 primitives (busiest: A-WALL 240, A-DOOR 36, A-GRID 12).",
            },
            new UnderlayInfo
            {
                Id = 5120, Name = "Fassade.pdf", Kind = "pdf", Source = "link",
                FilePath = @"C:\Projekt\PDF\Fassade.pdf", FileStatus = "loaded",
                ViewSpecific = true, OwnerViewId = 420, OwnerViewName = "Fassade Nord", OwnerViewType = "Elevation", OwnerViewScale = 100,
                Pinned = false, DrawLayer = "background", Origin = [0, 0, 0],
                Image = new UnderlayImageInfo
                {
                    PageNumber = 2, ResolutionDpi = 300,
                    PaperWidthMm = 594, PaperHeightMm = 420, PlacedWidthMm = 59400, PlacedHeightMm = 42000,
                    Scale = 100, LockProportions = true,
                },
                Summary = "PDF 'Fassade.pdf' on view 'Fassade Nord', page 2, 300 DPI, 594 × 420 mm, placed at 1:100, not pinned.",
            },
            new UnderlayInfo
            {
                Id = 912, Name = "TGA-Schacht.dwg", Kind = "dwg", Source = "import", FileStatus = "imported",
                LinkInstanceId = 8800, LinkName = "TGA.rvt",
                ViewSpecific = false, LevelId = 77, LevelName = "EG",
                Origin = [60000, 0, 0], RotationDegrees = 0, BboxMin = [60000, 0, 0], BboxMax = [64000, 3000, 0],
                LayerCount = 1, PrimitiveCount = 4,
                Layers = [new CadLayerSummary { Name = "M-DUCT", PrimitiveCount = 4, Primitives = new Dictionary<string, int> { ["line"] = 4 } }],
                Summary = "Imported DWG 'TGA-Schacht.dwg' in Revit link 'TGA.rvt' model-wide on level 'EG', 4 × 3 m, 1 layers, 4 primitives (busiest: M-DUCT 4).",
            },
        ],
    };

    private static CadGeometryResult GridLines() => new()
    {
        ImportId = 4711, Name = "A-101.dwg", Count = 12, Returned = 2,
        Primitives =
        [
            new CadPrimitive { Type = "line", Layer = "A-GRID", Points = [[0, 0, 0], [0, 32000, 0]], Length = 32000 },
            new CadPrimitive { Type = "line", Layer = "A-GRID", Points = [[6000, 0, 0], [6000, 32000, 0]], Length = 32000 },
        ],
    };

    // A 1×1 PNG — enough to prove the bytes travel as an image block, untouched.
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static FakeBridge NewBridge()
    {
        FakeBridge bridge = new();
        foreach (string name in UnderlayCommands.Append("GetCadImports")) bridge.Commands.Add(Registered(name));
        bridge.OnInvoke = (command, _) => command switch
        {
            // JToken.FromObject with default settings: exactly what McpBridgeServer writes.
            "GetUnderlays" => (JToken.FromObject(Scenario()), null),
            "GetCadGeometry" => (JToken.FromObject(GridLines()), null),
            "GetPdfPageAsImage" => (JToken.FromObject(new PageImageResult
            {
                Id = 5120, Name = "Fassade.pdf", Kind = "pdf", PageNumber = 2, Width = 1, Height = 1, SourceWidth = 1, SourceHeight = 1,
                File = @"C:\Temp\AnalyseTool\underlays\Fassade-5120-p2.png",
                Image = new ImageAttachment { MimeType = "image/png", Data = OnePixelPng },
            }), null),
            _ => (null, new JObject { ["code"] = "unknown_command", ["message"] = command }),
        };
        return bridge.Start();
    }

    [Test]
    [MethodDataSource(nameof(Commands))]
    public async Task Output_schema_survives_the_listing_cap(string command)
    {
        // Over 4096 characters the bridge lists a free-form object instead: no outputSchema reaches the
        // client, no structured content, and none of the field descriptions an agent reads. A schema
        // that grew past the cap would silently undo the point of declaring it.
        string full = CommandDispatcher.BuildSchema(CommandType(command).GetCustomAttribute<RevitCommandAttribute>()!.OutputType);
        await Assert.That(SchemaListing.Compact(full)).IsNotEqualTo(SchemaListing.FreeFormObject)
            .Because($"{command}: the output schema is {full.Length} characters, over the listing cap");
    }

    public static IEnumerable<string> Commands() => UnderlayCommands;

    [Test, Timeout(60_000)]
    public async Task An_agent_finds_the_underlay_tools_by_their_descriptions(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();

        JObject list = await exe.RequestAsync("tools/list");
        Dictionary<string, JObject> byName = ((JArray)list["tools"]!).Cast<JObject>().ToDictionary(t => (string)t["name"]!);

        using (Assert.Multiple())
        {
            foreach (string command in UnderlayCommands)
            {
                await Assert.That(byName.Keys).Contains(command);
                await Assert.That((bool)byName[command]["annotations"]!["readOnlyHint"]!).IsTrue();
                await Assert.That(byName[command]["outputSchema"]).IsNotNull();
            }
            // The words a user says must be in the description of the tool to start with — the agent
            // needs no system prompt to connect "DWG", "PDF" or "underlay" to it.
            string description = (string)byName["GetUnderlays"]["description"]!;
            foreach (string word in new[] { "DWG", "PDF", "underlay", "layer", "START HERE" })
                await Assert.That(description).Contains(word);
            // …and the old inventory command points there instead of competing with it.
            await Assert.That((string)byName["GetCadImports"]["description"]!).Contains("GetUnderlays");
            // Field descriptions travel in the schema: the unit is not left to guess.
            await Assert.That(byName["GetUnderlays"]["outputSchema"]!.ToString()).Contains("mm / 304.8 = Revit feet");
        }
    }

    [Test, Timeout(60_000)]
    public async Task One_call_answers_what_is_in_the_dwg_and_the_pdf(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        JObject list = await exe.RequestAsync("tools/list");
        JObject tool = ((JArray)list["tools"]!).Cast<JObject>().Single(t => (string)t["name"]! == "GetUnderlays");

        JObject reply = await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetUnderlays", ["arguments"] = new JObject() });
        JToken structured = reply["structuredContent"]!;

        // What a validating client does with the answer: check it against the schema it was listed with.
        JsonSchema schema = await JsonSchema.FromJsonAsync(tool["outputSchema"]!.ToString());
        ICollection<NJsonSchema.Validation.ValidationError> errors = schema.Validate(structured.ToString());

        JToken dwg = structured["underlays"]!.Single(u => (string)u["name"]! == "A-101.dwg");
        JToken pdf = structured["underlays"]!.Single(u => (string)u["kind"]! == "pdf");
        using (Assert.Multiple())
        {
            await Assert.That((bool?)reply["isError"] ?? false).IsFalse();
            await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors.Select(e => $"{e.Kind} at {e.Path}")));
            await Assert.That((string)structured["units"]!).IsEqualTo("mm");

            // The DWG of the scenario: linked, where, how big, and which layer is which — in one answer.
            await Assert.That((string)dwg["source"]!).IsEqualTo("link");
            await Assert.That((string)dwg["ownerViewName"]!).IsEqualTo("EG Grundriss");
            await Assert.That((double)dwg["bboxMax"]![0]!).IsEqualTo(48000);
            JToken grid = dwg["layers"]!.Single(l => (string)l["name"]! == "A-GRID");
            await Assert.That((int)grid["primitives"]!["line"]!).IsEqualTo(12);
            await Assert.That((int)dwg["layers"]!.Single(l => (string)l["name"]! == "A-DOOR")["primitives"]!["block"]!).IsEqualTo(36);

            // The PDF: page, DPI, paper size, scale, not pinned.
            await Assert.That((int)pdf["image"]!["pageNumber"]!).IsEqualTo(2);
            await Assert.That((double)pdf["image"]!["resolutionDpi"]!).IsEqualTo(300);
            await Assert.That((double)pdf["image"]!["paperWidthMm"]!).IsEqualTo(594);
            await Assert.That((double)pdf["image"]!["scale"]!).IsEqualTo(100);
            await Assert.That((bool)pdf["pinned"]!).IsFalse();

            // A DWG inside a Revit link says so, and says how to reach it.
            JToken inLink = structured["underlays"]!.Single(u => (string)u["name"]! == "TGA-Schacht.dwg");
            await Assert.That((long)inLink["linkInstanceId"]!).IsEqualTo(8800);
            await Assert.That((string)inLink["linkName"]!).IsEqualTo("TGA.rvt");
            await Assert.That(dwg["linkInstanceId"]).IsNull();

            // Absent, not null: an optional field the host has no value for is left out.
            await Assert.That(pdf["layers"]).IsNull();
            await Assert.That((string)reply["content"]![0]!["text"]!).Contains("A-101.dwg");
        }
    }

    [Test, Timeout(60_000)]
    public async Task Geometry_answers_validate_and_say_when_they_are_truncated(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        JObject list = await exe.RequestAsync("tools/list");
        JObject tool = ((JArray)list["tools"]!).Cast<JObject>().Single(t => (string)t["name"]! == "GetCadGeometry");

        JObject reply = await exe.RequestAsync("tools/call", new JObject
        {
            ["name"] = "GetCadGeometry",
            ["arguments"] = new JObject { ["importId"] = 4711, ["layers"] = new JArray("A-GRID"), ["types"] = new JArray("line"), ["limit"] = 2, ["linkInstanceId"] = 8800 },
        });
        JToken structured = reply["structuredContent"]!;
        JsonSchema schema = await JsonSchema.FromJsonAsync(tool["outputSchema"]!.ToString());
        ICollection<NJsonSchema.Validation.ValidationError> errors = schema.Validate(structured.ToString());

        using (Assert.Multiple())
        {
            await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors.Select(e => $"{e.Kind} at {e.Path}")));
            await Assert.That((int)structured["count"]!).IsEqualTo(12);
            await Assert.That((int)structured["returned"]!).IsEqualTo(2);
            await Assert.That((double)structured["primitives"]![1]!["points"]![0]![0]!).IsEqualTo(6000);
            // The arguments reached Revit as the agent wrote them.
            JToken? payload = bridge.Invocations.Single(i => i.Command == "GetCadGeometry").Payload;
            await Assert.That((long)payload!["importId"]!).IsEqualTo(4711);
            await Assert.That((string)payload["layers"]![0]!).IsEqualTo("A-GRID");
            await Assert.That((long)payload["linkInstanceId"]!).IsEqualTo(8800);
        }
    }

    [Test, Timeout(60_000)]
    public async Task A_pdf_page_arrives_as_an_image_the_model_can_see(CancellationToken ct)
    {
        await using FakeBridge bridge = NewBridge();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        JObject list = await exe.RequestAsync("tools/list");
        JObject tool = ((JArray)list["tools"]!).Cast<JObject>().Single(t => (string)t["name"]! == "GetPdfPageAsImage");

        JObject reply = await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetPdfPageAsImage", ["arguments"] = new JObject { ["id"] = 5120 } });
        JArray content = (JArray)reply["content"]!;
        JToken? image = content.FirstOrDefault(c => (string)c["type"]! == "image");
        string text = (string)content.First(c => (string)c["type"]! == "text")["text"]!;
        JToken structured = reply["structuredContent"]!;
        JsonSchema schema = await JsonSchema.FromJsonAsync(tool["outputSchema"]!.ToString());
        ICollection<NJsonSchema.Validation.ValidationError> errors = schema.Validate(structured.ToString());

        using (Assert.Multiple())
        {
            await Assert.That(image).IsNotNull();
            await Assert.That((string)image!["mimeType"]!).IsEqualTo("image/png");
            await Assert.That((string)image["data"]!).IsEqualTo(OnePixelPng);
            // The base64 is not read twice as text: gone from the text block and the structured content,
            // which still say what the picture is.
            await Assert.That(text).DoesNotContain(OnePixelPng);
            await Assert.That(text).Contains("Fassade.pdf");
            await Assert.That(structured["image"]!["data"]).IsNull();
            await Assert.That((string)structured["image"]!["mimeType"]!).IsEqualTo("image/png");
            await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors.Select(e => $"{e.Kind} at {e.Path}")));
        }
    }

    [Test, Timeout(60_000)]
    public async Task A_result_that_only_looks_like_an_image_stays_json(CancellationToken ct)
    {
        await using FakeBridge bridge = new();
        bridge.Commands.Add(new("GetThing", "Returns a thing.", FakeBridge.EmptyInput, FakeBridge.EmptyInput));
        // Not an image mime type: the convention does not apply, nothing is lifted or removed.
        bridge.OnInvoke = (_, _) => (new JObject { ["image"] = new JObject { ["mimeType"] = "text/plain", ["data"] = "aGVsbG8=" } }, null);
        bridge.Start();
        await using McpExe exe = McpExe.Start(bridge.Port);
        await exe.InitializeAsync();
        await exe.RequestAsync("tools/list");

        JObject reply = await exe.RequestAsync("tools/call", new JObject { ["name"] = "GetThing", ["arguments"] = new JObject() });

        using (Assert.Multiple())
        {
            await Assert.That(((JArray)reply["content"]!).Count).IsEqualTo(1);
            await Assert.That((string)reply["content"]![0]!["text"]!).Contains("aGVsbG8=");
        }
    }
}
