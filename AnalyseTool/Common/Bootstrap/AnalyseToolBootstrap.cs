using AnalyseTool.Common;
using AnalyseTool.Common.Dispatch;
using AnalyseTool.Common.Extensions;
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

            // Load user-authored C# extensions from %LOCALAPPDATA%\<plugin>\extensions\
            _loader = new ExtensionLoader(Dispatcher, HostRevitTag(uiApp));
            _loader.LoadAll(PathProvider.ExtensionsDirectory);

            _initialized = true;
        }

        /// <summary>Reloads extension command DLLs (collectible contexts) so changed code takes effect
        /// without restarting Revit. No-op until Initialize has run.</summary>
        public static void ReloadExtensions()
        {
            if (!_initialized) return;
            _loader.UnloadAll();
            _loader.LoadAll(PathProvider.ExtensionsDirectory);
        }

        /// <summary>Maps the running Revit version to the manifest's targetRevit tag, e.g. "2025" -&gt; "R25".</summary>
        private static string HostRevitTag(UIApplication uiApp)
        {
            string version = uiApp.Application.VersionNumber;   // e.g. "2025"
            return version.Length >= 4 ? $"R{version.Substring(2)}" : version;
        }
    }
}
