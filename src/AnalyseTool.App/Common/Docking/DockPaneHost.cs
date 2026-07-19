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

                // Hook Idling HERE: OnStartup is a valid API context, while the pane's WPF Loaded (the
                // caller of EnsureReadyAsync) is NOT — even subscribing to Idling there throws
                // "Revit is currently not within an API context", leaving a restored pane black.
                // The handler no-ops until someone actually awaits EnsureReadyAsync, and unhooks itself
                // once the host is initialized (by us or by any ribbon click).
                app.Idling += OnIdling;
                _idlingHooked = true;

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

        private static bool _idlingHooked;

        /// <summary>
        /// Completes once the host dispatcher exists. Fast path when a ribbon click already initialized it.
        /// Otherwise (pane auto-restored at startup) this only ARMS the pre-hooked Idling handler (see
        /// <see cref="Register"/>) — no Revit API is touched here, because the caller (WPF Loaded) is not
        /// an API context. The actual initialization happens inside <see cref="OnIdling"/>, which Revit
        /// invokes with a live <see cref="UIApplication"/> in a valid API context.
        /// </summary>
        public static Task EnsureReadyAsync()
        {
            if (AnalyseToolBootstrap.IsInitialized) return Task.CompletedTask;
            if (_ready is not null) return _ready.Task;
            if (!_idlingHooked)
                return Task.FromException(new InvalidOperationException("Dockable pane host was not registered."));

            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _ready.Task;
        }

        private static void OnIdling(object? sender, IdlingEventArgs e)
        {
            // Someone else (a ribbon click) already initialized the host — our work here is done.
            if (AnalyseToolBootstrap.IsInitialized)
            {
                Unhook();
                _ready?.TrySetResult(true);
                return;
            }

            if (_ready is null) return; // nobody is waiting yet — stay dormant

            try
            {
                AnalyseToolBootstrap.Initialize((UIApplication)sender!);
                Unhook();
                _ready.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Deferred host initialization (from dockable pane) failed");
                // Fault THIS waiter but clear the field, so a retry (pane hide/show) can arm a fresh
                // attempt instead of being handed the same dead task forever.
                TaskCompletionSource<bool> failed = _ready;
                _ready = null;
                failed.TrySetException(ex);
            }
        }

        private static void Unhook()
        {
            if (!_idlingHooked) return;
            _idlingHooked = false;
            _app!.Idling -= OnIdling;
        }
    }
}
