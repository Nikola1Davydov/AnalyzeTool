using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Sdk;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>Reloads extension command DLLs (collectible ALC) and refreshes the ribbon buttons,
    /// all without restarting Revit.</summary>
    [RevitCommand("ReloadExtensions",
        Description = "Reloads extension command DLLs and script sources (collectible ALC) and refreshes " +
                      "ribbon buttons, without restarting Revit. This is the 'apply' step of the authoring " +
                      "loop: after changing an extension's files, reload, then call " +
                      "GetExtensionDiagnostics to see whether it came back. SaveAsCommand reloads by " +
                      "itself, so this is for changes made another way. Cost: unloads and recompiles " +
                      "every script extension. Requires C# execution to be enabled in AnalyseTool Settings.")]
    internal sealed class ReloadExtensionsCommand : IRevitTask
    {
        /// <summary>Wire name, referenced by the MCP bridge to gate this tool's visibility.</summary>
        public const string CommandName = "ReloadExtensions";

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            // Raises ExtensionsReloaded; the host listens and refreshes the ribbon buttons.
            CoreServices.ReloadExtensions();
            return Task.FromResult<object?>(null);
        }
    }
}
