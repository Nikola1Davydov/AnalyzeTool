using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.SharedParameters
{
    [RevitCommand(
        Description = "Reads a shared parameter file (the .txt behind Manage → Shared Parameters): { path, exists, " +
                      "isRevitCurrent, stamp, groups: [{ id, name }], parameters: [{ guid, name, dataType, dataCategory, " +
                      "groupId, visible, description, userModifiable, hideWhenNoValue }], error }. Without a path it " +
                      "reads the file Revit currently uses. Read-only.",
        ReadOnly = true,
        InputType = typeof(GetSharedParameterFile.Request),
        OutputType = typeof(SharedParameterFileData))]
    internal sealed class GetSharedParameterFile : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? request = ctx.Payload.As<Request>();
            return ctx.RunInRevitAsync<object?>(app =>
            {
                string? current = app.Application.SharedParametersFilename;
                string? path = string.IsNullOrWhiteSpace(request?.Path) ? current : request!.Path;
                return SharedParameterFileService.Read(path, current);
            });
        }

        internal sealed record Request
        {
            [Description("Full path of the file to read; omit for the file Revit currently uses.")]
            public string? Path { get; set; }
        }
    }
}
