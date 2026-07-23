using AnalyseTool.Core.Common;
using AnalyseTool.Sdk;
using System.IO;

namespace AnalyseTool.App.Features
{
    /// <summary>
    /// Read-only: the plugin's CHANGELOG.md, shipped next to the plugin DLL by the build/installer.
    /// Backs the Settings window's "What's new" dialog. Returns { markdown, error }.
    /// </summary>
    [RevitCommand(
        Description = "Returns the plugin's changelog as markdown ({ markdown, error }). " +
                      "Does not touch the Revit model.",
        ReadOnly = true,
        HiddenFromMcp = true)]
    internal sealed class GetChangelog : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string path = Path.Combine(PathProvider.RootDirectory, "CHANGELOG.md");
            if (!File.Exists(path))
                return Task.FromResult<object?>(new { markdown = (string?)null, error = "Changelog file not found." });

            try
            {
                return Task.FromResult<object?>(new { markdown = File.ReadAllText(path), error = (string?)null });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object?>(new { markdown = (string?)null, error = ex.Message });
            }
        }
    }
}
