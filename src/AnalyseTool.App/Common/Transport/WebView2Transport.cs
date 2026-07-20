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
            if (!string.Equals(request.Type, WebMessageTypeEnum.Request.ToString(), StringComparison.OrdinalIgnoreCase))
                return;

            string command = request.Command;
            string? id = request.Id;
            try
            {
                // Progress sink bound to THIS window + request id, so a progress-aware command's updates
                // are pushed back only to the caller that started it.
                IProgress<ProgressInfo> progress = new Progress<ProgressInfo>(info => SendProgress(command, id, info));
                object? result = await _queue.ExecuteAsync(
                    new CommandRequest(command, request.Payload, Source) { Progress = progress });
                // Always reply (null -> JSON null) so a caller awaiting this command's response resolves.
                SendResponse(command, id, result);
            }
            catch (Exception ex)
            {
                SendError(command, id, ex.Message);
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
