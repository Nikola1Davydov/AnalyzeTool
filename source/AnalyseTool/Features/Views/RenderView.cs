using System.IO;
using Autodesk.Revit.UI;
using AnalyseTool.Services.Documents;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Features.Views;

/// <summary>
///     A picture of what the active view shows in the Revit window — the eyes of an AI client: it selects, changes or
///     colours elements, then looks at the result.
/// </summary>
/// <remarks>
///     Revit draws the picture itself (<see cref="Document.ExportImage" /> with the visible region of the window), so other
///     windows over Revit do not matter. The selection is drawn highlighted, so <c>SelectElements</c> and then this command
///     show the user — and the client — what was found.
/// </remarks>
[McpTool(ReadOnly = true, Title = "Picture of the active view")]
[Command(Description =
    "Returns a PNG picture of what the active Revit view shows in its window, to see the model. " +
    "width — in pixels, 1280 by default, 400 to 2000; a larger picture costs more tokens. " +
    "Selected elements are drawn highlighted: select what you found with SelectElements, then look. " +
    "Fails with view_not_exportable for views without graphics, such as schedules.")]
public sealed class RenderView(ActiveDocument document) : ICommand<RenderViewRequest, McpImage>
{
    private const int DefaultWidth = 1280;

    /// <inheritdoc />
    public async Task<McpImage> ExecuteAsync(RenderViewRequest request, CommandContext context)
    {
        var width = Math.Clamp(request.Width ?? DefaultWidth, 400, 2000);
        var folder = Path.Combine(Path.GetTempPath(), $"AnalyseTool-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            var viewName = await document.InvokeUIAsync(uiDocument => Export(uiDocument, folder, width), context.CancellationToken);

            // Revit names the file after the view; the folder is ours alone
            var file = Directory.GetFiles(folder, "*.png").FirstOrDefault()
                       ?? throw new CommandException(ViewErrorCodes.ViewNotExportable, $"Revit wrote no picture of the view '{viewName}'.");

            var data = await File.ReadAllBytesAsync(file, context.CancellationToken);
            return new McpImage(data, "image/png", $"The view '{viewName}', {width} px wide.");
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file still open by Revit or an antivirus: the temp folder is cleaned by Windows
            }
        }
    }

    private static string Export(UIDocument uiDocument, string folder, int width)
    {
        var view = uiDocument.ActiveView;
        var options = new ImageExportOptions
        {
            ExportRange = ExportRange.VisibleRegionOfCurrentView,
            FilePath = Path.Combine(folder, "view"),
            ZoomType = ZoomFitType.FitToPage,
            FitDirection = FitDirectionType.Horizontal,
            PixelSize = width,
            ImageResolution = ImageResolution.DPI_150,
            HLRandWFViewsFileType = ImageFileType.PNG,
            ShadowViewsFileType = ImageFileType.PNG
        };

        try
        {
            uiDocument.Document.ExportImage(options);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException exception)
        {
            throw new CommandException(ViewErrorCodes.ViewNotExportable, $"The view '{view.Name}' cannot be exported as a picture: {exception.Message}");
        }

        return view.Name;
    }
}

/// <summary>The input of <see cref="RenderView" />.</summary>
/// <param name="Width">The width of the picture in pixels; 1280 when omitted.</param>
public sealed record RenderViewRequest(int? Width = null);

/// <summary>
///     The error codes of the view commands.
/// </summary>
public static class ViewErrorCodes
{
    /// <summary>The active view has no graphics to export — a schedule, a browser.</summary>
    public const string ViewNotExportable = "view_not_exportable";
}
