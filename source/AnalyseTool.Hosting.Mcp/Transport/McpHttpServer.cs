using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AnalyseTool.Hosting.Mcp.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AnalyseTool.Hosting.Mcp.Transport;

/// <summary>
///     Where the server listens and where it writes <c>mcp.json</c>, fixed by <c>AddMcp</c>.
/// </summary>
/// <param name="RevitVersion">The Revit version number, <c>2026</c>.</param>
/// <param name="BasePort">The port of the first supported Revit version; later versions add their offset.</param>
/// <param name="ConnectionFile">The path of <c>mcp.json</c>.</param>
internal sealed record McpHostSettings(string RevitVersion, int BasePort, string ConnectionFile)
{
    /// <summary>The first Revit version of the template: its port is the base port.</summary>
    public const int FirstRevitVersion = 2025;

    public int GetPort(McpOptions options)
    {
        if (options.Port is { } port)
        {
            return port;
        }

        return int.TryParse(RevitVersion, out var version) && version > FirstRevitVersion ? BasePort + version - FirstRevitVersion : BasePort;
    }
}

/// <summary>
///     The MCP endpoint over Streamable HTTP on <c>http://localhost:&lt;port&gt;/mcp</c>, on <see cref="HttpListener" />:
///     Revit has no ASP.NET Core, so this class does what its MCP package does — sessions and the HTTP rules — and the
///     protocol itself is <see cref="StreamableHttpServerTransport" /> and <see cref="McpServer" /> of the SDK.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>Only <c>localhost</c>; every request carries the token of <c>mcp.json</c>; a request with an <c>Origin</c> header — a
///         browser page — is refused, so a web site cannot reach the model through the user's browser.</item>
///         <item>A client with the <c>initialize</c> handshake gets a session (<c>Mcp-Session-Id</c>); a request without a
///         session — the session-less protocol revision — gets a server of its own for that request.</item>
///         <item>A busy port (a second Revit of the same version) is a warning, not a failure: the add-in runs without MCP.</item>
///     </list>
/// </remarks>
internal sealed partial class McpHttpServer(
    McpHostSettings settings,
    IOptions<McpOptions> options,
    McpToolCatalog catalog,
    McpTools tools,
    IHostEnvironment environment,
    IServiceProvider services,
    ILoggerFactory loggerFactory) : IHostedService, IMcpEndpoint, IAsyncDisposable
{
    private const string EndpointPath = "/mcp";
    private const string SessionIdHeader = "Mcp-Session-Id";
    private const string ProtocolVersionHeader = "MCP-Protocol-Version";

    private static readonly JsonTypeInfo MessageTypeInfo = McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage));
    private static readonly JsonTypeInfo ErrorTypeInfo = McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcError));

    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ILogger _logger = loggerFactory.CreateLogger<McpHttpServer>();
    private readonly SdkLoggerFactory _sdkLoggerFactory = new(loggerFactory, options.Value.SdkLogLevel);
    private HttpListener? _listener;
    private Task? _acceptLoop;
    private byte[] _authorization = [];
    private McpEndpointState _state = McpEndpointState.Off;
    private McpConnectionFile? _connection;

    /// <summary>Gets the address of the endpoint once the server listens, or <see langword="null" />.</summary>
    public Uri? Url { get; private set; }

    private Uri ExpectedUrl => new($"http://localhost:{settings.GetPort(options.Value)}{EndpointPath}");

    private string Name => $"{environment.ApplicationName.Replace('.', '-').ToLowerInvariant()}-{settings.RevitVersion}";

    /// <inheritdoc />
    public McpEndpointInfo GetInfo()
    {
        var connection = _state == McpEndpointState.Listening ? _connection : null;
        var toolInfos = catalog.Tools
            .Select(tool => new McpToolInfo(tool.Name, tool.Description, tool.Annotations?.ReadOnlyHint ?? false))
            .ToList();

        return new McpEndpointInfo(_state, Url ?? ExpectedUrl, Name, connection?.Token, connection?.ClaudeCode, settings.ConnectionFile, toolInfos);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!(options.Value.Enabled ?? environment.IsDevelopment()))
        {
            LogDisabled(_logger);
            return Task.CompletedTask;
        }

        var port = settings.GetPort(options.Value);
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException exception)
        {
            LogCannotListen(_logger, exception, port);
            listener.Close();
            _state = McpEndpointState.PortBusy;
            return Task.CompletedTask;
        }

        _listener = listener;
        Url = ExpectedUrl;

        var connection = McpConnectionFile.Write(settings.ConnectionFile, Url.ToString(), Name);
        _authorization = Encoding.UTF8.GetBytes($"Bearer {connection.Token}");
        _connection = connection;
        _state = McpEndpointState.Listening;

        _acceptLoop = Task.Run(AcceptLoopAsync, CancellationToken.None);
        LogListening(_logger, Url, settings.ConnectionFile);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            return;
        }

        _state = McpEndpointState.Off;
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        _listener.Close();
        _listener = null;

        if (_acceptLoop is not null)
        {
            await _acceptLoop.ConfigureAwait(false);
        }

        foreach (var id in _sessions.Keys)
        {
            if (_sessions.TryRemove(id, out var session))
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener!.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stopping.IsCancellationRequested)
            {
                // Stop() ends the pending GetContextAsync with an exception
                break;
            }
            catch (HttpListenerException exception)
            {
                LogAcceptFailed(_logger, exception);
                continue;
            }

            _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (!string.Equals(request.Url?.AbsolutePath.TrimEnd('/'), EndpointPath, StringComparison.OrdinalIgnoreCase))
            {
                Reply(response, HttpStatusCode.NotFound);
            }
            else if (!string.IsNullOrEmpty(request.Headers["Origin"]))
            {
                LogBrowserRefused(_logger, request.Headers["Origin"]);
                Reply(response, HttpStatusCode.Forbidden);
            }
            else if (!IsAuthorized(request))
            {
                response.AddHeader("WWW-Authenticate", "Bearer");
                Reply(response, HttpStatusCode.Unauthorized);
            }
            else
            {
                switch (request.HttpMethod)
                {
                    case "POST":
                        await HandlePostAsync(request, response).ConfigureAwait(false);
                        break;
                    case "GET":
                        await HandleGetAsync(request, response).ConfigureAwait(false);
                        break;
                    case "DELETE":
                        await HandleDeleteAsync(request, response).ConfigureAwait(false);
                        break;
                    default:
                        response.AddHeader("Allow", "GET, POST, DELETE");
                        Reply(response, HttpStatusCode.MethodNotAllowed);
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or OperationCanceledException)
        {
            // The client went away or Revit is closing
            LogRequestAborted(_logger, exception.Message);
        }
        catch (Exception exception)
        {
            LogRequestFailed(_logger, exception, request.HttpMethod);
            TryReply(response, HttpStatusCode.InternalServerError);
        }
        finally
        {
            TryClose(response);
        }
    }

    private async Task HandlePostAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        JsonRpcMessage? message;
        try
        {
            message = (JsonRpcMessage?)await JsonSerializer.DeserializeAsync(request.InputStream, MessageTypeInfo, _stopping.Token).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            await ReplyErrorAsync(response, HttpStatusCode.BadRequest, McpErrorCode.InvalidRequest, "The body is not a JSON-RPC message.").ConfigureAwait(false);
            return;
        }

        if (request.Headers[ProtocolVersionHeader] is { Length: > 0 } protocolVersion)
        {
            message.Context ??= new JsonRpcMessageContext();
            message.Context.ProtocolVersion = protocolVersion;
        }

        var requestId = message is JsonRpcRequest jsonRpcRequest ? jsonRpcRequest.Id : default;
        var sessionId = request.Headers[SessionIdHeader];

        if (!string.IsNullOrEmpty(sessionId))
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                // The client starts over with initialize, as the protocol asks
                await ReplyErrorAsync(response, HttpStatusCode.NotFound, McpErrorCode.InvalidRequest, "Session not found.", requestId).ConfigureAwait(false);
                return;
            }

            response.AddHeader(SessionIdHeader, session.Id);
            await PostAsync(session.Transport, message, response, session.Closing).ConfigureAwait(false);
        }
        else if (message is JsonRpcRequest { Method: RequestMethods.Initialize })
        {
            var session = StartSession();
            response.AddHeader(SessionIdHeader, session.Id);
            await PostAsync(session.Transport, message, response, session.Closing).ConfigureAwait(false);
        }
        else
        {
            await PostStatelessAsync(message, response).ConfigureAwait(false);
        }
    }

    private async Task HandleGetAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (await FindSessionAsync(request, response).ConfigureAwait(false) is not { } session)
        {
            return;
        }

        // The stream of server-initiated messages lives until the session closes or the client goes away
        response.AddHeader(SessionIdHeader, session.Id);
        StartEventStream(response);
        await session.Transport.HandleGetRequestAsync(response.OutputStream, session.Closing).ConfigureAwait(false);
    }

    private async Task HandleDeleteAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (await FindSessionAsync(request, response).ConfigureAwait(false) is not { } session)
        {
            return;
        }

        if (_sessions.TryRemove(session.Id, out _))
        {
            await session.DisposeAsync().ConfigureAwait(false);
            LogSessionClosed(_logger, session.Id);
        }

        Reply(response, HttpStatusCode.OK);
    }

    private async Task<Session?> FindSessionAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        var sessionId = request.Headers[SessionIdHeader];
        if (string.IsNullOrEmpty(sessionId))
        {
            await ReplyErrorAsync(response, HttpStatusCode.BadRequest, McpErrorCode.InvalidRequest, $"The {SessionIdHeader} header is required.").ConfigureAwait(false);
            return null;
        }

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            await ReplyErrorAsync(response, HttpStatusCode.NotFound, McpErrorCode.InvalidRequest, "Session not found.").ConfigureAwait(false);
            return null;
        }

        return session;
    }

    private Session StartSession()
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
#pragma warning disable MCP9006 // The session properties of the transport are obsolete in the SDK, but the initialize handshake needs them
        var transport = new StreamableHttpServerTransport(_sdkLoggerFactory) { SessionId = id };
