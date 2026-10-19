using Autodesk.Revit.UI;

namespace AnalyseTool.Core.Common.Dispatch
{
    /// <summary>
    /// The way onto the Revit thread, as the dispatcher and command contexts see it. An interface rather
    /// than <see cref="RevitTaskHub"/> itself because that class implements Revit's IExternalEventHandler
    /// and cannot even be loaded without Revit — which kept the dispatcher, and with it reloads and
    /// command registration, out of reach of the Revit-free tests.
    /// </summary>
    internal interface IRevitTaskHub
    {
        Task<T> EnqueueAsync<T>(Func<UIApplication, T> work);
    }
}
