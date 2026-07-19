using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using AnalyseTool.Sdk;

namespace AnalyseTool.Tools.Elements
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
