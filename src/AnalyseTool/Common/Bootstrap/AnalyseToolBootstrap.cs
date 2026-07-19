using AnalyseTool.Common.Dispatch;
using AnalyseTool.Common.Extensions;
using AnalyseTool.Common.Transport;
using Autodesk.Revit.UI;
using Serilog;
using System.Reflection;

namespace AnalyseTool.Common.Bootstrap
{
    internal static class AnalyseToolBootstrap
    {
        private static bool _initialized;
        private static ExtensionLoader _loader = null!;
        public static CommandDispatcher Dispatcher { get; private set; } = null!;
        public static RevitTaskHub RevitTaskHub { get; private set; } = null!;

        /// <summary>True once <see cref="Initialize"/> has run — lets callers that may fire before any
        /// ribbon click (e.g. an auto-restored dockable pane) decide whether they must defer.</summary>
        public static bool IsInitialized => _initialized;

        public static void Initialize(UIApplication uiApp)
        {
            if (_initialized) return;

            AppLog.Initialize();
            Log.Information("Initializing AnalyseTool host (Revit {RevitVersion})", uiApp.Application.VersionNumber);

            Context.Init(uiApp);
            DocumentTracker.Initialize(uiApp);

            // Created here because ExternalEvent.Create requires a valid Revit API context,
            // and IExternalCommand.Execute (our caller) is one.
            RevitTaskHub = new RevitTaskHub();
            RevitTaskHub.Initialize();

            Dispatcher = new CommandDispatcher(RevitTaskHub);
            // Built-ins live in three assemblies now: platform commands in Core, feature commands
            // in Tools, host commands (CheckUpdate, GetChangelog, …) here.
            Dispatcher.RegisterBuiltIns(typeof(CommandDispatcher).Assembly);
            Dispatcher.RegisterBuiltIns(typeof(Features.Families.GetFamilies).Assembly);
            Dispatcher.RegisterBuiltIns(Assembly.GetExecutingAssembly());

            // Extensions may reference host/Tools types (they shouldn't, but be safe): share them
            // by simple name so crossing types keep one identity. Core registers itself already.
            ExtensionLoadContext.ShareWithExtensions(Assembly.GetExecutingAssembly());
            ExtensionLoadContext.ShareWithExtensions(typeof(Features.Families.GetFamilies).Assembly);

            // Load user-authored C# extensions from %LOCALAPPDATA%\<plugin>\extensions\<revitVersion>\
            _loader = new ExtensionLoader(Dispatcher, uiApp.Application.VersionNumber);
            _loader.LoadAll();

            // Core-side commands (ReloadExtensions, SaveAsCommand, …) reach the loader/dispatcher
            // through CoreServices; the reload event lets the host refresh its ribbon buttons.
            CoreServices.Initialize(Dispatcher, _loader);
            CoreServices.ExtensionsReloaded += () =>
                RibbonEventHub.Run(app => RibbonHost.RefreshExtensionButtons(app.Application.VersionNumber));

            // MCP transport: owns the localhost WebSocket bridge to the SAME dispatcher, and
            // auto-starts it if the user enabled it previously (persisted in mcp.json).
            McpServerController.Initialize(Dispatcher);

            _initialized = true;
            Log.Information("AnalyseTool host ready — {CommandCount} commands registered", Dispatcher.RegisteredCommands.Count);
        }

        /// <summary>Reloads extension command DLLs (collectible contexts) so changed code takes effect
        /// without restarting Revit. No-op until Initialize has run. Delegates to Core — the single
        /// owner of the loader lifecycle since the platform split.</summary>
        public static void ReloadExtensions() => CoreServices.ReloadExtensions();


    }
}
