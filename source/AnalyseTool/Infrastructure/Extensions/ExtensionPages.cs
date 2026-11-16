using System.IO;
using System.Text;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Manifest;
using AnalyseTool.Hosting.WebView.Controls;

namespace AnalyseTool.Infrastructure.Extensions;

/// <summary>
///     The HTML page of an extension button: the file of the extension folder served under its own virtual host (or the
///     dev server of <c>ui.devUrl</c>), with <c>window.AT</c> injected — the API extension pages of 1.x use.
/// </summary>
public static class ExtensionPages
{
    /// <summary>
    ///     <c>AT.invoke(command, payload, { onProgress, signal })</c> over the WebView2 messages of the pipeline: resolves with
    ///     the result, rejects with an <c>Error</c> whose <c>code</c> is the error code. The same contract as 1.x, whose errors
    ///     were plain messages.
    /// </summary>
    public const string BridgeScript = """
                                       (function () {
                                         if (window.AT) return;
                                         var wv = window.chrome && window.chrome.webview;
                                         if (!wv) {
                                           window.AT = { inRevit: false, invoke: function () { return Promise.reject(new Error('Not inside AnalyseTool: WebView2 messaging is not available')); } };
                                           return;
                                         }
                                         var pending = new Map();
                                         var seq = 0;
                                         wv.addEventListener('message', function (e) {
                                           var msg = e.data;
                                           if (!msg || !msg.id) return;
                                           var call = pending.get(msg.id);
                                           if (!call) return;
                                           if (msg.type === 'progress') { if (call.onProgress) call.onProgress(msg.payload); return; }
                                           if (msg.type !== 'response') return;
                                           pending.delete(msg.id);
                                           if (msg.error) {
                                             var error = new Error(msg.error.message || msg.error.code);
                                             error.code = msg.error.code;
                                             call.reject(error);
                                           } else {
                                             call.resolve(msg.payload === undefined ? null : msg.payload);
                                           }
                                         });
                                         window.AT = {
                                           inRevit: true,
                                           invoke: function (command, payload, options) {
                                             return new Promise(function (resolve, reject) {
                                               var id = 'at-' + Date.now() + '-' + (++seq);
                                               pending.set(id, { resolve: resolve, reject: reject, onProgress: options && options.onProgress });
                                               var signal = options && options.signal;
                                               if (signal) {
                                                 if (signal.aborted) { pending.delete(id); reject(new DOMException('Aborted', 'AbortError')); return; }
                                                 signal.addEventListener('abort', function () { wv.postMessage({ type: 'cancel', id: id }); });
                                               }
                                               wv.postMessage({ type: 'request', id: id, command: command, payload: payload == null ? null : payload, headers: {} });
                                             });
                                           }
                                         };
                                       })();
                                       """;

    /// <summary>The page of a button of an extension.</summary>
    /// <param name="extension">The extension.</param>
    /// <param name="button">The button; <see langword="null" /> for the extension's main page.</param>
    public static ExternalPage For(ExtensionDescriptor extension, ExtensionButton? button)
    {
        var ui = extension.Manifest.Ui;
        var entry = (!string.IsNullOrWhiteSpace(button?.EntryHtml) ? button!.EntryHtml : ui?.EntryHtml) ?? "index.html";
        entry = entry.Replace('\\', '/').TrimStart('/');
        var key = extension.Id + "/" + entry;
        if (!string.IsNullOrWhiteSpace(ui?.DevUrl))
        {
            return new ExternalPage(key, ui!.DevUrl!) { InitScript = BridgeScript };
        }

        var host = HostName(extension.Id);
        return new ExternalPage(key, $"https://{host}/{entry}")
        {
            HostName = host,
            Folder = extension.Directory,
            InitScript = BridgeScript
        };
    }

    /// <summary>Whether the page file of a button exists — a missing file is reported instead of a blank window.</summary>
    public static bool Exists(ExtensionDescriptor extension, ExtensionButton? button)
    {
        var page = For(extension, button);
        return page.Folder is null || File.Exists(Path.Combine(extension.Directory, page.Url[(page.Url.IndexOf('/', 8) + 1)..].Split('?', '#')[0]));
    }

    /// <summary>One virtual host per extension, so two extensions never share an origin (storage, cookies).</summary>
    private static string HostName(string id)
    {
        var builder = new StringBuilder();
        foreach (var character in id.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        return builder.ToString().Trim('-') + ".ext.analysetool.local";
    }
}
