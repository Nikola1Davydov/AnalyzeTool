using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using System.ComponentModel;

namespace AnalyseTool.Tools.Families
{
    /// <summary>Renames a family in the active document. Returns ok=false on a duplicate/invalid name.</summary>
    [RevitCommand(
        Description = "Renames a family (by id) in the active document. Returns ok=false with an error " +
                      "message on a duplicate or invalid name.",
        InputType = typeof(RenameFamily.Request))]
    internal sealed class RenameFamily : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
                new FamilyActionsService().RenameFamily(
                    app.ActiveUIDocument.Document, req.Id, req.NewName ?? string.Empty));
        }

        public sealed class Request
        {
            [Description("Family id to rename.")]
            public long Id { get; set; }

            [Description("The new family name.")]
            public string? NewName { get; set; }
        }
    }
}
