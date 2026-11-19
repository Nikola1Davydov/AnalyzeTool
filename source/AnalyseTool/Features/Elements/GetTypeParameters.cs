using System.ComponentModel;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Services.Documents;
using AnalyseTool.Services.Elements;

namespace AnalyseTool.Features.Elements;

/// <summary>
///     All type parameters of a batch of types in one call — one round trip for a whole selection.
/// </summary>
[McpTool(ReadOnly = true)]
[Command(Description =
    "Returns ALL type parameters (display values, empty ones included) for a batch of element types. Read-only. Payload: " +
    "{ typeIds: [long] } — type ids from GetElements with elementKind \"types\". Returns { types: [{ typeId, parameters: " +
    "[{ name, value, id, builtInParameter, spec, unit }] }] }; 'id' tells two parameters of one name apart and is what " +
    "SetDataToParameters takes; 'spec' says what the value means (length, area, number, string...) and 'unit' the document " +
    "unit a numeric value is in (millimeters, feet...). Ids that are not types are skipped. Cost: reads only the given types — cheap.")]
public sealed class GetTypeParameters(ActiveDocument document, TypeAndWorksetService service)
    : ICommand<GetTypeParametersRequest, TypeParametersResult>
{
    /// <inheritdoc />
    public Task<TypeParametersResult> ExecuteAsync(GetTypeParametersRequest request, CommandContext context)
    {
        var ids = request.TypeIds ?? [];
        return document.InvokeAsync(revitDocument => service.GetTypeParameters(revitDocument, ids), context.CancellationToken);
    }
}

/// <summary>The input of <see cref="GetTypeParameters" />.</summary>
/// <param name="TypeIds">The ids of the types.</param>
public sealed record GetTypeParametersRequest(
    [property: Description("Revit ElementIds of the types, e.g. from GetElements with elementKind \"types\".")]
    IReadOnlyList<long>? TypeIds = null);
