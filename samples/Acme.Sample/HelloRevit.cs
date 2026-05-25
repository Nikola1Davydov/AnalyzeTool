using AnalyseTool.Sdk;

namespace Acme.Sample
{
    /// <summary>
    /// Minimal sample extension command. Registered as "acme.sample.Hello" (extension id from
    /// plugin.json + the [RevitCommand] name) and callable from JS via AT.invoke("acme.sample.Hello").
    /// Demonstrates: command metadata via [RevitCommand] (Description/ReadOnly), an input type for the
    /// MCP schema via InputType=typeof(Input), and the one rule: touch the Revit model only inside
    /// RunInRevitAsync.
    /// </summary>
    [RevitCommand("Hello",
        Description = "Greets the caller and reports the active view name and current selection count. " +
                      "Optionally include a name to personalize the greeting.",
        ReadOnly = true,
        InputType = typeof(HelloRevit.Input))]
    public sealed class HelloRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Input? input = ctx.Payload.As<Input>();
            return ctx.RunInRevitAsync<object?>(app =>
            {
                Autodesk.Revit.UI.UIDocument uiDoc = app.ActiveUIDocument;
                int selectedCount = uiDoc.Selection.GetElementIds().Count;
                string activeView = uiDoc.Document.ActiveView.Name;
                string who = string.IsNullOrWhiteSpace(input?.Name) ? "world" : input!.Name;

                return new
                {
                    message = $"Hello, {who}, from Acme.Sample!",
                    selectedCount,
                    activeView
                };
            });
        }

        public sealed record Input
        {
            [System.ComponentModel.Description("Optional name to greet; defaults to \"world\".")]
            public string? Name { get; set; }
        }
    }
}
