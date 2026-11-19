using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Features.Inspection;

/// <summary>
///     What an element is made of — its properties and parameters as data, one level at a time, as RevitLookup shows it.
///     Reads only.
/// </summary>
/// <remarks>
///     The way for a caller to look into an element without writing code: other elements come back as ids to inspect next,
///     nested objects as paths. See <see cref="ObjectInspector" /> for what is read and what is not.
/// </remarks>
[McpTool(ReadOnly = true, Title = "Inspect an element")]
[Command(Description =
    "Returns the properties and parameters of an element of the active Revit document, one level deep, as RevitLookup shows them. " +
    "Values: numbers, strings, enums as names, points as [x, y, z] in feet. Other elements come back as references {id, type, name}: " +
    "inspect them by id. Nested objects come back with a path: call again with the same id and that path, e.g. Location.Curve; " +
    "a number in the path is an item of a collection, e.g. Geometry.0. Methods are not called; Geometry is read only by path. " +
    "A property that throws comes back with a note. Parameters (only for the element itself) have the value as the user sees it, " +
    "the storage type and whether they are read-only. limit — items of collections, 20 by default.")]
public sealed class InspectElement(ActiveDocument document) : ICommand<InspectElementRequest, InspectElementResult>
{
    private const int DefaultLimit = 20;

    /// <inheritdoc />
    public Task<InspectElementResult> ExecuteAsync(InspectElementRequest request, CommandContext context)
    {
        if (request.Limit is < 1)
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "limit must be at least 1.");
        }

        return document.InvokeAsync(revitDocument =>
        {
            var element = revitDocument.GetElement(new ElementId(request.Id))
                          ?? throw new CommandException(CommandErrorCodes.NotFound, $"There is no element {request.Id} in the document.");

            var path = request.Path?.Trim() ?? "";
            var inspector = new ObjectInspector(revitDocument, request.Limit ?? DefaultLimit);
            var target = inspector.Resolve(element, path);

            return new InspectElementResult(
                request.Id,
                path,
                target.GetType().FullName ?? target.GetType().Name,
                ObjectInspector.CountOf(target),
                inspector.Members(target, path),
                target is Element self ? inspector.Parameters(self) : null);
        }, context.CancellationToken);
    }
}

/// <summary>The input of <see cref="InspectElement" />.</summary>
/// <param name="Id">The element id (<see cref="ElementId.Value" />).</param>
/// <param name="Path">A nested object of the element: property names joined by dots, numbers for items of collections.</param>
/// <param name="Limit">How many items of a collection to list; 20 when omitted.</param>
public sealed record InspectElementRequest(long Id, string? Path = null, int? Limit = null);

/// <summary>The result of <see cref="InspectElement" />.</summary>
/// <param name="Id">The element id.</param>
/// <param name="Path">The path inspected; empty for the element itself.</param>
/// <param name="Type">The full name of the type of what was inspected.</param>
/// <param name="Count">The number of items, when it is a collection.</param>
/// <param name="Members">Its properties, or the first items of a collection.</param>
/// <param name="Parameters">The parameters, when it is the element itself.</param>
public sealed record InspectElementResult(
    long Id,
    string Path,
    string Type,
    int? Count,
    IReadOnlyList<InspectedMember> Members,
    IReadOnlyList<InspectedParameter>? Parameters);

/// <summary>A property, or an item of a collection, of what <see cref="InspectElement" /> inspected.</summary>
/// <param name="Name">The property name, or <c>[index]</c>.</param>
/// <param name="Type">The type of the value, short.</param>
/// <param name="Value">A number, string, enum name, <c>[x, y, z]</c>, or the first references of a collection of elements.</param>
/// <param name="Reference">The element the value refers to: inspect it by its id.</param>
/// <param name="Path">The path to read the value itself, for nested objects and collections.</param>
/// <param name="Count">The number of items, for collections.</param>
/// <param name="Note">Why there is no value: the property throws, or it is read only by path.</param>
public sealed record InspectedMember(
    string Name,
    string Type,
    object? Value,
    InspectedReference? Reference,
    string? Path,
    int? Count,
    string? Note);

/// <summary>An element a value refers to.</summary>
/// <param name="Id">The element id; for a category, the id of the category.</param>
/// <param name="Type">The class of the element, short; <see langword="null" /> when it is not in the document.</param>
/// <param name="Name">The element name.</param>
public sealed record InspectedReference(long Id, string? Type, string? Name);

/// <summary>A parameter of an element.</summary>
/// <param name="Name">The parameter name, as the user sees it.</param>
/// <param name="Value">The value as the user sees it, with units; <see langword="null" /> when the parameter has none.</param>
/// <param name="Storage">String, Integer, Double or ElementId.</param>
/// <param name="ReadOnly">Whether the parameter cannot be changed.</param>
/// <param name="Reference">The element an ElementId parameter refers to.</param>
public sealed record InspectedParameter(string Name, object? Value, string Storage, bool ReadOnly, InspectedReference? Reference);
