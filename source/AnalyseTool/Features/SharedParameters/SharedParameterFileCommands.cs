using System.ComponentModel;
using System.IO;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Threading;
using AnalyseTool.Services.SharedParameters;

namespace AnalyseTool.Features.SharedParameters;

/// <summary>
///     Reads a shared parameter file — by default the one Revit uses. Needs no open document: the file is an application
///     setting, so the command reads it through <see cref="IRevitDispatcher" />, not <c>ActiveDocument</c>.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Reads a shared parameter file (the .txt behind Manage → Shared Parameters): { path, exists, isRevitCurrent, stamp, " +
    "groups: [{ id, name }], parameters: [{ guid, name, dataType, dataCategory, groupId, visible, description, userModifiable, " +
    "hideWhenNoValue }], error }. Without a path it reads the file Revit currently uses. Read-only.")]
public sealed class GetSharedParameterFile(IRevitDispatcher dispatcher) : ICommand<GetSharedParameterFileRequest, SharedParameterFileData>
{
    /// <inheritdoc />
    public async Task<SharedParameterFileData> ExecuteAsync(GetSharedParameterFileRequest request, CommandContext context)
    {
        var current = await dispatcher.InvokeAsync(application => application.Application.SharedParametersFilename, context.CancellationToken);
        var path = string.IsNullOrWhiteSpace(request.Path) ? current : request.Path;
        return SharedParameterFileService.Read(path, current);
    }
}

/// <summary>The input of <see cref="GetSharedParameterFile" />.</summary>
/// <param name="Path">The file to read; the one Revit uses when omitted.</param>
public sealed record GetSharedParameterFileRequest(
    [property: Description("Full path of the file to read; omit for the file Revit currently uses.")]
    string? Path = null);

/// <summary>
///     Writes a shared parameter file: the shape <see cref="GetSharedParameterFile" /> returns, edited.
/// </summary>
[McpTool(Destructive = true)]
[Command(Description =
    "Writes a shared parameter file from { path, stamp, groups, parameters } — the shape GetSharedParameterFile returns, " +
    "edited. The whole list is written: a parameter left out is deleted from the file, a renamed one keeps its GUID. Refuses " +
    "(file_changed) when the file changed on disk since 'stamp' was read, or (invalid_file) when names, GUIDs or group ids " +
    "collide; keeps the previous file as <file>.bak. Columns and sections the editor does not show are kept. Returns the file " +
    "as read back. Writes a file shared across projects — not the model.")]
public sealed class SaveSharedParameterFile(IRevitDispatcher dispatcher) : ICommand<SaveSharedParameterFileRequest, SharedParameterFileData>
{
    /// <inheritdoc />
    public async Task<SharedParameterFileData> ExecuteAsync(SaveSharedParameterFileRequest request, CommandContext context)
    {
        var current = await dispatcher.InvokeAsync(application => application.Application.SharedParametersFilename, context.CancellationToken);
        return SharedParameterFileService.Save(request.Path, request.Stamp, request.Groups ?? [], request.Parameters ?? [], current);
    }
}

/// <summary>The input of <see cref="SaveSharedParameterFile" />.</summary>
/// <param name="Path">The file to write.</param>
/// <param name="Stamp">The stamp the caller read; the save is refused when the file changed since. Null overwrites regardless.</param>
/// <param name="Groups">All groups of the file.</param>
/// <param name="Parameters">All parameters of the file.</param>
public sealed record SaveSharedParameterFileRequest(
    [property: Description("Full path of the file to write.")]
    string Path,
    [property: Description("The stamp GetSharedParameterFile returned; the save is refused if the file changed since. Omit to overwrite regardless.")]
    string? Stamp = null,
    [property: Description("All groups of the file.")]
    IReadOnlyList<SharedParameterGroupDto>? Groups = null,
    [property: Description("All parameters of the file.")]
    IReadOnlyList<SharedParameterDto>? Parameters = null);

/// <summary>
///     Makes a file Revit's shared parameter file, as Manage → Shared Parameters → Browse does — the editor's «open» and
///     «new file». Not an MCP tool: an agent reads and saves by path.
/// </summary>
[Command(Description =
    "Makes a file Revit's shared parameter file (as Manage → Shared Parameters → Browse does) and returns it read like " +
    "GetSharedParameterFile. With create=true a missing file is created empty first. Changes a Revit setting, not the model.")]
public sealed class SetSharedParameterFile(IRevitDispatcher dispatcher) : ICommand<SetSharedParameterFileRequest, SharedParameterFileData>
{
    /// <inheritdoc />
    public async Task<SharedParameterFileData> ExecuteAsync(SetSharedParameterFileRequest request, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "No file path given.");
        }

        var path = Path.GetFullPath(request.Path);
        if (!File.Exists(path))
        {
            if (!request.Create)
            {
                throw new CommandException(CommandErrorCodes.NotFound, $"File not found: {path}");
            }

            SharedParameterFile.CreateEmpty().Save(path);
        }

        var current = await dispatcher.InvokeAsync(application =>
        {
            application.Application.SharedParametersFilename = path;
            return application.Application.SharedParametersFilename;
        }, context.CancellationToken);

        return SharedParameterFileService.Read(path, current);
    }
}

/// <summary>The input of <see cref="SetSharedParameterFile" />.</summary>
/// <param name="Path">The shared parameter file.</param>
/// <param name="Create">Create the file, empty, when it does not exist.</param>
public sealed record SetSharedParameterFileRequest(string Path, bool Create = false);
