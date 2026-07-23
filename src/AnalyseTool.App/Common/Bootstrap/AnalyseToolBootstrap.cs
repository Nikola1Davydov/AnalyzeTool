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

            CommandDispatcher dispatcher = new CommandDispatcher(hub);
            // Built-ins live in three assemblies: platform commands in Core, feature commands in
            // Tools, host commands (CheckUpdate, GetChangelog, PickFolder, …) here.
            dispatcher.RegisterBuiltIns(
                typeof(CommandDispatcher).Assembly,
                typeof(AnalyseTool.Tools.Families.GetFamilies).Assembly,
                typeof(McpServerController).Assembly,
                Assembly.GetExecutingAssembly());

            // Extensions may reference host/Tools types (they shouldn't, but be safe): share them
            // by simple name so crossing types keep one identity. Core registers itself already.
            ExtensionLoadContext.ShareWithExtensions(Assembly.GetExecutingAssembly());
            ExtensionLoadContext.ShareWithExtensions(typeof(AnalyseTool.Tools.Families.GetFamilies).Assembly);

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

            // Busy-state push: every window/pane shows what the platform is doing (bottom status bar).
            // The payload mirrors GetQueueStatus so event and poll stay one shape.
            queue.RunningChanged += () =>
                Common.Transport.WebView2Transport.BroadcastEvent(
                    "QueueChanged", Core.Features.Extensions.GetQueueStatus.Snapshot());

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
