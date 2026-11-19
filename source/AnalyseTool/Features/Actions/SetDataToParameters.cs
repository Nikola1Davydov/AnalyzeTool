using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Actions;
using AnalyseTool.Services.Documents;

namespace AnalyseTool.Features.Actions;

/// <summary>
///     Writes values to element parameters in one transaction.
/// </summary>
[McpTool(Destructive = true)]
[Command(Description =
    "Writes values to element parameters (MODIFIES the model, inside a transaction). " +
    "Payload: { items: [{ elementId, id (parameter id), value }], mode: \"Overwrite\" | \"OnlyIfEmpty\" | \"SkipIfEqual\" }. " +
    "Returns { ok, written, skipped, error, warnings: [{ description, elementIds }], problems: [{ elementId, parameterId, reason }] } — " +
    "'skipped' counts items whose element or parameter was not found, was read-only, could not take the value, or that " +
    "the mode filtered out; 'problems' names the ones that failed with a reason. One bad item never fails the batch: the " +
    "others are written and committed. Parameter ids come from GetCategoryParameters ('id'); a numeric value is taken in " +
    "the document unit that command reports as 'unit' (2400 means 2400 mm on a metric model, not feet). Cost: one " +
    "transaction over the given items.")]
public sealed class SetDataToParameters(ActiveDocument document, ParameterWriteService service)
    : ICommand<SetDataToParametersRequest, SetDataResult>
{
    /// <inheritdoc />
    public Task<SetDataResult> ExecuteAsync(SetDataToParametersRequest request, CommandContext context)
    {
        var items = (request.Items ?? [])
            .Select(item => item is null ? null : new ParameterWrite(item.ElementId, item.Id, item.Value ?? string.Empty))
            .ToList();

        return document.InvokeAsync(revitDocument => service.Write(revitDocument, items, request.Mode), context.CancellationToken);
    }
}

/// <summary>The input of <see cref="SetDataToParameters" />.</summary>
/// <param name="Items">The writes to apply.</param>
/// <param name="Mode">How to treat a parameter that already has a value.</param>
public sealed record SetDataToParametersRequest(
    [property: Description("Parameter writes to apply.")]
    IReadOnlyList<SetParamItem?> Items,
    [property: Description("How to apply: Overwrite (always), OnlyIfEmpty (skip if the parameter has a value), or SkipIfEqual.")]
    SetDataMode Mode = SetDataMode.Overwrite);

/// <summary>One parameter write of <see cref="SetDataToParametersRequest" />.</summary>
/// <param name="ElementId">The element to modify.</param>
/// <param name="Id">The parameter id.</param>
/// <param name="Value">The new value as text.</param>
public sealed record SetParamItem(
    [property: Description("Revit ElementId of the element to modify.")]
    long ElementId,
    [property: Description("Parameter id: a BuiltInParameter integer value, or a ParameterElement ElementId for shared/project params.")]
    long Id,
    [property: Description("New value as a string (parsed according to the parameter's storage type).")]
    string? Value = "");
