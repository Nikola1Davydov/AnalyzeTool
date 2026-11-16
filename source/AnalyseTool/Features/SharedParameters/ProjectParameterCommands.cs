using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.SharedParameters;

namespace AnalyseTool.Features.SharedParameters;

/// <summary>
///     The shared and project parameters of the active document.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Lists the shared and project parameters of the open document: { parameters: [{ id, guid, name, isShared, bound, " +
    "isInstance, dataType, group, categories }] }. 'bound' = false marks a shared parameter only loaded families carry. Read-only.")]
public sealed class GetProjectParameters(ActiveDocument document, ProjectParameterService service) : ICommand<ProjectParametersResult>
{
    /// <inheritdoc />
    public Task<ProjectParametersResult> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync(service.List, context.CancellationToken);
    }
}

/// <summary>
///     What a parameter can be bound to in the active document: the inputs of <see cref="BindSharedParameters" />.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "What a parameter can be bound to in the open document: { categories: [{ id, name, type }], groups: [{ typeId, label }], " +
    "defaultGroup } — the inputs of BindSharedParameters. Read-only.")]
public sealed class GetBindingOptions(ActiveDocument document, ProjectParameterService service) : ICommand<BindingOptionsResult>
{
    /// <inheritdoc />
    public Task<BindingOptionsResult> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync(service.BindingOptions, context.CancellationToken);
    }
}

/// <summary>
///     Adds parameters of Revit's shared parameter file to the active document.
/// </summary>
[McpTool(Destructive = true)]
[Command(Description =
    "Adds parameters of Revit's current shared parameter file to the project (MODIFIES the model, one transaction): binds " +
    "each GUID to the categories as instance or type parameter in the given properties group. A parameter already in the " +
    "project keeps its categories and gains these. Category ids and group ids come from GetBindingOptions, GUIDs from " +
    "GetSharedParameterFile. Returns { added, updated, problems: [{ name, reason }] }.")]
public sealed class BindSharedParameters(ActiveDocument document, ProjectParameterService service) : ICommand<BindSharedParametersRequest, BindResult>
{
    /// <inheritdoc />
    public Task<BindResult> ExecuteAsync(BindSharedParametersRequest request, CommandContext context)
    {
        if (request.Guids is not { Count: > 0 })
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "No parameters given.");
        }

        var guids = request.Guids
            .Select(text => Guid.TryParse(text, out var guid) ? guid : Guid.Empty)
            .Where(guid => guid != Guid.Empty)
            .ToList();
        var categoryIds = request.CategoryIds ?? [];

        return document.InvokeAsync(
            revitDocument => service.Bind(revitDocument, guids, categoryIds.ToList(), request.IsInstance, request.GroupTypeId),
            context.CancellationToken);
    }
}

/// <summary>The input of <see cref="BindSharedParameters" />.</summary>
/// <param name="Guids">The GUIDs of the shared parameters to add.</param>
/// <param name="CategoryIds">The category ids.</param>
/// <param name="IsInstance">Instance (true) or type (false) parameter.</param>
/// <param name="GroupTypeId">The properties-palette group; Data when omitted.</param>
public sealed record BindSharedParametersRequest(
    [property: Description("GUIDs of the shared parameters to add.")]
    IReadOnlyList<string> Guids,
    [property: Description("Category ElementIds (from GetBindingOptions).")]
    IReadOnlyList<long>? CategoryIds = null,
    [property: Description("Instance parameter (true) or type parameter (false).")]
    bool IsInstance = true,
    [property: Description("Properties-palette group (GroupTypeId from GetBindingOptions); default Data.")]
    string? GroupTypeId = null);
