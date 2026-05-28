using AnalyseTool.Common.Dispatch;
using AnalyseTool.Common.Extensions;
using AnalyseTool.Common.Transport;
using Autodesk.Revit.UI;
using System.Reflection;

namespace AnalyseTool.Common.Bootstrap
{
    internal static class AnalyseToolBootstrap
    {
        private static bool _initialized;
        private static ExtensionLoader _loader = null!;
        public static CommandDispatcher Dispatcher { get; private set; } = null!;
        public static RevitTaskHub RevitTaskHub { get; private set; } = null!;

        public static void Initialize(UIApplication uiApp)
        {
            if (_initialized) return;

            Context.Init(uiApp);

            // Created here because ExternalEvent.Create requires a valid Revit API context,
            // and IExternalCommand.Execute (our caller) is one.
            RevitTaskHub = new RevitTaskHub();
            RevitTaskHub.Initialize();

            Dispatcher = new CommandDispatcher(RevitTaskHub);
            Dispatcher.RegisterBuiltIns(Assembly.GetExecutingAssembly());

            // Load user-authored C# extensions from %LOCALAPPDATA%\<plugin>\extensions\<revitVersion>\
            _loader = new ExtensionLoader(Dispatcher, uiApp.Application.VersionNumber);
            _loader.LoadAll();

            // MCP transport: owns the localhost WebSocket bridge to the SAME dispatcher, and
            // auto-starts it if the user enabled it previously (persisted in mcp.json).
            McpServerController.Initialize(Dispatcher);

            _initialized = true;
        }

        /// <summary>Reloads extension command DLLs (collectible contexts) so changed code takes effect
        /// without restarting Revit. No-op until Initialize has run.</summary>
        public static void ReloadExtensions()
        {
            if (!_initialized) return;
            _loader.UnloadAll();
            _loader.LoadAll();
        }


    }
}
