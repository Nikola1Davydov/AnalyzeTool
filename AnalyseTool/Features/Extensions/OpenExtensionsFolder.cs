using AnalyseTool.Common;
using AnalyseTool.Sdk;
using System.Diagnostics;
using System.IO;

namespace AnalyseTool.Features.Extensions
{
    /// <summary>Opens the extensions folder in Explorer.</summary>
    [RevitCommand(
        Description = "Opens the extensions folder (%LOCALAPPDATA%\\AnalyseTool\\extensions) in Windows Explorer.",
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class OpenExtensionsFolder : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string folder = PathProvider.ExtensionsDirectory;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            return Task.FromResult<object?>(null);
        }
    }
}
