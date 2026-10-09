using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.SharedParameters
{
    [RevitCommand(
        Description = "Replaces the user's saved report parameter sets with the given list and returns them as " +
                      "stored (new sets get an id). Writes a file in the user's AnalyseTool folder, not the model.",
        InputType = typeof(SaveParameterSets.Request),
        OutputType = typeof(ParameterSetsResult),
        HiddenFromMcp = true)] // the report page's own storage
    internal sealed class SaveParameterSets : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? request = ctx.Payload.As<Request>();
            return Task.FromResult<object?>(new ParameterSetStore().Save(request?.Sets ?? new()));
        }

        internal sealed record Request
        {
            [Description("All sets to keep; a set left out is deleted.")]
            public List<ParameterSetDto> Sets { get; set; } = new();
        }
    }
}
