using System.Globalization;
using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Visualization;

namespace AnalyseTool.Features.Views;

/// <summary>
///     Draws geometry over the views of the active document — elements, boxes, lines, points — to show what was found or
///     computed. Nothing is added to the model: no elements, no undo step.
/// </summary>
/// <remarks>
///     Every call adds to what is shown, in its own colour, so groups can be told apart; <see cref="ClearGeometry" /> removes
///     it all. The geometry of an element is read once: when the element changes, show it again.
/// </remarks>
[McpTool(ReadOnly = true, Title = "Show geometry in the views")]
[Command(Description =
    "Draws geometry over the 3D views and plans of the active Revit document without changing the model: no elements, no undo step. " +
    "In a plan, what lies above the cut plane is pressed onto it, so a cut element shows as its footprint. " +
    "ids — elements, drawn with their solids and edges slightly outside the element, so it does not hide them; boxes — {min, max}; lines — {from, to}; points — drawn as small crosses. " +
    "Coordinates are [x, y, z] in feet, the internal units of Revit, as bounding boxes and locations report them. " +
    "color — #RRGGBB, red by default; transparency — of faces, 0 to 100, 50 by default. " +
    "Each call adds to what is shown: use a colour per group, then RenderView to look. ClearGeometry removes everything. " +
    "Ids that are not in the document or have no geometry are returned as missing.")]
public sealed class ShowGeometry(ActiveDocument document, IGeometryOverlay overlay) : ICommand<ShowGeometryRequest, ShowGeometryResult>
{
    /// <inheritdoc />
    public Task<ShowGeometryResult> ExecuteAsync(ShowGeometryRequest request, CommandContext context)
    {
        var color = ParseColor(request.Color);
        var boxes = request.Boxes ?? [];
        var lines = request.Lines ?? [];
        var points = request.Points ?? [];

        // Checked before Revit is reached: a wrong point fails the whole call, nothing half-drawn
        foreach (var box in boxes)
        {
            ToPoint(box.Min, "boxes.min");
            ToPoint(box.Max, "boxes.max");
        }

        foreach (var line in lines)
        {
            ToPoint(line.From, "lines.from");
            ToPoint(line.To, "lines.to");
        }

        foreach (var point in points)
        {
            ToPoint(point, "points");
        }

        return document.InvokeAsync(revitDocument =>
        {
            var geometry = new OverlayGeometry(color, request.Transparency);
            var missing = new List<long>();

            foreach (var id in (request.Ids ?? []).Distinct())
            {
                if (revitDocument.GetElement(new ElementId(id)) is not { } element || !geometry.Add(element))
                {
                    missing.Add(id);
                }
            }

            foreach (var box in boxes)
            {
                geometry.Add(new BoundingBoxXYZ { Min = ToPoint(box.Min, "boxes.min"), Max = ToPoint(box.Max, "boxes.max") });
            }

            foreach (var line in lines)
            {
                geometry.Add(ToPoint(line.From, "lines.from"), ToPoint(line.To, "lines.to"));
            }

            foreach (var point in points)
            {
                geometry.Add(ToPoint(point, "points"));
            }

            overlay.Show(revitDocument, geometry);
            return new ShowGeometryResult(geometry.IsEmpty ? 0 : 1, missing, overlay.Count);
        }, context.CancellationToken);
    }

    private static XYZ ToPoint(IReadOnlyList<double>? point, string name)
    {
        if (point is not { Count: 3 } || point.Any(value => !double.IsFinite(value)))
        {
            throw new CommandException(CommandErrorCodes.BadRequest, $"Each of {name} must be [x, y, z] in feet.");
        }

        return new XYZ(point[0], point[1], point[2]);
    }

    private static Color ParseColor(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? ShowGeometryRequest.DefaultColor : value.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            throw new CommandException(CommandErrorCodes.BadRequest, $"The color '{value}' is not #RRGGBB.");
        }

        return new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}

/// <summary>The input of <see cref="ShowGeometry" />. Coordinates are <c>[x, y, z]</c> in feet.</summary>
/// <param name="Ids">Elements to draw with their geometry (<see cref="ElementId.Value" />).</param>
/// <param name="Boxes">Boxes by their corners.</param>
/// <param name="Lines">Line segments.</param>
/// <param name="Points">Points, drawn as small crosses.</param>
/// <param name="Color">The colour, <c>#RRGGBB</c>.</param>
/// <param name="Transparency">The transparency of faces, 0 (opaque) to 100.</param>
public sealed record ShowGeometryRequest(
    IReadOnlyList<long>? Ids = null,
    IReadOnlyList<GeometryBox>? Boxes = null,
    IReadOnlyList<GeometryLine>? Lines = null,
    IReadOnlyList<IReadOnlyList<double>>? Points = null,
    string? Color = null,
    int Transparency = 50)
{
    /// <summary>The colour when none is given: red.</summary>
    public const string DefaultColor = "FF0000";
}

/// <summary>A box of <see cref="ShowGeometryRequest" />.</summary>
/// <param name="Min">The corner with the smallest coordinates.</param>
/// <param name="Max">The corner with the largest coordinates.</param>
public sealed record GeometryBox(IReadOnlyList<double> Min, IReadOnlyList<double> Max);

/// <summary>A line segment of <see cref="ShowGeometryRequest" />.</summary>
/// <param name="From">The start point.</param>
/// <param name="To">The end point.</param>
public sealed record GeometryLine(IReadOnlyList<double> From, IReadOnlyList<double> To);

/// <summary>The result of <see cref="ShowGeometry" />.</summary>
/// <param name="Added">How many geometries this call added: 1, or 0 when there was nothing to draw.</param>
/// <param name="Missing">The ids that are not in the document or have no geometry.</param>
/// <param name="Shown">How many geometries are shown now, from every call.</param>
public sealed record ShowGeometryResult(int Added, IReadOnlyList<long> Missing, int Shown);
