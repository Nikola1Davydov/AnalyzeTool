using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Mcp;
using AnalyseTool.Mcp.Bridge;
using Newtonsoft.Json.Linq;
using System.Net.Sockets;
using System.Text;

namespace AnalyseTool.Tests;

/// <summary>The real in-Revit bridge, over a real socket, without Revit: a dispatcher with no hub answers
/// everything that does not execute a command (version, list, auth). Until the IRevitTaskHub seam the
/// bridge could not be built outside Revit, and only the exe was tested, against a fake.</summary>
public class BridgeServerTests
{
    private const string Token = "test-token";

    private static McpBridgeServer Start(TimeSpan? idle = null, int maxConnections = 32)
    {
        McpBridgeServer server = new(new CommandQueue(new CommandDispatcher(null!)), Token)
        {
            IdleTimeout = idle ?? TimeSpan.FromSeconds(30),
            MaxConnections = maxConnections,
        };
        server.Start(0); // any free port
        return server;
    }

    private static async Task<TcpClient> ConnectAsync(McpBridgeServer server)
    {
        TcpClient client = new();
        await client.ConnectAsync("127.0.0.1", server.Port);
        return client;
    }

    /// <summary>True when the server closed the connection (a read returns 0) within the deadline.</summary>
    private static async Task<bool> ClosedByServerAsync(TcpClient client, TimeSpan within)
    {
        using CancellationTokenSource deadline = new(within);
        try
        {
            int read = await client.GetStream().ReadAsync(new byte[16], deadline.Token);
            return read == 0;
        }
        catch (IOException) { return true; } // reset
        catch (OperationCanceledException) { return false; }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
    }

    [Test, Timeout(30_000)]
    public async Task An_idle_connection_is_closed(CancellationToken ct)
    {
        McpBridgeServer server = Start(idle: TimeSpan.FromMilliseconds(300));
        try
        {
            using TcpClient silent = await ConnectAsync(server);
            await Assert.That(await ClosedByServerAsync(silent, TimeSpan.FromSeconds(5))).IsTrue();
        }
        finally { server.Stop(); }
    }

    [Test, Timeout(30_000)]
    public async Task A_connection_over_the_cap_is_closed_on_arrival(CancellationToken ct)
    {
        McpBridgeServer server = Start(maxConnections: 2);
        try
        {
            using TcpClient first = await ConnectAsync(server);
            using TcpClient second = await ConnectAsync(server);
            await WaitForAsync(() => server.OpenConnections == 2);

            using TcpClient third = await ConnectAsync(server);

            using (Assert.Multiple())
            {
                await Assert.That(await ClosedByServerAsync(third, TimeSpan.FromSeconds(5))).IsTrue();
                await Assert.That(await ClosedByServerAsync(first, TimeSpan.FromMilliseconds(200))).IsFalse();
            }
        }
        finally { server.Stop(); }
    }

    [Test, Timeout(30_000)]
    public async Task Two_requests_in_one_write_get_two_replies(CancellationToken ct)
    {
        McpBridgeServer server = Start();
        try
        {
            using TcpClient client = await ConnectAsync(server);
            NetworkStream stream = client.GetStream();
            string both = new JObject { [McpWire.Id] = "1", [McpWire.Token] = Token, [McpWire.Type] = McpWire.TypeVersion }.ToString(Newtonsoft.Json.Formatting.None)
                        + new JObject { [McpWire.Id] = "2", [McpWire.Token] = Token, [McpWire.Type] = McpWire.TypeVersion }.ToString(Newtonsoft.Json.Formatting.None);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(both));

            JsonFrameReader replies = new(stream, 1024 * 1024);
            JObject one = JObject.Parse((await replies.ReadFrameAsync(CancellationToken.None))!);
            JObject two = JObject.Parse((await replies.ReadFrameAsync(CancellationToken.None))!);

            using (Assert.Multiple())
            {
                await Assert.That((string?)one[McpWire.Id]).IsEqualTo("1");
                await Assert.That((string?)two[McpWire.Id]).IsEqualTo("2");
                await Assert.That((string?)two[McpWire.Result]?[McpWire.Catalog]).IsNotNull();
            }
        }
        finally { server.Stop(); }
    }

    [Test, Timeout(30_000)]
    public async Task A_request_without_the_token_is_refused(CancellationToken ct)
    {
        McpBridgeServer server = Start();
        try
        {
            using TcpClient client = await ConnectAsync(server);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"id\":\"x\",\"type\":\"list\"}"));

            JObject reply = JObject.Parse((await new JsonFrameReader(stream, 1024 * 1024).ReadFrameAsync(CancellationToken.None))!);
            await Assert.That((string?)reply[McpWire.Error]?[McpWire.ErrorCode]).IsEqualTo(McpWire.Codes.Unauthorized);
        }
        finally { server.Stop(); }
    }
}
