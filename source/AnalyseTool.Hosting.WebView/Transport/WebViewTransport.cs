using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.WebView.ClientApp;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace AnalyseTool.Hosting.WebView.Transport;

/// <summary>
///     A message of the page ↔ host protocol: request, cancel, progress and response, matched by <c>id</c>.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>page → host: <c>{ type: "request", id, command, headers?, payload? }</c>, <c>{ type: "cancel", id }</c>;</item>
///         <item>host → page: <c>{ type: "progress", id, payload: { fraction, message } }</c>, <c>{ type: "response", id, payload?, error? }</c>.</item>
///     </list>
///     The TypeScript side is <c>revit-bridge/src/protocol.ts</c>; keep both in step.
/// </remarks>
internal sealed record WebMessage(
    string Type,
    string? Id,
    string? Command = null,
    Dictionary<string, string>? Headers = null,
    JsonElement? Payload = null,
    CommandError? Error = null);

/// <summary>
///     Connects one WebView to the command pipeline.
/// </summary>
/// <remarks>
///     <para>
///         Lives on the UI thread of its WebView: <c>WebMessageReceived</c> arrives there, and every reply is posted there.
///         The pipeline itself runs on the thread pool (<see cref="ICommandExecutor" />).
///     </para>
///     <para>
///         With the navigation protection on — the default, <paramref name="allowedSource" /> set — messages from any origin but
///         <see cref="ClientAppSource.Origin" /> are dropped. Posting to a WebView closed in the middle of a command never throws.
///     </para>
/// </remarks>
internal sealed partial class WebViewTransport(
    CoreWebView2 core,
    Dispatcher dispatcher,
    ICommandExecutor executor,
    ClientAppSource? allowedSource,
    JsonSerializerOptions serializerOptions,
    ILogger logger)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _calls = new();

    public void Attach()
    {
        core.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Stops listening and cancels the calls still running: their window is gone.</summary>
    public void Detach()
    {
        core.WebMessageReceived -= OnWebMessageReceived;

        foreach (var id in _calls.Keys)
        {
            if (_calls.TryRemove(id, out var cancellation))
            {
                cancellation.Cancel();
            }
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        // async void: nothing may escape into the WPF dispatcher of Revit
        try
        {
            if (allowedSource is not null && !allowedSource.IsOwn(args.Source))
            {
                LogForeignMessage(logger, args.Source);
                return;
            }

            var message = JsonSerializer.Deserialize<WebMessage>(args.WebMessageAsJson, serializerOptions);
            switch (message)
            {
                case { Type: "request", Id: not null, Command: not null }:
                    await RunAsync(message).ConfigureAwait(true);
                    break;
                case { Type: "cancel", Id: not null }:
                    if (_calls.TryGetValue(message.Id, out var cancellation))
                    {
                        cancellation.Cancel();
                    }

                    break;
                default:
                    LogUnknownMessage(logger, args.WebMessageAsJson);
                    break;
            }
        }
        catch (Exception exception)
        {
            LogMessageFailed(logger, exception);
        }
    }

    private async Task RunAsync(WebMessage message)
    {
        var id = message.Id!;
        using var cancellation = new CancellationTokenSource();
        _calls[id] = cancellation;

        try
        {
            var request = new CommandRequest(message.Command!, message.Payload ?? default, CommandSources.WebView)
            {
                Headers = message.Headers ?? new Dictionary<string, string>(),
                Progress = new ProgressSink(this, id),
                CancellationToken = cancellation.Token
            };

            // Back on the UI thread after the await: the executor never throws
            var result = await executor.ExecuteAsync(request).ConfigureAwait(true);
            Respond(id, result);
        }
        finally
        {
            _calls.TryRemove(id, out _);
        }
    }

    /// <summary>Progress comes from the thread pool; the WebView takes messages only on its UI thread.</summary>
    private void PostProgress(string id, ProgressInfo value)
    {
        dispatcher.InvokeAsync(() => Post(new WebMessage("progress", id, Payload: ToJson(value))));
    }

    private void Respond(string id, CommandResult result)
    {
        JsonElement? payload;
        try
        {
            payload = ToJson(result.Value);
        }
        catch (Exception exception) when (exception is NotSupportedException or JsonException or InvalidOperationException)
        {
            LogSerializationFailed(logger, exception, id);
            Post(new WebMessage("response", id, Error: new CommandError(CommandErrorCodes.Failed, "The result cannot be sent to the page.")));
            return;
        }

        Post(new WebMessage("response", id, Payload: payload, Error: result.Error));
    }

    private JsonElement? ToJson(object? value)
    {
        return value is null ? null : JsonSerializer.SerializeToElement(value, value.GetType(), serializerOptions);
    }

    private void Post(WebMessage message)
    {
        try
        {
            core.PostWebMessageAsJson(JsonSerializer.Serialize(message, serializerOptions));
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or COMException)
        {
            // The window or pane was closed while the command ran — nobody is listening
        }
    }

    [LoggerMessage(LogLevel.Warning, "Dropped a message from a foreign origin {Origin}")]
    private static partial void LogForeignMessage(ILogger logger, string origin);

    [LoggerMessage(LogLevel.Warning, "Dropped an unknown message: {Json}")]
    private static partial void LogUnknownMessage(ILogger logger, string json);

    [LoggerMessage(LogLevel.Error, "A message from the page could not be handled")]
    private static partial void LogMessageFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Error, "The result of call {Id} could not be serialized")]
    private static partial void LogSerializationFailed(ILogger logger, Exception exception, string? id);

    /// <summary>Progress of one call, posted to its page on the UI thread.</summary>
    private sealed class ProgressSink(WebViewTransport transport, string id) : IProgress<ProgressInfo>
    {
        public void Report(ProgressInfo value)
        {
            transport.PostProgress(id, value);
        }
    }
}
