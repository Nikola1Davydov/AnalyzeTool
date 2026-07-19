using Microsoft.Web.WebView2.Core;

namespace AnalyseTool.Common
{
    /// <summary>
    /// One place that decides how every WebView2 window/pane loads the clientapp:
    /// <list type="bullet">
    /// <item><b>Release</b> — serve the built SPA from the plugin folder over a private virtual host.</item>
    /// <item><b>Debug</b> — load the Vite dev server (HMR).</item>
    /// </list>
    /// The switch is the single <c>ATRELEASE</c> symbol (defined for every Release config in
    /// AnalyseTool.csproj), so it works for R25/R26/R27 and any future Revit version without the windows
    /// enumerating them. Every window calls <see cref="ResolveUrl"/> instead of duplicating an
    /// <c>#if RELEASE_R25 || …</c> block.
    /// </summary>
    internal static class ClientAppHost
    {
        private const string VirtualHost = "app";

        /// <summary>
        /// Applies the virtual-host mapping (release only) and returns the URL for a hash route
        /// (e.g. <c>""</c> for the app root, or <c>"#/families"</c>). The mapping is idempotent, so this
        /// may be called repeatedly on the same WebView (e.g. the dockable pane switching content).
        /// </summary>
        public static string ResolveUrl(CoreWebView2 core, string hashRoute = "")
        {
#if ATRELEASE
            // Re-mapping the same host name just updates the mapping (WebView2 doesn't throw), so callers
            // that navigate more than once are fine.
            core.SetVirtualHostNameToFolderMapping(
                VirtualHost, PathProvider.RootDirectory, CoreWebView2HostResourceAccessKind.Allow);
            return $"https://{VirtualHost}/index.html{hashRoute}";
#else
            return PathProvider.DebugServerUrl.TrimEnd('/') + "/" + hashRoute;
#endif
        }
    }
}
