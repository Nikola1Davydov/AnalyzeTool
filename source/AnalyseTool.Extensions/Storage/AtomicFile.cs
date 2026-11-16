using System.IO;

namespace AnalyseTool.Extensions.Storage;

/// <summary>
///     Writes a file of the profile in one step: the text goes to <c>&lt;file&gt;.tmp</c>, which then replaces the file. The
///     replace tries again for a moment while the file is held — an antivirus, the indexer or OneDrive opens a file that was
///     just written and briefly denies the move.
/// </summary>
public static class AtomicFile
{
    /// <summary>Writes <paramref name="text" /> to <paramref name="path" />, creating its folder.</summary>
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 10)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }
}
