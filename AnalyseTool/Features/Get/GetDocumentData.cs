using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Features.Get
{
    [RevitCommand(
        Description = "Returns summary information about the active Revit document (title, path and related metadata).",
        ReadOnly = true)]
    internal sealed class GetDocumentData : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;
                return new DocumentData
                {
                    Name = doc.Title,
                    Id = doc.CreationGUID.ToString()
                };
            });


    }
    internal sealed record DocumentData
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
    }
}
