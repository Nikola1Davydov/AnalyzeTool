using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     The review warnings of the active document.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Returns the active document's review warnings: [{ warningDescription, failingElements, additionalElements }] — each " +
    "with its description and the failing/additional element ids. Read-only. Cost: proportional to the number of warnings, " +
    "not to the size of the model.")]
public sealed class GetWarningsInRevit(ActiveDocument document) : ICommand<IReadOnlyList<WarningInRevitModel>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<WarningInRevitModel>> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync<IReadOnlyList<WarningInRevitModel>>(
            revitDocument => revitDocument.GetWarnings()
                .Select(warning => new WarningInRevitModel(
                    warning.GetDescriptionText(),
                    warning.GetFailingElements().Select(id => id.Value).ToList(),
                    warning.GetAdditionalElements().Select(id => id.Value).ToList()))
                .ToList(),
            context.CancellationToken);
    }
}

/// <summary>A review warning of <see cref="GetWarningsInRevit" />.</summary>
/// <param name="WarningDescription">Revit's description text.</param>
/// <param name="FailingElements">The ids of the failing elements.</param>
/// <param name="AdditionalElements">The ids of the additional elements.</param>
public sealed record WarningInRevitModel(string WarningDescription, IReadOnlyList<long> FailingElements, IReadOnlyList<long> AdditionalElements);
