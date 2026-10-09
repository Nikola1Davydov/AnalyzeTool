using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.SharedParameters
{
    [RevitCommand(
        Description = "Makes a file Revit's shared parameter file (as Manage → Shared Parameters → Browse does) and " +
                      "returns it read like GetSharedParameterFile. With create=true a missing file is created empty " +
                      "first. Changes a Revit setting, not the model.",
        InputType = typeof(SetSharedParameterFile.Request),
        OutputType = typeof(SharedParameterFileData),
        HiddenFromMcp = true)] // the editor's "open / new file"; an agent reads and saves by path
    internal sealed class SetSharedParameterFile : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? request = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(request?.Path)) throw new InvalidOperationException("No file path given.");

            return ctx.RunInRevitAsync<object?>(app =>
            {
                string path = Path.GetFullPath(request!.Path);
                if (!File.Exists(path))
                {
                    if (!request.Create) throw new InvalidOperationException($"File not found: {path}");
                    SharedParameterFile.CreateEmpty().Save(path);
                }
                app.Application.SharedParametersFilename = path;
                return SharedParameterFileService.Read(path, app.Application.SharedParametersFilename);
            });
        }

        internal sealed record Request
        {
            [Description("Full path of the shared parameter file.")]
            public string Path { get; set; } = string.Empty;

            [Description("Create the file (empty) when it does not exist.")]
            public bool Create { get; set; }
        }
    }
}
