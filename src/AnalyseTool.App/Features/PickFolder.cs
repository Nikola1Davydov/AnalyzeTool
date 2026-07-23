using AnalyseTool.Sdk;

namespace AnalyseTool.App.Features
{
    /// <summary>
    /// Opens a native folder-picker and returns the chosen path (or null if cancelled). Used to add a
    /// library folder from the palette, since a WebView can't show a native folder dialog itself. Runs on
    /// the Revit UI thread (via RunInRevitAsync) because the dialog must be shown there.
    /// </summary>
    [RevitCommand(
        Description = "Shows a native folder-picker dialog and returns the selected folder path (or null).",
        HiddenFromMcp = true)]
    internal sealed class PickFolder : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            return ctx.RunInRevitAsync<object?>(_ =>
            {
                Microsoft.Win32.OpenFolderDialog dialog = new()
                {
                    Title = "Select a Revit family folder",
                    Multiselect = false,
                };
                return new { path = dialog.ShowDialog() == true ? dialog.FolderName : null };
            });
        }
    }
}
