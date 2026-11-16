using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Web.WebView2.Core;

namespace AnalyseTool.Hosting.WebView.Runtime;

/// <summary>
///     One WebView2 environment for the whole add-in: one browser process and one user data folder for every window.
/// </summary>
/// <remarks>
///     The user data folder is <c>%LOCALAPPDATA%\&lt;add-in&gt;\WebView2</c>: the Revit folder is not writable.
/// </remarks>
internal sealed class WebViewEnvironmentProvider(IHostEnvironment environment)
{
    /// <summary>The page with the Evergreen runtime installer.</summary>
    public const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    private Task<CoreWebView2Environment>? _environment;

    /// <summary>Gets the user data folder of the environment.</summary>
    public string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        environment.ApplicationName,
        "WebView2");

    /// <summary>Returns the installed runtime version, or <see langword="null" /> when the runtime is missing.</summary>
    public static string? GetRuntimeVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Returns the shared environment, creating it on the first call. Call on the UI thread.</summary>
    public Task<CoreWebView2Environment> GetAsync()
    {
        return _environment ??= CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
    }
}
