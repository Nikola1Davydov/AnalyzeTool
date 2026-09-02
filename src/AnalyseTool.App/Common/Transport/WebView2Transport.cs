using AnalyseTool.App.Common.Dispatch;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Sdk;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.App.Common.Transport
{
    internal sealed class WebView2Transport
    {
        private const string Source = "webview2";

        private readonly WebView2 _webView;
        private readonly CommandQueue _queue;

        // All attached transports, for host-initiated broadcasts (busy state, …). A window's
        // transport registers on Attach and leaves on Detach; Post already tolerates a WebView that
        // died without detaching.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<WebView2Transport, byte> _attached = new();

        // In-flight requests of THIS window, so a Cancel can reach the one it names. Per-transport, not
        // static: a window may abandon its own calls and no one else's.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _inFlight = new();

        public WebView2Transport(WebView2 webView, CommandQueue queue)
        {
            _webView = webView;
            _queue = queue;
        }

        public void Attach()
        {
            _webView.WebMessageReceived += OnWebMessageReceived;
            _attached[this] = 0;
        }

        public void Detach()
        {
            _webView.WebMessageReceived -= OnWebMessageReceived;
            _attached.TryRemove(this, out _);
        }

        /// <summary>How many windows/panes are attached right now — the activity indicator shows a
        /// window's own calls only when no window is left to show them.</summary>
        public static int AttachedCount => _attached.Count;

        /// <summary>Pushes an Event to EVERY attached window/pane (e.g. "QueueChanged"). Safe from any
        /// thread — posting marshals to each WebView's dispatcher.</summary>
        public static void BroadcastEvent(string name, object? payload = null)
        {
            foreach (WebView2Transport transport in _attached.Keys)
                transport.SendEvent(name, payload);
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            WebViewMessage? request = JsonConvert.DeserializeObject<WebViewMessage>(args.WebMessageAsJson);
            if (request == null) return;

            // Ping is answered here and now: this handler runs on the WebView's UI thread — Revit's — so
            // reaching this line IS the fact the page is asking about. Nothing to enqueue, nothing to log:
            // one of these arrives every tenth of a second from every open window.
            if (string.Equals(request.Type, WebMessageTypeEnum.Ping.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Post(JsonConvert.SerializeObject(new WebViewMessage { Type = "Pong", Command = "Ping", Id = request.Id }));
                return;
            }

            // Cancel carries no work of its own: it names an id and returns. The command it interrupts
            // still answers through the normal path, so there is exactly one way a call finishes.
            if (string.Equals(request.Type, WebMessageTypeEnum.Cancel.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(request.Id) &&
                    _inFlight.TryGetValue(request.Id!, out CancellationTokenSource? pending))
                {
                    try { pending.Cancel(); } catch (ObjectDisposedException) { /* it just finished */ }
                }
                return;
            }

            if (!string.Equals(request.Type, WebMessageTypeEnum.Request.ToString(), StringComparison.OrdinalIgnoreCase))
                return;

            string command = request.Command;
            string? id = request.Id;

            // A token for every request, whether or not anyone ever cancels it: deciding per command
            // would mean knowing in advance which ones are worth interrupting.
            CancellationTokenSource cts = new();
            if (!string.IsNullOrEmpty(id)) _inFlight[id!] = cts;
            try
            {
                // Progress sink bound to THIS window + request id, so a progress-aware command's updates
                // are pushed back only to the caller that started it.
                IProgress<ProgressInfo> progress = new Progress<ProgressInfo>(info => SendProgress(command, id, info));
                object? result = await _queue.ExecuteAsync(
                    new CommandRequest(command, request.Payload, Source)
                    {
                        Progress = progress,
                        CancellationToken = cts.Token,
                    });
                // Always reply (null -> JSON null) so a caller awaiting this command's response resolves.
                SendResponse(command, id, result);
            }
            catch (OperationCanceledException)
            {
                // The caller asked for this, so it is not a fault — but the promise still has to settle,
                // and saying which way it settled is the whole point.
                SendError(command, id, "Cancelled.");
            }
            catch (Exception ex)
            {
                SendError(command, id, ex.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(id)) _inFlight.TryRemove(id!, out _);
                cts.Dispose();
            }
        }

        private void SendResponse(string command, string? id, object? payload)
        {
            string json = JsonConvert.SerializeObject(new WebViewMessage
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = command,
                Id = id,
                Payload = payload is null ? JValue.CreateNull() : JToken.FromObject(payload)
            });
            Post(json);
        }

        /// <summary>Pushes a host-initiated broadcast (no request Id) to this WebView — e.g. "the active
        /// document changed". The frontend routes these by name (see RevitBridge's Event handling).</summary>
        public void SendEvent(string name, object? payload = null)
        {
            string json = JsonConvert.SerializeObject(new WebViewMessage
            {
                Type = "Event",
                Command = name,
                Payload = payload is null ? JValue.CreateNull() : JToken.FromObject(payload)
            });
            Post(json);
        }

        /// <summary>Posts to the WebView, tolerating a window/pane that was closed while a long command
        /// was still running — CoreWebView2 may be null or already disposed by then, and throwing here
        /// would surface inside an async-void handler (or a progress callback) and could crash Revit.</summary>
        private void Post(string json)
        {
            // PostWebMessageAsJson is only valid on the WebView's UI thread; broadcasts (queue state)
            // arrive from worker threads, so marshal when needed.
            if (!_webView.Dispatcher.CheckAccess())
            {
                _webView.Dispatcher.BeginInvoke(() => Post(json));
                return;
            }
            try
            {
                _webView.CoreWebView2?.PostWebMessageAsJson(json);
            }
            catch (ObjectDisposedException) { /* window closed mid-command — nobody is listening */ }
            catch (InvalidOperationException) { /* WebView torn down — same */ }
        }

        /// <summary>Pushes an intermediate progress update for an in-flight request (same Id), before the
        /// final Response. The frontend routes it to the call's onProgress by Id.</summary>
        private void SendProgress(string command, string? id, ProgressInfo info)
        {
            string json = JsonConvert.SerializeObject(new WebViewMessage
            {
                Type = "Progress",
                Command = command,
                Id = id,
                Payload = JObject.FromObject(new { fraction = info.Fraction, message = info.Message })
            });
            Post(json);
        }

        private void SendError(string command, string? id, string message)
        {
            string json = JsonConvert.SerializeObject(new WebViewMessage
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = command,
                Id = id,
                Error = message,
                // Kept for back-compat with command-name routing (e.g. AI handlers read Payload.error).
                Payload = JObject.FromObject(new { error = message })
            });
            Post(json);
        }
    }
}
