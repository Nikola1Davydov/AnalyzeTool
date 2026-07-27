using AnalyseTool.Sdk;

namespace AnalyseTool.App.Features
{
    /// <summary>Opens a native file picker (on the Revit UI thread) and returns the chosen path.
    /// A host command (not Core): it shows a WPF dialog, and Core is headless by design.</summary>
    [RevitCommand(
        Description = "Opens a file picker and returns the selected file path (or null if cancelled).",
        InputType = typeof(BrowseForFile.Request),
        HiddenFromMcp = true)]
    internal sealed class BrowseForFile : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            return ctx.RunInRevitAsync<object?>(_ =>
            {
                Microsoft.Win32.OpenFileDialog dialog = new()
                {
                    Title = string.IsNullOrWhiteSpace(req?.Title) ? "Select a file" : req!.Title,
                    Filter = string.IsNullOrWhiteSpace(req?.Filter) ? "All files (*.*)|*.*" : req!.Filter,
                    Multiselect = false,
                    CheckFileExists = true,
                };

                bool? ok = dialog.ShowDialog();
                return new { path = ok == true ? dialog.FileName : null };
            });
        }

        internal sealed record Request
        {
            public string? Title { get; set; }

            /// <summary>WPF OpenFileDialog filter string, e.g. "Extension package (*.zip)|*.zip".</summary>
            public string? Filter { get; set; }
        }
    }
}
