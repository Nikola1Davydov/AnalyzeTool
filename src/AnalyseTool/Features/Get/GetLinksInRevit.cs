using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Get
{
    [RevitCommand(
        Description = "Returns linked content in the document: Revit links and linked CAD files " +
                      "(each with id and name).",
        ReadOnly = true)]
    internal sealed class GetLinksInRevit : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app => new LinksService().GetLinks(app.ActiveUIDocument.Document));
    }
}
