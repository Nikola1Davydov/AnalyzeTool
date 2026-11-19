using System.IO;
using Autodesk.Revit.UI;

namespace AnalyseTool.Launcher;

/// <summary>
///     Writes startup and shutdown failures to a file. Works without the add-in's own logging, which may be exactly what
///     failed to load.
/// </summary>
internal static class StartupLog
{
    /// <summary>Appends <paramref name="text" /> to <c>%LOCALAPPDATA%\&lt;add-in&gt;\logs\&lt;Revit version&gt;\startup-error.txt</c>.</summary>
    public static void Write(UIControlledApplication application, string text)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PluginInfo.Name,
            "logs",
            application.ControlledApplication.VersionNumber);

        var path = Path.Combine(directory, "startup-error.txt");

        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Nowhere to write; Revit still reports the failure itself
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
