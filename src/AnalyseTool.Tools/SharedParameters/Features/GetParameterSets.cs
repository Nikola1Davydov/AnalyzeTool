using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.SharedParameters
{
    [RevitCommand(
        Description = "Returns the user's saved report parameter sets: { path, sets: [{ id, name, parameters: " +
                      "[{ guid, name }], updated }], error }. Read-only; reads a file, not the model.",
        ReadOnly = true,
        OutputType = typeof(ParameterSetsResult),
        HiddenFromMcp = true)] // the report page's own storage
    internal sealed class GetParameterSets : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            Task.FromResult<object?>(new ParameterSetStore().Load());
    }
}
