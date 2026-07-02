using AnalyseTool.Infrastructure;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Features.Families
{
    /// <summary>Renames a family type (FamilySymbol). Returns ok=false on a duplicate/invalid name.</summary>
    [RevitCommand(
        Description = "Renames a family type (FamilySymbol, by id) in the active document. Returns " +
                      "ok=false with an error message on a duplicate or invalid name.",
        InputType = typeof(RenameFamilyType.Request))]
    internal sealed class RenameFamilyType : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            return ctx.RunInRevitAsync<object?>(app =>
                new FamilyActionsService().RenameFamilyType(
                    app.ActiveUIDocument.Document, req.Id, req.NewName ?? string.Empty));
        }

        public sealed class Request
        {
            [Description("Family type (FamilySymbol) id to rename.")]
            public long Id { get; set; }

            [Description("The new type name.")]
            public string? NewName { get; set; }
        }
    }
}
