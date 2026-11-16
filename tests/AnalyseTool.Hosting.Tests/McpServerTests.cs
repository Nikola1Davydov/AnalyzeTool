using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Mcp.Tools;
using AnalyseTool.Hosting.Mcp.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AnalyseTool.Hosting.Tests;

/// <summary>
///     The MCP server of AnalyseTool.Hosting.Mcp on a real port, called by the MCP client of the SDK.
/// </summary>
public sealed class McpServerTests
{
    /// <summary>
    ///     The two ways a client talks to the server: the <c>initialize</c> handshake with a session, and the session-less
    ///     revision of 2026, where every request stands alone.
    /// </summary>
    public const string HandshakeProtocolVersion = "2025-11-25";

    public const string SessionlessProtocolVersion = "2026-07-28";

    public static IEnumerable<string> ProtocolVersions()
    {
        yield return HandshakeProtocolVersion;
        yield return SessionlessProtocolVersion;
    }

    [Test]
    [MethodDataSource(nameof(ProtocolVersions))]
    public async Task ListTools_ReturnsOnlyTheMarkedCommands(string protocolVersion)
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand), typeof(McpRefuseCommand), typeof(GreetCommand));
        await using var client = await server.ConnectAsync(protocolVersion);

        var tools = await client.ListToolsAsync();

        // ListRevitVersions is the tool of the one address of every Revit version
        await Assert.That(tools.Select(tool => tool.Name)).IsEquivalentTo(["ListRevitVersions", "McpGreet", "McpRefuse"]);
    }

    [Test]
    [MethodDataSource(nameof(ProtocolVersions))]
    public async Task ListTools_DescribesTheToolFromTheCommand(string protocolVersion)
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand));
        await using var client = await server.ConnectAsync(protocolVersion);

        var tool = (await client.ListToolsAsync()).Single(item => item.Name == "McpGreet").ProtocolTool;

        await Assert.That(tool.Description).IsEqualTo("Greets by name over MCP.");
        await Assert.That(tool.Annotations?.ReadOnlyHint ?? false).IsTrue();
        await Assert.That(tool.InputSchema.GetProperty("properties").TryGetProperty("name", out _)).IsTrue();
        // Every tool can be sent to another open Revit version
        await Assert.That(tool.InputSchema.GetProperty("properties").TryGetProperty("revit", out _)).IsTrue();
    }

    [Test]
    public async Task CallTool_WithAnotherRevitVersion_RunsInThatRevit()
    {
        // Two Revit versions, one token: 2025 does not have McpPoint, 2026 does
        var connectionFile = McpTestServer.NewConnectionFile();
        await using var revit2025 = await McpTestServer.StartAsync(connectionFile, McpTestServer.FreePort(), "2025", typeof(McpGreetCommand));
        await using var revit2026 = await McpTestServer.StartAsync(connectionFile, McpTestServer.FreePort(), "2026", typeof(McpGreetCommand), typeof(McpPointCommand));
        await using var client = await revit2025.ConnectAsync(HandshakeProtocolVersion);

        var forwarded = await client.CallToolAsync("McpPoint", new Dictionary<string, object?> { ["revit"] = "2026" });
        var here = await client.CallToolAsync("McpGreet", new Dictionary<string, object?> { ["name"] = "Revit", ["revit"] = "2025" });
        var closed = await client.CallToolAsync("McpGreet", new Dictionary<string, object?> { ["name"] = "Revit", ["revit"] = "2027" });
        var versions = await client.CallToolAsync("ListRevitVersions", new Dictionary<string, object?>());

        using (Assert.Multiple())
        {
            await Assert.That(forwarded.IsError ?? false).IsFalse();
            await Assert.That(TextOf(forwarded)).Contains("\"x\"");
            await Assert.That(TextOf(here)).IsEqualTo("\"Hello, Revit (mcp)\"");
            await Assert.That(closed.IsError ?? false).IsTrue();
            await Assert.That(TextOf(closed)).StartsWith("revit_not_open");
            await Assert.That(TextOf(versions)).Contains("2025");
            await Assert.That(TextOf(versions)).Contains("2026");
        }
    }

    [Test]
    [MethodDataSource(nameof(ProtocolVersions))]
    public async Task CallTool_RunsTheCommandThroughThePipeline(string protocolVersion)
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand));
        await using var client = await server.ConnectAsync(protocolVersion);

        var result = await client.CallToolAsync("McpGreet", new Dictionary<string, object?> { ["name"] = "Revit" });

        await Assert.That(result.IsError ?? false).IsFalse();
        await Assert.That(TextOf(result)).IsEqualTo("\"Hello, Revit (mcp)\"");
    }

    [Test]
    [MethodDataSource(nameof(ProtocolVersions))]
    public async Task CallTool_ObjectResult_IsAlsoStructuredContent(string protocolVersion)
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpPointCommand));
        await using var client = await server.ConnectAsync(protocolVersion);

        var result = await client.CallToolAsync("McpPoint");

        await Assert.That(result.StructuredContent?.GetProperty("x").GetInt32()).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(nameof(ProtocolVersions))]
    public async Task CallTool_ImageResult_IsAnImageWithItsCaption(string protocolVersion)
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpPictureCommand));
        await using var client = await server.ConnectAsync(protocolVersion);

        var result = await client.CallToolAsync("McpPicture");

        var image = result.Content.OfType<ImageContentBlock>().Single();
        await Assert.That(image.MimeType).IsEqualTo("image/png");
        await Assert.That(image.DecodedData.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(TextOf(result)).IsEqualTo("Three bytes.");
    }

    [Test]
    [MethodDataSource(nameof(ProtocolVersions))]
    public async Task CallTool_FailedCommand_IsAToolErrorWithItsCode(string protocolVersion)
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpRefuseCommand));
        await using var client = await server.ConnectAsync(protocolVersion);

        var result = await client.CallToolAsync("McpRefuse");

        await Assert.That(result.IsError ?? false).IsTrue();
        await Assert.That(TextOf(result)).IsEqualTo("refused: Not over MCP.");
    }

    [Test]
    [MethodDataSource(nameof(ProtocolVersions))]
    public async Task CallTool_UnmarkedCommand_IsRefused(string protocolVersion)
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand), typeof(GreetCommand));
        await using var client = await server.ConnectAsync(protocolVersion);

        await Assert.ThrowsAsync<McpException>(async () => await client.CallToolAsync("Greet", new Dictionary<string, object?> { ["name"] = "Revit" }));
    }

    [Test]
    public async Task CallTool_SendsTheProgressBeforeTheResult()
    {
        // Over raw HTTP, not the SDK client: that client handles every incoming message as a task of its own, so the
        // progress may reach IProgress after CallToolAsync returned — and get dropped. The server's part is the order of
        // the events on the wire.
        await using var server = await McpTestServer.StartAsync(typeof(McpProgressCommand));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {server.Token}");
        http.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");

        using var initialize = await http.PostAsync(server.Url, InitializeBody());
        http.DefaultRequestHeaders.Add("Mcp-Session-Id", initialize.Headers.GetValues("Mcp-Session-Id").Single());
        http.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-06-18");
        using var initialized = await http.PostAsync(server.Url, Json("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));

        using var call = await http.PostAsync(server.Url, Json(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"McpProgress","arguments":{},"_meta":{"progressToken":"p1"}}}"""));
        var events = await call.Content.ReadAsStringAsync();

        var progress = events.IndexOf("notifications/progress", StringComparison.Ordinal);
        var result = events.IndexOf("\"result\"", StringComparison.Ordinal);
        await Assert.That(progress).IsGreaterThanOrEqualTo(0);
        await Assert.That(progress).IsLessThan(result);
    }

    [Test]
    public async Task CallTool_SdkInformationRecords_StayOutOfTheLog()
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand));
        await using (var client = await server.ConnectAsync(HandshakeProtocolVersion))
        {
            await client.CallToolAsync("McpGreet", new Dictionary<string, object?> { ["name"] = "Revit" });
        }

        var log = server.Log.Entries.Select(entry => entry.Message).ToList();
        await Assert.That(log).Contains(message => message.Contains("McpGreet", StringComparison.Ordinal));
        await Assert.That(log).DoesNotContain(message => message.Contains("request handler", StringComparison.Ordinal));
    }

    [Test]
    public async Task Request_WithoutToken_IsUnauthorized()
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand));
        using var http = new HttpClient();

        using var response = await http.PostAsync(server.Url, InitializeBody());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Request_FromABrowserPage_IsForbidden()
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand));
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Url) { Content = InitializeBody() };
        request.Headers.Add("Authorization", $"Bearer {server.Token}");
        request.Headers.Add("Origin", "https://example.com");

        using var response = await http.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Start_KeepsTheTokenOfTheConnectionFile()
    {
        var connectionFile = Path.Combine(Path.GetTempPath(), $"mcp-{Guid.NewGuid():N}.json");
        string first;
        await using (var server = await McpTestServer.StartAsync(connectionFile, typeof(McpGreetCommand)))
        {
            first = server.Token;
        }

        await using var again = await McpTestServer.StartAsync(connectionFile, typeof(McpGreetCommand));

        await Assert.That(again.Token).IsEqualTo(first);
    }

    [Test]
    public async Task Start_BusyPort_RunsWithoutMcp()
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand));
        await using var second = await McpTestServer.StartAsync(McpTestServer.NewConnectionFile(), server.Port, typeof(McpGreetCommand));

        await Assert.That(second.Server.Url).IsNull();
    }

    [Test]
    public async Task Start_Disabled_DoesNotListen()
    {
        using var host = McpTestServer.BuildHost(McpTestServer.NewConnectionFile(), McpTestServer.FreePort(), enabled: false, typeof(McpGreetCommand));

        await host.StartAsync();

        await Assert.That(host.Services.GetRequiredService<McpHttpServer>().Url).IsNull();
        await host.StopAsync();
    }

    [Test]
    public async Task Start_ToolWithoutDescription_Fails()
    {
        using var host = McpTestServer.BuildHost(McpTestServer.NewConnectionFile(), McpTestServer.FreePort(), enabled: false, typeof(McpUndescribedCommand), typeof(McpScalarRequestCommand));

        var exception = await Assert.ThrowsAsync<McpToolException>(async () => await host.StartAsync());

        await Assert.That(exception!.Errors.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetInfo_Listening_GivesTheConnectionDetailsAndTools()
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand), typeof(GreetCommand));

        var info = server.Server.GetInfo();

        await Assert.That(info.State).IsEqualTo(McpEndpointState.Listening);
        await Assert.That(info.Url).IsEqualTo(server.Url);
        await Assert.That(info.Token).IsEqualTo(server.Token);
        await Assert.That(info.ClaudeCode).Contains($"Bearer {server.Token}");
        await Assert.That(info.Tools.Select(tool => tool.Name)).IsEquivalentTo(["McpGreet"]);
    }

    [Test]
    public async Task GetInfo_PortBusy_HasNoToken()
    {
        await using var server = await McpTestServer.StartAsync(typeof(McpGreetCommand));
        await using var second = await McpTestServer.StartAsync(McpTestServer.NewConnectionFile(), server.Port, typeof(McpGreetCommand));

        var info = second.Server.GetInfo();

        await Assert.That(info.State).IsEqualTo(McpEndpointState.PortBusy);
        await Assert.That(info.Token).IsNull();
    }

    private static string TextOf(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }

    private static StringContent InitializeBody()
    {
        return Json("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");
    }

    private static StringContent Json(string body)
    {
        return new StringContent(body, Encoding.UTF8, "application/json");
    }
}

/// <summary>A started host with the MCP server on a free port.</summary>
internal sealed class McpTestServer : IAsyncDisposable
{
    private readonly IHost _host;

    private McpTestServer(IHost host, int port, string connectionFile)
    {
        _host = host;
        Port = port;
        Server = host.Services.GetRequiredService<McpHttpServer>();
        Log = host.Services.GetRequiredService<LogRecorder>();
        Url = new Uri($"http://localhost:{port}/mcp");
        Token = File.Exists(connectionFile) ? JsonDocument.Parse(File.ReadAllText(connectionFile)).RootElement.GetProperty("token").GetString()! : "";
    }

    public int Port { get; }

    public Uri Url { get; }

    public string Token { get; }

    public McpHttpServer Server { get; }

    public LogRecorder Log { get; }

    public static Task<McpTestServer> StartAsync(params Type[] commands)
    {
        return StartAsync(NewConnectionFile(), FreePort(), commands);
    }

    public static Task<McpTestServer> StartAsync(string connectionFile, params Type[] commands)
    {
        return StartAsync(connectionFile, FreePort(), commands);
    }

    public static Task<McpTestServer> StartAsync(string connectionFile, int port, params Type[] commands)
    {
        return StartAsync(connectionFile, port, "2026", commands);
    }

    public static async Task<McpTestServer> StartAsync(string connectionFile, int port, string revitVersion, params Type[] commands)
    {
        var host = BuildHost(connectionFile, port, enabled: true, revitVersion, commands);
        await host.StartAsync();
        return new McpTestServer(host, port, connectionFile);
    }

    public static IHost BuildHost(string connectionFile, int port, bool enabled, params Type[] commands)
    {
        return BuildHost(connectionFile, port, enabled, "2026", commands);
    }

    public static IHost BuildHost(string connectionFile, int port, bool enabled, string revitVersion, params Type[] commands)
    {
        var builder = TestHost.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = enabled.ToString(),
            ["Mcp:Port"] = port.ToString()
        });
        builder.AddRevitCommands(configure => configure.FromTypes(commands).UseCommandLogging());
        builder.AddMcp(new McpHostSettings(revitVersion, port, connectionFile));
        return builder.Build();
    }

    public static string NewConnectionFile()
    {
        // A folder of its own: the servers of a test see each other in mcp\ next to the file, and only those
        return Path.Combine(Path.GetTempPath(), $"mcp-{Guid.NewGuid():N}", "mcp.json");
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task<McpClient> ConnectAsync(string protocolVersion)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = Url,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {Token}" }
        });

        var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion });

        // The handshake revision must get a session, the 2026 revision must not: each test covers the path it names
        var expectsSession = protocolVersion == McpServerTests.HandshakeProtocolVersion;
        if ((client.SessionId is not null) != expectsSession)
        {
            throw new InvalidOperationException($"Protocol {protocolVersion}: session '{client.SessionId}', expected {(expectsSession ? "one" : "none")}.");
        }

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
