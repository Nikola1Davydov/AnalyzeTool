using AnalyseTool.Sdk;

namespace AnalyseTool.App.Features
{
    /// <summary>Opens a native folder picker (on the Revit UI thread) and returns the chosen path.
    /// A host command (not Core): it shows a WPF dialog, and Core is headless by design.</summary>
    [RevitCommand(
        Description = "Opens a folder picker and returns the selected folder path (or null if cancelled).",
        HiddenFromMcp = true)]
    internal sealed class BrowseForFolder : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(_ =>
            {
                Microsoft.Win32.OpenFolderDialog dialog = new()
                {
                    Title = "Select a folder",
                    Multiselect = false,
                };

                bool? ok = dialog.ShowDialog();
                return new { path = ok == true ? dialog.FolderName : null };
            });
    }
}
