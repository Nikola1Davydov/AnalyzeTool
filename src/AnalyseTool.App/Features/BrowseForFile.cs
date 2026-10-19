using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.App.Features
{
    /// <summary>Opens a native file picker (on the Revit UI thread) and returns the chosen path.
    /// A host command (not Core): it shows a WPF dialog, and Core is headless by design.</summary>
    [RevitCommand(
        Description = "Opens a file picker — or, with save=true, a save-as dialog — and returns the chosen file path " +
                      "(or null if cancelled).",
        InputType = typeof(BrowseForFile.Request),
        HiddenFromMcp = true)]
    internal sealed class BrowseForFile : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            return ctx.RunInRevitAsync<object?>(_ =>
            {
                string title = string.IsNullOrWhiteSpace(req?.Title) ? "Select a file" : req!.Title;
                string filter = string.IsNullOrWhiteSpace(req?.Filter) ? "All files (*.*)|*.*" : req!.Filter;

                // A save dialog for "new file": it allows a name that does not exist yet and asks before
                // pointing at one that does.
                Microsoft.Win32.FileDialog dialog = req?.Save == true
                    ? new Microsoft.Win32.SaveFileDialog { OverwritePrompt = true, FileName = req.FileName ?? string.Empty }
                    : new Microsoft.Win32.OpenFileDialog { Multiselect = false, CheckFileExists = true };
                dialog.Title = title;
                dialog.Filter = filter;

                bool? ok = dialog.ShowDialog();
                return new { path = ok == true ? dialog.FileName : null };
            });
        }

        internal sealed record Request
        {
            [Description("Dialog title.")]
            public string? Title { get; set; }

            [Description("WPF file dialog filter string, e.g. \"Extension package (*.zip)|*.zip\".")]
            public string? Filter { get; set; }

            [Description("Show a save-as dialog (the file may not exist yet) instead of an open dialog.")]
            public bool Save { get; set; }

            [Description("Suggested file name for the save-as dialog.")]
            public string? FileName { get; set; }
        }
    }
}
