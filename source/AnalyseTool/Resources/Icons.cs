using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace AnalyseTool.Resources;

/// <summary>
///     Loads ribbon icons embedded in this assembly.
/// </summary>
/// <remarks>
///     Icons are read from manifest resources rather than WPF pack URIs: a pack URI resolves the assembly by name,
///     which could load a second copy of AnalyseTool.dll outside the Launcher's isolated context.
/// </remarks>
internal static class Icons
{
    /// <summary>
    ///     Sets the small (<c>{name}16.png</c>) and large (<c>{name}32.png</c>) images of the button.
    /// </summary>
    /// <param name="button">The ribbon button.</param>
    /// <param name="name">The common icon name, for example <c>RibbonIcon</c>.</param>
    /// <returns>The same button, for chaining.</returns>
    public static PushButton WithIcons(this PushButton button, string name)
    {
        button.Image = Load($"{name}16.png");
        button.LargeImage = Load($"{name}32.png");
        return button;
    }

    /// <summary>
    ///     Loads the icon with the specified file name from <c>Resources/Icons</c>.
    /// </summary>
    /// <param name="fileName">The icon file name, for example <c>RibbonIcon32.png</c>.</param>
    /// <returns>The frozen image.</returns>
    /// <exception cref="InvalidOperationException">The icon is not embedded in the assembly.</exception>
    /// <summary>Sets a Segoe MDL2 Assets glyph as the icons: crisp at any DPI, no file.</summary>
    public static RibbonButton WithGlyph(this RibbonButton button, string glyph)
    {
        button.Image = Glyph(glyph, 16);
        button.LargeImage = Glyph(glyph, 32);
        return button;
    }

    /// <summary>Renders a Segoe MDL2 Assets glyph in the blue of the AnalyseTool icon.</summary>
    public static ImageSource Glyph(string glyph, int size)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var text = new System.Windows.Media.FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface("Segoe MDL2 Assets"), size * 0.6875, new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)), 1.0);
            context.DrawText(text, new System.Windows.Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static ImageSource Load(string fileName)
    {
        var assembly = typeof(Icons).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Resources.Icons.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"The icon '{resourceName}' is not embedded in {assembly.GetName().Name}.");

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }
}
