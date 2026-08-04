using AnalyseTool.Sdk;
using Newtonsoft.Json;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Pipelines
{
    /// <summary>
    /// Reads a saved pipeline back, as the editor needs it to open one.
    ///
    /// <para>Returns the file's own JSON rather than a re-serialized <c>PipelineDocument</c>, for the
    /// same reason <see cref="SavePipeline"/> writes the author's bytes: anything the model does not
    /// declare would be silently dropped, so opening a file in the editor and saving it again would
    /// quietly delete whatever it did not understand — including an author's mistake, which is exactly
    /// what has to survive to be fixed. Parsed first all the same, so a file that cannot be read comes
    /// back as an error here rather than as a blank canvas.</para>
    /// </summary>
    [RevitCommand(
        Description = "Returns a saved pipeline's document, for editing. Payload: { name }. " +
                      "Returns { name, path, pipeline } — 'pipeline' being the file's JSON verbatim.",
        ReadOnly = true,
        InputType = typeof(GetPipeline.Request),
        OutputType = typeof(GetPipelineResult))]
    internal sealed class GetPipeline : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();
            string name = req.Name ?? string.Empty;

            string path = PipelineStore.ResolvePath(name);
            if (!File.Exists(path))
                throw new FileNotFoundException($"No pipeline '{name}'. Looked at: {path}");

            string json = File.ReadAllText(path);
            PipelineStore.Parse(json, path); // read-back check only; the TEXT is what is returned

            return Task.FromResult<object?>(new GetPipelineResult(
                Path.GetFileNameWithoutExtension(path), path, Newtonsoft.Json.Linq.JToken.Parse(json)));
        }

        internal sealed class Request
        {
            [Description("Name of a saved pipeline (extension optional), or an absolute path.")]
            public string? Name { get; set; }
        }
    }

    internal sealed record GetPipelineResult(
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("path")] string Path,
        [property: JsonProperty("pipeline")] Newtonsoft.Json.Linq.JToken Pipeline);
}
