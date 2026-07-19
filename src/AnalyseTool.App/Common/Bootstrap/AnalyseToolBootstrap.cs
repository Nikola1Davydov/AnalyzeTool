using AnalyseTool.App.Common.Extensions;
using AnalyseTool.Core;
using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Transport;
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
                typeof(AnalyseTool.Tools.Features.Families.GetFamilies).Assembly,
                Assembly.GetExecutingAssembly());

            // Extensions may reference host/Tools types (they shouldn't, but be safe): share them
            // by simple name so crossing types keep one identity. Core registers itself already.
            ExtensionLoadContext.ShareWithExtensions(Assembly.GetExecutingAssembly());
            ExtensionLoadContext.ShareWithExtensions(typeof(AnalyseTool.Tools.Features.Families.GetFamilies).Assembly);

            // Load user-authored C# extensions from %LOCALAPPDATA%\<plugin>\extensions\<revitVersion>\
            ExtensionLoader loader = new ExtensionLoader(dispatcher, revitVersion);
            loader.LoadAll();

            // From here on the platform is reachable ONLY through CoreServices (windows, dock panes,
            // ribbon and Core commands all use it); the reload event refreshes the ribbon buttons.
            CoreServices.Initialize(dispatcher, loader, revitVersion);
            CoreServices.ExtensionsReloaded += () =>
                RibbonEventHub.Run(app => RibbonHost.RefreshExtensionButtons(app.Application.VersionNumber));

            // MCP transport: owns the localhost WebSocket bridge to the SAME dispatcher, and
            // auto-starts it if the user enabled it previously (persisted in mcp.json).
            McpServerController.Initialize(dispatcher);

            Log.Information("AnalyseTool host ready — {CommandCount} commands registered", dispatcher.RegisteredCommands.Count);
        }
    }
}
