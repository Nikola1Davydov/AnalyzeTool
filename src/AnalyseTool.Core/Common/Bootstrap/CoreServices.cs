using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Extensions;
using Serilog;

namespace AnalyseTool.Core.Common.Bootstrap
{
    /// <summary>
    /// Core-side service registry, populated once by the host bootstrap. Platform commands
    /// (GetCommands, ReloadExtensionsCommand, SaveAsCommand, …) reach the dispatcher and the
    /// extension loader through this class, so Core never references the host assembly.
    /// </summary>
    internal static class CoreServices
    {
        public static CommandDispatcher Dispatcher { get; private set; } = null!;
        public static ExtensionLoader Loader { get; private set; } = null!;

        public static bool IsInitialized { get; private set; }

        /// <summary>Raised after <see cref="ReloadExtensions"/> completes. The host subscribes to
        /// refresh UI that mirrors the command set (e.g. ribbon extension buttons) — Core itself
        /// has no knowledge of the ribbon.</summary>
        public static event Action? ExtensionsReloaded;

        public static void Initialize(CommandDispatcher dispatcher, ExtensionLoader loader)
        {
            Dispatcher = dispatcher;
            Loader = loader;
            IsInitialized = true;
        }

        /// <summary>Reloads extension command DLLs (collectible contexts) so changed code takes effect
        /// without restarting Revit. No-op until Initialize has run.</summary>
        public static void ReloadExtensions()
        {
            if (!IsInitialized) return;
            Log.Information("Reloading extensions");
            Loader.UnloadAll();
            Loader.LoadAll();
            Log.Information("Reload done — {CommandCount} commands registered", Dispatcher.RegisteredCommands.Count);
            ExtensionsReloaded?.Invoke();
        }
    }
}
