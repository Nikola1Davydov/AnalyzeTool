using Autodesk.Revit.UI;
using System.Collections.Concurrent;

namespace AnalyseTool.Common.Dispatch
{
    /// <summary>
    /// Central marshaller onto the Revit thread. One long-lived <see cref="ExternalEvent"/> is
    /// created once (in a valid API context, from Bootstrap); callers enqueue synchronous work from
    /// any thread and await the result. Replaces the single-slot ExternalEventHub for the new
    /// dispatch path.
    /// </summary>
    internal sealed class RevitTaskHub : IExternalEventHandler
    {
        private readonly ConcurrentQueue<Action<UIApplication>> _queue = new();
        private ExternalEvent? _event;

        /// <summary>Must be called from a valid Revit API context (e.g. inside IExternalCommand.Execute).</summary>
        public void Initialize() => _event ??= ExternalEvent.Create(this);

        public Task<T> EnqueueAsync<T>(Func<UIApplication, T> work)
        {
            if (_event is null)
                throw new InvalidOperationException("RevitTaskHub is not initialized.");

            // RunContinuationsAsynchronously: the awaiting continuation must not run inline on the
            // Revit thread inside Execute (it could re-enter or block the event handler).
            TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(app =>
            {
                try { tcs.SetResult(work(app)); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            _event.Raise();
            return tcs.Task;
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out Action<UIApplication>? item))
                item(app);
        }

        public string GetName() => "AnalyseTool.RevitTaskHub";
    }
}
