using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.SharedParameters;

namespace AnalyseTool.Features.SharedParameters;

/// <summary>
///     How well parameters are filled in the active document. One Revit call per parameter, so the page shows progress and
///     the user can cancel between parameters.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Evaluates how well parameters are filled in the open document. For each requested parameter, over the elements of the " +
    "categories it is bound to: { documentTitle, parameters: [{ name, guid, isInstance, total, filled, categories: [{ name, " +
    "total, filled }], levels: [{ name, total, filled }], values: [{ value, count }], otherValuesCount, distinctValues, error }] }. " +
    "A type parameter is counted per instance with its type's value. Identify a shared parameter by guid, a project parameter " +
    "by name. Read-only. Cost: one pass over the bound categories per parameter.")]
public sealed class GetParameterReport(ActiveDocument document, ParameterReportService service) : ICommand<GetParameterReportRequest, ParameterReportResult>
{
    /// <inheritdoc />
    public async Task<ParameterReportResult> ExecuteAsync(GetParameterReportRequest request, CommandContext context)
    {
        var requests = (request.Parameters ?? [])
            .Select(parameter => new ParameterReportRequest(Guid.TryParse(parameter.Guid, out var guid) ? guid : null, parameter.Name))
            .ToList();

        var title = await document.InvokeAsync(revitDocument => revitDocument.Title, context.CancellationToken);
        var reports = new List<ParameterReportDto>(requests.Count);
        foreach (var item in requests)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            reports.Add(await document.InvokeAsync(revitDocument => service.BuildOne(revitDocument, item, request.TopValues), context.CancellationToken));
            context.Progress.Report(new ProgressInfo((double)reports.Count / requests.Count, $"{reports.Count} / {requests.Count}"));
        }

        return new ParameterReportResult(title, reports);
    }
}

/// <summary>The input of <see cref="GetParameterReport" />.</summary>
/// <param name="Parameters">The parameters to evaluate.</param>
/// <param name="TopValues">How many of the most frequent values to list per parameter.</param>
public sealed record GetParameterReportRequest(
    [property: Description("Parameters to evaluate.")]
    IReadOnlyList<ParameterRef>? Parameters = null,
    [property: Description("How many of the most frequent values to list per parameter (1–100, default 10).")]
    int TopValues = 10);

/// <summary>A parameter of <see cref="GetParameterReportRequest" />.</summary>
/// <param name="Guid">The GUID of a shared parameter.</param>
/// <param name="Name">The name of a project parameter, which has no GUID.</param>
public sealed record ParameterRef(
    [property: Description("GUID of a shared parameter.")]
    string? Guid = null,
    [property: Description("Name — used for a project parameter, which has no GUID.")]
    string? Name = null);

/// <summary>
///     The user's saved report parameter sets — the report page's own storage, not an MCP tool.
/// </summary>
[Command(Description =
    "Returns the user's saved report parameter sets: { path, sets: [{ id, name, parameters: [{ guid, name }], updated, " +
    "projectId, projectName }], error }. Read-only; reads a file, not the model.")]
public sealed class GetParameterSets(ParameterSetStore store) : ICommand<ParameterSetsResult>
{
    /// <inheritdoc />
    public Task<ParameterSetsResult> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(store.Load());
    }
}

/// <summary>
///     Replaces the user's saved report parameter sets — the report page's own storage, not an MCP tool.
/// </summary>
[Command(Description =
    "Replaces the user's saved report parameter sets with the given list and returns them as stored (new sets get an id). " +
    "Writes a file in the user's AnalyseTool folder, not the model.")]
public sealed class SaveParameterSets(ParameterSetStore store) : ICommand<SaveParameterSetsRequest, ParameterSetsResult>
{
    /// <inheritdoc />
    public Task<ParameterSetsResult> ExecuteAsync(SaveParameterSetsRequest request, CommandContext context)
    {
        return Task.FromResult(store.Save(request.Sets ?? []));
    }
}

/// <summary>The input of <see cref="SaveParameterSets" />.</summary>
/// <param name="Sets">All sets to keep; a set left out is deleted.</param>
public sealed record SaveParameterSetsRequest(IReadOnlyList<ParameterSetDto>? Sets = null);
