using AnalyseTool.Utils;
using Autodesk.Revit.UI;
using System.Collections.Concurrent;

namespace AnalyseTool.Infrastructure.Extensions
{
    /// <summary>
    /// Lets AdWindows ribbon buttons (whose handlers run on the UI thread, outside a Revit API
    /// context and without a UIApplication) run work with a valid <see cref="UIApplication"/>.
    /// The single ExternalEvent is created once at OnStartup (a valid API context).
    /// </summary>
    internal sealed class RibbonEventHub : IExternalEventHandler
    {
        private static RibbonEventHub? _instance;
        private readonly ConcurrentQueue<Action<UIApplication>> _queue = new();
        private ExternalEvent? _event;

        /// <summary>Call from a valid API context (Launcher OnStartup). Idempotent.</summary>
        public static void Initialize()
        {
            if (_instance != null) return;
            _instance = new RibbonEventHub();
            _instance._event = ExternalEvent.Create(_instance);
        }

        public static void Run(Action<UIApplication> action)
        {
            if (_instance?._event is null) return;
            _instance._queue.Enqueue(action);
            _instance._event.Raise();
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out Action<UIApplication>? action))
            {
                try { action(app); }
                catch (Exception ex) { UserDialogUtils.Error(ex.Message); }
            }
        }

        public string GetName() => "AnalyseTool.RibbonEventHub";
    }
}
