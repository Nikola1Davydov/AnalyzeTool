using AnalyseTool.Common.Bootstrap;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Serilog;

namespace AnalyseTool.Common.Docking
{
    /// <summary>
    /// Owns AnalyseTool's ONE dockable pane and its content routing. We deliberately register a single
    /// pane (not one per feature/extension): Revit only allows <c>RegisterDockablePane</c> during
    /// OnStartup, so a single always-present host that swaps its route lets features AND extensions
    /// appear in the dock without a Revit restart — they just navigate the shared WebView.
    ///
    /// Implements <see cref="IDockablePaneProvider"/> so Revit can (re)create the pane lazily, including
    /// restoring it at startup from a saved layout.
    /// </summary>
    internal sealed class DockPaneHost : IDockablePaneProvider
    {
        // Stable id — MUST stay constant across sessions so Revit remembers the pane's dock position.
        public static readonly DockablePaneId PaneId = new(new Guid("6F3A2C10-7B4E-4E28-9C2F-2A8D5E1B94A7"));
        private const string PaneTitle = "AnalyseTool";

        private static readonly DockPaneHost _provider = new();
        private static AnalyseToolDockPane? _pane;
        private static UIControlledApplication? _app;
        private static TaskCompletionSource<bool>? _ready;
        private static string? _currentContent; // content key the pane last navigated to (for toggle logic)

        private DockPaneHost() { }

        /// <summary>Registers the single pane. Call exactly once, from RibbonHost.Build (OnStartup) — the
        /// only point Revit permits pane registration.</summary>
        public static void Register(UIControlledApplication app)
        {
            _app = app;
            try
            {
                app.RegisterDockablePane(PaneId, PaneTitle, _provider);
                Log.Information("Registered AnalyseTool dockable pane");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to register the AnalyseTool dockable pane");
            }
        }

        /// <summary>Revit calls this to obtain the pane content and its initial docking state.</summary>
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _pane ??= new AnalyseToolDockPane();
            data.FrameworkElement = _pane;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser,
            };
        }

        /// <summary>Shows a built-in clientapp route (e.g. "#/families-dock") in the pane, with toggle.</summary>
        public static void ShowRoute(string route) =>
            Toggle("route:" + route, p => p.ShowRoute(route));

        /// <summary>Shows an extension's page in the shared pane, with toggle. Keyed by extension id so a
        /// second click on the same extension's button hides the pane.</summary>
        public static void ShowExtension(string id, string directory, string? devUrl, string entryHtml) =>
            Toggle("ext:" + id, p => p.ShowExtension(id, directory, devUrl, entryHtml));

        /// <summary>
        /// Toggles the pane for a piece of content (identified by <paramref name="contentKey"/>):
        /// <list type="bullet">
        /// <item>closed → open and show it;</item>
        /// <item>open on the SAME content → close (toggle off);</item>
        /// <item>open on DIFFERENT content → switch, stay open.</item>
        /// </list>
        /// The caller (a ribbon command) must have initialized the host first, so
        /// <see cref="Context.UiApplication"/> is valid here.
        /// </summary>
        private static void Toggle(string contentKey, Action<AnalyseToolDockPane> apply)
        {
            _pane ??= new AnalyseToolDockPane();
            DockablePane pane = Context.UiApplication.GetDockablePane(PaneId);

            // Same content already visible → this click means "hide".
            if (pane.IsShown() && string.Equals(_currentContent, contentKey, StringComparison.Ordinal))
            {
                pane.Hide();
                return;
            }

            apply(_pane);
            _currentContent = contentKey;
            pane.Show();
        }

        /// <summary>
        /// Completes once the host dispatcher exists. Fast path when a ribbon click already initialized it.
        /// Otherwise (pane auto-restored at startup) we subscribe a one-shot <see cref="UIControlledApplication.Idling"/>:
        /// its handler receives a live <see cref="UIApplication"/> in a valid API context, which is exactly
        /// what <see cref="AnalyseToolBootstrap.Initialize"/> needs — then we unsubscribe immediately so the
        /// idle event isn't fired on repeatedly.
        /// </summary>
        public static Task EnsureReadyAsync()
        {
            if (AnalyseToolBootstrap.IsInitialized) return Task.CompletedTask;
            if (_ready is not null) return _ready.Task;

            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_app is null)
            {
                _ready.TrySetException(new InvalidOperationException("Dockable pane host was not registered."));
                return _ready.Task;
            }

            void OnIdling(object? sender, IdlingEventArgs e)
            {
                _app!.Idling -= OnIdling; // strictly once — Idling fires continuously while Revit is idle
                try
                {
                    AnalyseToolBootstrap.Initialize((UIApplication)sender!);
                    _ready!.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Deferred host initialization (from dockable pane) failed");
                    _ready!.TrySetException(ex);
                }
            }

            _app.Idling += OnIdling;
            return _ready.Task;
        }
    }
}
