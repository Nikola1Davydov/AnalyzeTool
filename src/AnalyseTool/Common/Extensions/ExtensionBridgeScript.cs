namespace AnalyseTool.Common.Extensions
{
    /// <summary>
    /// JavaScript injected into every standalone extension page (via
    /// AddScriptToExecuteOnDocumentCreatedAsync) so it can call host commands without bundling the
    /// SPA's RevitBridge. Mirrors the AT.invoke protocol: posts {Type,Command,Payload,Id} and
    /// resolves the matching promise when a response with the same Id (or an Error) comes back.
    /// </summary>
    internal static class ExtensionBridgeScript
    {
        public const string Js = @"
(function () {
  if (window.AT) return;
  var wv = window.chrome && window.chrome.webview;
  if (!wv) {
    window.AT = { invoke: function () { return Promise.reject(new Error('WebView2 messaging not available')); } };
    return;
  }
  var pending = new Map();
  var seq = 0;
  wv.addEventListener('message', function (e) {
    var msg = e.data;
    if (!msg || !msg.Id) return;
    var p = pending.get(msg.Id);
    if (!p) return;
    pending.delete(msg.Id);
    if (msg.Error) p.reject(new Error(msg.Error));
    else p.resolve(msg.Payload);
  });
  window.AT = {
    invoke: function (command, payload) {
      return new Promise(function (resolve, reject) {
        var id = 'at-' + Date.now() + '-' + (++seq);
        pending.set(id, { resolve: resolve, reject: reject });
        wv.postMessage({ Type: 'Request', Command: command, Payload: (payload == null ? null : payload), Id: id });
      });
    }
  };
})();
";
    }
}
