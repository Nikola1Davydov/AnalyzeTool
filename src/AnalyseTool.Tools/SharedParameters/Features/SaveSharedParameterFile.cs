using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.SharedParameters
{
    [RevitCommand(
        Description = "Writes a shared parameter file from { path, stamp, groups, parameters } — the shape " +
                      "GetSharedParameterFile returns, edited. The whole list is written: a parameter left out is " +
                      "deleted from the file, a renamed one keeps its GUID. Refuses when the file changed on disk " +
                      "since 'stamp' was read, or when names, GUIDs or group ids collide; keeps the previous file as " +
                      "<file>.bak. Columns and sections the editor does not show are kept. Returns the file as read " +
                      "back. Writes a file shared across projects — not the model.",
        Destructive = true,
        InputType = typeof(SaveSharedParameterFile.Request),
        OutputType = typeof(SharedParameterFileData))]
    internal sealed class SaveSharedParameterFile : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? request = ctx.Payload.As<Request>();
            if (request is null) throw new InvalidOperationException("Empty payload.");

            return ctx.RunInRevitAsync<object?>(app =>
                SharedParameterFileService.Save(request.Path, request.Stamp, request.Groups, request.Parameters,
                    app.Application.SharedParametersFilename));
        }

        internal sealed record Request
        {
            [Description("Full path of the file to write.")]
            public string Path { get; set; } = string.Empty;

            [Description("The stamp GetSharedParameterFile returned; the save is refused if the file changed since. Omit to overwrite regardless.")]
            public string? Stamp { get; set; }

            [Description("All groups of the file.")]
            public List<SharedParameterGroupDto> Groups { get; set; } = new();

            [Description("All parameters of the file.")]
            public List<SharedParameterDto> Parameters { get; set; } = new();
        }
    }
}
