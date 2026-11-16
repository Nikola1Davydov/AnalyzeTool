using System.IO;
using System.Text.Json;
using AnalyseTool.Extensions.Storage;

namespace AnalyseTool.Infrastructure.Ribbon;

/// <summary>
///     <c>%LOCALAPPDATA%\AnalyseTool\pane-profile.json</c>: a file, not the browser storage of WebView2, so it is the same in every
///     Revit version, survives a reset of WebView2 and moves with the rest of the profile folder.
/// </summary>
public static class PaneProfile
{
    private static readonly Lock Gate = new();

    public static string FilePath(ExtensionPaths paths)
    {
        return Path.Combine(paths.ProfileRoot, "pane-profile.json");
    }

    public static JsonElement? Read(ExtensionPaths paths)
    {
        lock (Gate)
        {
            try
            {
                var path = FilePath(paths);
                return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone() : null;
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // A broken profile starts the pane with the defaults; the next save writes a good one
                return null;
            }
        }
    }

    public static void Write(ExtensionPaths paths, JsonElement profile)
    {
        lock (Gate)
        {
            AtomicFile.WriteAllText(FilePath(paths), JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
