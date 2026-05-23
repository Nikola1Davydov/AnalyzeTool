using AnalyseTool.Common.Model;
using AnalyseTool.Infrastructure.Dispatch;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Infrastructure.Transport
{
    internal sealed class WebView2Transport
    {
        private readonly WebView2 _webView;
        private readonly CommandDispatcher _dispatcher;

        public WebView2Transport(WebView2 webView, CommandDispatcher dispatcher)
        {
            _webView = webView;
            _dispatcher = dispatcher;
        }

        public void Attach()
        {
            _webView.WebMessageReceived += OnWebMessageReceived;
        }

        public void Detach()
        {
            _webView.WebMessageReceived -= OnWebMessageReceived;
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
                object? result = await _dispatcher.DispatchAsync(command, request.Payload, CancellationToken.None);
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
            _webView.CoreWebView2.PostWebMessageAsJson(json);
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
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }
}
