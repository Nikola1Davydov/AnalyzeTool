using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace AnalyseTool.Tools.Elements
{
    [RevitCommand(
        Description = "Returns the active document's title and its stable creation id. Read-only and cheap " +
                      "— it reads no elements. For what a session actually needs up front (units, language, " +
                      "levels, per-category instance counts) call GetModelOverview instead.",
        ReadOnly = true,
        OutputType = typeof(DocumentData))]
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
