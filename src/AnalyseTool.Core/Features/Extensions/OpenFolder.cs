using AnalyseTool.Sdk;
using System.Diagnostics;
using System.IO;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>Opens a folder in Windows Explorer. Used by the Settings UI for the
    /// "open" affordance on each extension source path and on each installed extension.</summary>
    [RevitCommand(
        Description = "Opens a folder in Windows Explorer.",
        InputType = typeof(OpenFolderPayload),
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class OpenFolder : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            OpenFolderPayload? payload = ctx.Payload.As<OpenFolderPayload>();
            string? path = payload?.Path?.Trim();

            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Path is required.");

            if (!Directory.Exists(path))
                throw new InvalidOperationException($"Folder not found: {path}");

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return Task.FromResult<object?>(null);
        }
    }

    internal sealed class OpenFolderPayload
    {
        public string Path { get; set; } = string.Empty;
    }
}
