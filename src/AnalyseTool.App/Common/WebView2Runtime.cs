using AnalyseTool.Core.Common;
using Microsoft.Web.WebView2.Core;
using Serilog;
using System.Diagnostics;

namespace AnalyseTool.App.Common
{
    /// <summary>
    /// Guards against a missing Microsoft Edge WebView2 Runtime — the whole UI runs on it, so without it
    /// every AnalyseTool window would open blank. We detect it up front and, if absent, show a friendly
    /// one-time message with a download link instead of a broken window.
    /// </summary>
    internal static class WebView2Runtime
    {
        private const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
        private static bool _warned;

        /// <summary>
        /// Makes WebView2 register its Win32 window classes under a unique suffix
        /// (<c>Chrome_WidgetWin_0_EmbeddedBrowserWebView</c>). Revit 2025 hosts an older CEF (Manage Links
        /// and other dialogs) that registers the same <c>Chrome_WidgetWin_0</c> class; once a newer WebView2
        /// has claimed it, CEF's CreateWindowEx fails and Revit dies in libcef.dll (#138). This is
        /// Microsoft's opt-in fix for exactly that collision; it is harmless on Revit versions without it.
        /// </summary>
        internal const string BrowserArguments = "--edge-webview-unique-window-class";

        /// <summary>
        /// The one way to create a <see cref="CoreWebView2Environment"/> in this add-in. Every WebView2
        /// shares the profile's user-data folder, and WebView2 refuses a second environment on the same
        /// folder with different options — so all windows must go through here.
        /// </summary>
        public static Task<CoreWebView2Environment> CreateEnvironmentAsync()
        {
            CoreWebView2EnvironmentOptions options = new(additionalBrowserArguments: BrowserArguments);
            return CoreWebView2Environment.CreateAsync(null, PathProvider.ProfilePath, options);
        }

        /// <summary>True when the Evergreen WebView2 Runtime is installed.</summary>
        public static bool IsAvailable()
        {
            try
            {
                string? version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrEmpty(version);
            }
            catch
            {
                return false; // throws WebView2RuntimeNotFoundException when absent
            }
        }

        /// <summary>Returns true if the runtime is present. Otherwise logs, shows a one-time dialog with a
        /// download link, and returns false so the caller can skip opening a WebView2 window.</summary>
        public static bool EnsureOrWarn()
        {
            if (IsAvailable()) return true;

            Log.Warning("Microsoft Edge WebView2 Runtime not found — UI windows cannot open");

            if (!_warned)
            {
                _warned = true;
                bool open = UserDialogUtils.ShowConfirmation(
                    "AnalyseTool needs the Microsoft Edge WebView2 Runtime to show its interface, but it " +
                    "isn't installed.\n\nInstall the Evergreen runtime, then restart Revit.\n\n" +
                    "Open the download page now?",
                    "WebView2 Runtime required");

                if (open)
                {
                    try { Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true }); }
                    catch (Exception ex) { Log.Error(ex, "Failed to open the WebView2 download page"); }
                }
            }

            return false;
        }
    }
}
