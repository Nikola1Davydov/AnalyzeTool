using AnalyseTool.Sdk;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Core.Common.Dispatch
{
    internal sealed class RevitContext : IRevitContext
    {
        private readonly IRevitTaskHub _hub;

        public RevitContext(IRevitTaskHub hub, JToken? payload)
        {
            _hub = hub;
            Payload = new RevitPayload(payload);
        }

        public RevitPayload Payload { get; }

        public Task<T> RunInRevitAsync<T>(Func<UIApplication, T> work) => _hub.EnqueueAsync(work);

        public Task RunInRevitAsync(Action<UIApplication> work) =>
            _hub.EnqueueAsync<object?>(app => { work(app); return null; });
    }
}
