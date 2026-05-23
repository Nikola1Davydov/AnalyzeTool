using AnalyseTool.Sdk;

namespace Acme.Sample
{
    /// <summary>
    /// Minimal sample extension command. Registered as "acme.sample.Hello" (extension id from
    /// plugin.json + the [RevitCommand] name) and callable from JS via AT.invoke("acme.sample.Hello").
    /// Demonstrates the one rule: touch the Revit model only inside RunInRevitAsync.
    /// </summary>
    [RevitCommand("Hello")]
    public sealed class HelloRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            return ctx.RunInRevitAsync<object?>(app =>
            {
                Autodesk.Revit.UI.UIDocument uiDoc = app.ActiveUIDocument;
                int selectedCount = uiDoc.Selection.GetElementIds().Count;
                string activeView = uiDoc.Document.ActiveView.Name;

                return new
                {
                    message = "Hello from Acme.Sample!",
                    selectedCount,
                    activeView
                };
            });
        }
    }
}
