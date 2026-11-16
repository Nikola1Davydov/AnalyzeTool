using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.WebView.ClientApp;
using AnalyseTool.Hosting.WebView.Runtime;
using AnalyseTool.Hosting.WebView.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;

namespace AnalyseTool.Hosting.WebView.Controls;

/// <summary>
///     The security choice of <c>AddWebView</c>.
/// </summary>
/// <param name="RestrictNavigation">Whether only the add-in's own pages may load and send commands; on unless <c>AllowAnyNavigation()</c>.</param>
internal sealed record WebViewSecurity(bool RestrictNavigation);

/// <summary>
///     The services a <see cref="WebViewHost" /> needs; one instance for the whole add-in.
/// </summary>
internal sealed record WebViewServices(
    WebViewSecurity Security,
    WebViewEnvironmentProvider Environment,
    ClientAppSource Source,
    ICommandExecutor Executor,
    IOptions<JsonSerializerOptions> SerializerOptions,
    IHostEnvironment HostEnvironment,
    ILogger<WebViewHost> Logger);

/// <summary>
///     The WebView surface of <see cref="WebViewWindow" /> and of the dock pane: environment, settings, transport,
///     recovery and the optional navigation guard live here once.
/// </summary>
/// <remarks>
///     Initializes on <c>Loaded</c>: WebView2 needs a window handle. Without the WebView2 runtime it shows a message with a
///     link to the installer instead of a blank surface.
/// </remarks>
[PublicAPI]
public sealed partial class WebViewHost : ContentControl, IDisposable
{
    private readonly WebViewServices _services;
    private Microsoft.Web.WebView2.Wpf.WebView2? _webView;
    private WebViewTransport? _transport;
    private string _route;
    private ExternalPage? _page;
    private readonly List<ExternalPage> _mounted = [];
    private readonly HashSet<string> _frameScripts = new(StringComparer.Ordinal);
    private bool _initializing;

    internal WebViewHost(WebViewServices services, string route)
    {
        _services = services;
        _route = route;
        Loaded += (_, _) => _ = InitializeAsync();
    }

    internal WebViewHost(WebViewServices services, ExternalPage page) : this(services, page.Key)
    {
        _page = page;
    }

    /// <summary>Gets the external page the host shows, or <see langword="null" /> for a route of ClientApp.</summary>
    public ExternalPage? Page => _page;

    /// <summary>Gets the route the host shows, for example <c>/families</c>.</summary>
    public string Route => _route;

    /// <summary>Shows another route without reloading the WebView environment.</summary>
    public void Navigate(string route)
    {
        _route = route;
        _webView?.CoreWebView2?.Navigate(_services.Source.GetUrl(route));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _transport?.Detach();
        _transport = null;
        _webView?.Dispose();
        _webView = null;
    }

    private async Task InitializeAsync()
    {
        if (_initializing || _webView is not null)
        {
            return;
        }

        if (WebViewEnvironmentProvider.GetRuntimeVersion() is null)
        {
            LogRuntimeMissing(_services.Logger);
            ShowMessage("The Microsoft Edge WebView2 Runtime is not installed.", "Install the WebView2 Runtime",
                WebViewEnvironmentProvider.RuntimeDownloadUrl);
            return;
        }

        _initializing = true;
        try
        {
            var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
            Content = webView;
            await webView.EnsureCoreWebView2Async(await _services.Environment.GetAsync());

            Configure(webView.CoreWebView2);
            foreach (var mounted in _mounted.ToList())
            {
                await ApplyMountAsync(webView.CoreWebView2, mounted);
            }

            _transport = new WebViewTransport(webView.CoreWebView2, Dispatcher, _services.Executor,
                // An external page is held to its own origin by the navigation guard; ClientApp by its source
                _services.Security.RestrictNavigation && _page is null ? _services.Source : null, _services.SerializerOptions.Value, _services.Logger);
            _transport.Attach();
            _webView = webView;

            if (_page is not null)
            {
                await ShowPageAsync(webView.CoreWebView2, _page);
            }
            else
            {
                webView.CoreWebView2.Navigate(_services.Source.GetUrl(_route));
            }
        }
        catch (Exception exception)
        {
            LogInitializationFailed(_services.Logger, exception);
            ShowMessage($"The page could not be loaded: {exception.Message}", null, null);
        }
        finally
        {
            _initializing = false;
        }
    }

