using AnalyseTool.App.Common.Extensions;
using AnalyseTool.Core;
using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Mcp.Bridge;
using Autodesk.Revit.UI;
using Serilog;
using System.Reflection;

namespace AnalyseTool.App.Common.Bootstrap
{
    /// <summary>Composition root of the host: wires the Core platform (hub, dispatcher, loader,
    /// MCP transport) once, in a valid Revit API context. Holds NO state of its own — after
    /// Initialize everything lives in <see cref="CoreServices"/>, the single registry.</summary>
    internal static class AnalyseToolBootstrap
    {
        // Kept alive here: a Timer with no reference is collected, and with it the push.
        private static System.Threading.Timer? _availabilityWatcher;
        private static bool _lastBlocked;

        public static void Initialize(UIApplication uiApp)
        {
            if (CoreServices.IsInitialized) return;

            AppLog.Initialize();
            string revitVersion = uiApp.Application.VersionNumber;
            Log.Information("Initializing AnalyseTool host (Revit {RevitVersion})", revitVersion);

            Context.Init(uiApp);
            DocumentTracker.Initialize(uiApp);

            // Created here because ExternalEvent.Create requires a valid Revit API context,
            // and IExternalCommand.Execute (our caller) is one.
            RevitTaskHub hub = new RevitTaskHub();
            hub.Initialize();

            // The model index: one SQLite copy per open model, fed by DocumentChanged, built and kept in
            // step in the background. Subscribed here because the events need a valid API context.
            Core.Common.Index.ModelIndexHost.Initialize(uiApp, hub);

            CommandDispatcher dispatcher = new CommandDispatcher(hub);
            // Built-ins live in three assemblies: platform commands in Core, feature commands in
            // Tools, host commands (CheckUpdate, GetChangelog, PickFolder, …) here.
            dispatcher.RegisterBuiltIns(
                typeof(CommandDispatcher).Assembly,
                typeof(AnalyseTool.Tools.Elements.GetElements).Assembly,
                typeof(McpServerController).Assembly,
                Assembly.GetExecutingAssembly());

            // Extensions may reference host/Tools types (they shouldn't, but be safe): share them
            // by simple name so crossing types keep one identity. Core registers itself already.
            ExtensionLoadContext.ShareWithExtensions(Assembly.GetExecutingAssembly());
            ExtensionLoadContext.ShareWithExtensions(typeof(AnalyseTool.Tools.Elements.GetElements).Assembly);

            // Load user-authored C# extensions from %LOCALAPPDATA%\<plugin>\extensions\<revitVersion>\
            ExtensionLoader loader = new ExtensionLoader(dispatcher, revitVersion);
            loader.LoadAll();

            // The queue is the ONLY way transports and UI reach command execution — the dispatcher
            // itself never leaves this method.
            CommandQueue queue = new CommandQueue(dispatcher);

            // From here on the platform is reachable ONLY through CoreServices (windows, dock panes,
            // ribbon and Core commands all use it); the reload event refreshes the ribbon buttons.
            CoreServices.Initialize(queue, loader, revitVersion);
            CoreServices.ExtensionsReloaded += () =>
                RibbonEventHub.Run(app => RibbonHost.RefreshExtensionButtons(app.Application.VersionNumber));

            // Pinning a command in the launcher changes the ribbon and nothing else — same refresh,
            // without unloading and rebuilding every extension load context to redraw one button.
            CoreServices.RibbonButtonsChanged += () =>
                RibbonEventHub.Run(app => RibbonHost.RefreshExtensionButtons(app.Application.VersionNumber));

            // Busy-state push: every window/pane shows what the platform is doing (bottom status bar).
            // The payload mirrors GetQueueStatus so event and poll stay one shape.
            queue.RunningChanged += () =>
                Common.Transport.WebView2Transport.BroadcastEvent(
                    "QueueChanged", Core.Features.Extensions.GetQueueStatus.Snapshot());

            // The host's own sign of activity, for the person at Revit: a small window that appears
            // when a command runs without a window of its own to report it (an agent over MCP), with
            // progress and a Cancel that reaches the command through the queue.
            Common.Activity.ActivityIndicator.Initialize(queue);

            // Availability push: "Revit is busy with another action" (a dialog, an edit mode, a native
            // command) is detected by the Idling stamp within ~1.5 s, but until now it reached a window
            // only when that window next polled — up to ten seconds in idle, which read as "the bar is
            // slow". The watcher pushes the same snapshot the moment the verdict flips either way; a
            // held UI thread cannot deliver it, and for that case the page has its own heartbeat.
            _availabilityWatcher = new System.Threading.Timer(_ =>
            {
                bool blocked = Core.Features.Extensions.GetQueueStatus.IsBlocked();
                if (blocked == _lastBlocked) return;
                _lastBlocked = blocked;
                Common.Transport.WebView2Transport.BroadcastEvent(
                    "QueueChanged", Core.Features.Extensions.GetQueueStatus.Snapshot());
            }, null, 250, 250);

            // Revit-availability stamping runs in the host's single permanent Idling handler
            // (DockPaneHost.OnIdling, hooked at OnStartup). Freshen the stamp here once: Initialize
            // runs inside a command context, where Idling hasn't fired for a while by definition —
            // without this the busy bar would flash a false "Revit is busy" right after bootstrap.
            RevitAvailability.ReportIdle();

            // MCP transport: the localhost TCP bridge enqueues into the SAME queue; auto-starts if
            // the user enabled it previously (persisted in mcp.json).
            McpServerController.Initialize(queue);

            Log.Information("AnalyseTool host ready — {CommandCount} commands registered", dispatcher.RegisteredCommands.Count);
        }
    }
}
