using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.SharedParameters
{
    [RevitCommand(
        Description = "Lists the shared and project parameters of the open document: { parameters: [{ id, guid, name, " +
                      "isShared, bound, isInstance, dataType, group, categories }] }. 'bound' = false marks a shared " +
                      "parameter only loaded families carry. Read-only.",
        ReadOnly = true,
        OutputType = typeof(ProjectParametersResult))]
    internal sealed class GetProjectParameters : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app => new ProjectParameterService().List(app.ActiveUIDocument.Document));
    }
}
