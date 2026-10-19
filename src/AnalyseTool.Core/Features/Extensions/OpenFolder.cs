using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using System.ComponentModel;
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

            // The plugin's own extension folders are created on first use, not at install: "open my
            // extensions folder" on a fresh machine must open an empty folder, not report it missing.
            if (!Directory.Exists(path) && IsOwnRoot(path))
                Directory.CreateDirectory(path);

            if (!Directory.Exists(path))
                throw new InvalidOperationException($"Folder not found: {path}");

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return Task.FromResult<object?>(null);
        }

        private static bool IsOwnRoot(string path) =>
            SamePath(path, ExtensionSources.DefaultDevRoot) || SamePath(path, ExtensionSources.DefaultManagedRoot);

        private static bool SamePath(string a, string b) =>
            string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class OpenFolderPayload
    {
        [Description("Absolute path of an existing folder to reveal in Explorer.")]
        public string Path { get; set; } = string.Empty;
    }
}
