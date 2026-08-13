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
        /// <summary>The single entry point for command execution. Windows, ribbon and transports
        /// go through this — the CommandDispatcher itself is not exposed on purpose.</summary>
        public static CommandQueue Queue { get; private set; } = null!;
        public static ExtensionLoader Loader { get; private set; } = null!;

        /// <summary>Revit version year ("2025"), captured once at bootstrap. Lets platform commands
        /// resolve version-scoped paths without reaching for an ambient UIApplication — the Revit
        /// API must only ever be touched through IRevitContext.RunInRevitAsync.</summary>
        public static string RevitVersion { get; private set; } = string.Empty;

        public static bool IsInitialized { get; private set; }

        /// <summary>Raised after <see cref="ReloadExtensions"/> completes. The host subscribes to
        /// refresh UI that mirrors the command set (e.g. ribbon extension buttons) — Core itself
        /// has no knowledge of the ribbon.</summary>
        public static event Action? ExtensionsReloaded;

        /// <summary>Raised when what the ribbon SHOWS changed while the commands behind it did not —
        /// the user pinning a command in the launcher, or taking a button away. Deliberately not
        /// <see cref="ExtensionsReloaded"/>: redrawing one button must not unload and rebuild every
        /// collectible load context, which is what a reload does.</summary>
        public static event Action? RibbonButtonsChanged;

        /// <summary>Announces a ribbon-only change. Safe before Initialize — with no host subscribed
        /// there is no ribbon to refresh, and the state itself is already on disk.</summary>
        public static void RaiseRibbonButtonsChanged() => RibbonButtonsChanged?.Invoke();

        public static void Initialize(CommandQueue queue, ExtensionLoader loader, string revitVersion)
        {
            Queue = queue;
            Loader = loader;
            RevitVersion = revitVersion;
            IsInitialized = true;
        }

        /// <summary>Serializes reloads. Every reload path funnels through this class — the ribbon button,
        /// install/update/remove, enable/disable, SaveAsCommand — but they arrive on different threads
        /// (UI thread, MCP thread pool). Two interleaved reloads used to drop load contexts from the
        /// loader's list without unloading them (a permanent leak) and register the same names twice.</summary>
        private static readonly object ReloadGate = new();

        /// <summary>Reloads extension command DLLs (collectible contexts) so changed code takes effect
        /// without restarting Revit. No-op until Initialize has run.</summary>
        public static void ReloadExtensions()
        {
            if (!IsInitialized) return;

            lock (ReloadGate)
            {
                Log.Information("Reloading extensions");
                Loader.UnloadAll();
                Loader.LoadAll();
                Log.Information("Reload done — {CommandCount} commands registered", Queue.RegisteredCommands.Count);
            }

            // Outside the lock: subscribers touch the ribbon (and hop to the Revit UI thread to do it),
            // so holding the gate across them invites a deadlock for no benefit.
            ExtensionsReloaded?.Invoke();
        }
    }
}
