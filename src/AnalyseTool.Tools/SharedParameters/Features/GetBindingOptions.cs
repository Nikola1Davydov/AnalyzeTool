using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.SharedParameters
{
    [RevitCommand(
        Description = "What a parameter can be bound to in the open document: { categories: [{ id, name, type }], " +
                      "groups: [{ typeId, label }], defaultGroup } — the inputs of BindSharedParameters. Read-only.",
        ReadOnly = true,
        OutputType = typeof(BindingOptionsResult))]
    internal sealed class GetBindingOptions : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app => new ProjectParameterService().BindingOptions(app.ActiveUIDocument.Document));
    }
}