    /// <summary>
    ///     Lets this WebView show an external page in a frame: its folder under its virtual host, and its frame script. The
    ///     ClientApp page then puts it in an <c>iframe</c>.
    /// </summary>
    public async Task MountAsync(ExternalPage page)
    {
        if (!_mounted.Any(item => item.Key == page.Key))
        {
            _mounted.Add(page);
        }

        if (_webView?.CoreWebView2 is { } core)
        {
            await ApplyMountAsync(core, page);
        }
    }

    private async Task ApplyMountAsync(CoreWebView2 core, ExternalPage page)
    {
        if (page.HostName is not null && page.Folder is not null)
        {
            core.SetVirtualHostNameToFolderMapping(page.HostName, page.Folder, CoreWebView2HostResourceAccessKind.Allow);
        }

        // Scripts on document creation run in every frame too; once per WebView is enough
        if (page.FrameScript is not null && _frameScripts.Add(page.FrameScript))
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(page.FrameScript);
        }
    }

    private static async Task ShowPageAsync(CoreWebView2 core, ExternalPage page)
    {
        if (page.HostName is not null && page.Folder is not null)
        {
            core.SetVirtualHostNameToFolderMapping(page.HostName, page.Folder, CoreWebView2HostResourceAccessKind.Allow);
        }

        if (page.InitScript is not null)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(page.InitScript);
        }

        core.Navigate(page.Url);
    }

    private void Configure(CoreWebView2 core)
    {
        var development = _services.HostEnvironment.IsDevelopment();

        core.Settings.AreDevToolsEnabled = development;
        core.Settings.AreDefaultContextMenusEnabled = development;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        _services.Source.Map(core);

        core.ProcessFailed += (_, args) =>
        {
            LogProcessFailed(_services.Logger, args.ProcessFailedKind);
            if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                Dispose();
                ShowMessage("The embedded browser stopped. Close and open the window again.", null, null);
            }
            else
            {
                _webView?.Reload();
            }
        };

        if (_services.Security.RestrictNavigation)
        {
            RestrictNavigation(core);
        }
    }

    /// <summary>Only our own pages may load: any page in the WebView can call commands.</summary>
    private void RestrictNavigation(CoreWebView2 core)
    {
        core.NavigationStarting += (_, args) =>
        {
            if (_page is null ? _services.Source.IsOwn(args.Uri) : IsPageOrigin(_page, args.Uri))
            {
                return;
            }

            args.Cancel = true;
            OpenInBrowser(args.Uri);
        };

        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            OpenInBrowser(args.Uri);
        };
    }

    private static bool IsPageOrigin(ExternalPage page, string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var target)
               && Uri.TryCreate(page.Url, UriKind.Absolute, out var own)
               && string.Equals(target.GetLeftPart(UriPartial.Authority), own.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }

    private void OpenInBrowser(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
        {
            LogOpenedInBrowser(_services.Logger, uri);
            Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
            return;
        }

        LogNavigationBlocked(_services.Logger, uri);
    }

    private void ShowMessage(string text, string? linkText, string? linkUrl)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var panel = new StackPanel { Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(block);

        if (linkText is not null && linkUrl is not null)
        {
            var link = new Hyperlink(new Run(linkText)) { NavigateUri = new Uri(linkUrl) };
            link.RequestNavigate += (_, _) => OpenInBrowser(linkUrl);
            panel.Children.Add(new TextBlock(link));
        }

        Content = panel;
    }

    [LoggerMessage(LogLevel.Warning, "The Microsoft Edge WebView2 Runtime is not installed")]
    private static partial void LogRuntimeMissing(ILogger logger);

    [LoggerMessage(LogLevel.Error, "The WebView could not be initialized")]
    private static partial void LogInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "The WebView process failed: {Kind}")]
    private static partial void LogProcessFailed(ILogger logger, CoreWebView2ProcessFailedKind kind);

    [LoggerMessage(LogLevel.Information, "Opened {Uri} in the system browser")]
    private static partial void LogOpenedInBrowser(ILogger logger, string uri);

    [LoggerMessage(LogLevel.Warning, "Blocked the navigation to {Uri}")]
    private static partial void LogNavigationBlocked(ILogger logger, string uri);
}
