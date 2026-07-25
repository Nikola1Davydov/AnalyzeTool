using AnalyseTool.Sdk;

namespace __Namespace__;

[RevitCommand(
    Description = "Returns the active document's title.",
    ReadOnly = true)]
internal sealed class Hello : IRevitTask
{
    public async Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
    {
        var documentName = await revitContext.RunInRevitAsync<string?>(app =>
        {
            var name = app.ActiveUIDocument?.Document.Title ?? "(no active document)";
            return name;
        });
        return documentName;
    }
}