#pragma warning restore MCP9006
        var server = McpServer.Create(transport, CreateServerOptions(), _sdkLoggerFactory, services);
        var closing = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        var session = new Session(id, transport, server, server.RunAsync(closing.Token), closing);

        _sessions[id] = session;
        LogSessionStarted(_logger, id);
        return session;
    }

    /// <summary>A request without a session: a server lives for this request only.</summary>
    private async Task PostStatelessAsync(JsonRpcMessage message, HttpListenerResponse response)
    {
        await using var transport = new StreamableHttpServerTransport(_sdkLoggerFactory) { Stateless = true };
        await using var server = McpServer.Create(transport, CreateServerOptions(), _sdkLoggerFactory, services);
        using var closing = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);

        var running = server.RunAsync(closing.Token);
        try
        {
            await PostAsync(transport, message, response, closing.Token).ConfigureAwait(false);
        }
        finally
        {
            await closing.CancelAsync().ConfigureAwait(false);
            await Session.ObserveAsync(running).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Hands a message to the transport. A request gets its response — and progress on the way — as a server-sent event
    ///     stream; a notification or a response gets <c>202 Accepted</c> and no body.
    /// </summary>
    private static async Task PostAsync(StreamableHttpServerTransport transport, JsonRpcMessage message, HttpListenerResponse response, CancellationToken cancellationToken)
    {
        StartEventStream(response);
        var wroteResponse = await transport.HandlePostRequestAsync(message, response.OutputStream, cancellationToken).ConfigureAwait(false);
        if (!wroteResponse)
        {
            response.ContentType = null;
            response.SendChunked = false;
            Reply(response, HttpStatusCode.Accepted);
        }
    }

    private McpServerOptions CreateServerOptions()
    {
        return new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = environment.ApplicationName,
                Title = $"{environment.ApplicationName} (Revit {settings.RevitVersion})",
                Version = typeof(McpHttpServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0"
            },
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = false } },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = tools.ListToolsAsync,
                CallToolHandler = tools.CallToolAsync
            }
        };
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        return header is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header), _authorization);
    }

    private static void StartEventStream(HttpListenerResponse response)
    {
        response.ContentType = "text/event-stream";
        response.AddHeader("Cache-Control", "no-cache,no-store");
        response.SendChunked = true;
    }

    private static void Reply(HttpListenerResponse response, HttpStatusCode status)
    {
        response.StatusCode = (int)status;
        response.ContentLength64 = 0;
    }

    private static async Task ReplyErrorAsync(HttpListenerResponse response, HttpStatusCode status, McpErrorCode code, string message, RequestId requestId = default)
    {
        var error = new JsonRpcError { Id = requestId, Error = new JsonRpcErrorDetail { Code = (int)code, Message = message } };
        var body = JsonSerializer.SerializeToUtf8Bytes(error, ErrorTypeInfo);

        response.StatusCode = (int)status;
        response.ContentType = "application/json";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
    }

    private static void TryReply(HttpListenerResponse response, HttpStatusCode status)
    {
        try
        {
            Reply(response, status);
        }
        catch (InvalidOperationException)
        {
            // The headers are already sent
        }
    }

    private static void TryClose(HttpListenerResponse response)
    {
        try
        {
            response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // The client went away
        }
    }

    [LoggerMessage(LogLevel.Information, "MCP server is off; turn it on with Mcp:Enabled")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(LogLevel.Information, "MCP server listens on {Url}; connection details in {ConnectionFile}")]
    private static partial void LogListening(ILogger logger, Uri url, string connectionFile);

    [LoggerMessage(LogLevel.Warning, "MCP server cannot listen on port {Port} — another Revit of this version may use it; the add-in runs without MCP")]
    private static partial void LogCannotListen(ILogger logger, Exception exception, int port);

    [LoggerMessage(LogLevel.Warning, "MCP: a request from a browser page ({Origin}) was refused")]
    private static partial void LogBrowserRefused(ILogger logger, string? origin);

    [LoggerMessage(LogLevel.Debug, "MCP session {SessionId} started")]
    private static partial void LogSessionStarted(ILogger logger, string sessionId);

    [LoggerMessage(LogLevel.Debug, "MCP session {SessionId} closed")]
    private static partial void LogSessionClosed(ILogger logger, string sessionId);

    [LoggerMessage(LogLevel.Debug, "MCP request aborted: {Reason}")]
    private static partial void LogRequestAborted(ILogger logger, string reason);

    [LoggerMessage(LogLevel.Error, "MCP {Method} request failed")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, string method);

    [LoggerMessage(LogLevel.Warning, "MCP server could not accept a request")]
    private static partial void LogAcceptFailed(ILogger logger, Exception exception);

    /// <summary>A client session of the <c>initialize</c> handshake: its transport, its server and the server's run.</summary>
    private sealed class Session(
        string id,
        StreamableHttpServerTransport transport,
        McpServer server,
        Task running,
        CancellationTokenSource closing) : IAsyncDisposable
    {
        public string Id { get; } = id;

        public StreamableHttpServerTransport Transport { get; } = transport;

        public CancellationToken Closing { get; } = closing.Token;

        public async ValueTask DisposeAsync()
        {
            await closing.CancelAsync().ConfigureAwait(false);
            await server.DisposeAsync().ConfigureAwait(false);
            await Transport.DisposeAsync().ConfigureAwait(false);
            await ObserveAsync(running).ConfigureAwait(false);
            closing.Dispose();
        }

        /// <summary>Waits for a server run that was cancelled; its cancellation is the expected end.</summary>
        public static async Task ObserveAsync(Task running)
        {
            try
            {
                await running.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
