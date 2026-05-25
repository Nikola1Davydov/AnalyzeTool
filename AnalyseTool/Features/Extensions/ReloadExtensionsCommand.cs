using AnalyseTool.Common.Bootstrap;
using AnalyseTool.Common.Extensions;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features.Extensions
{
    /// <summary>Reloads extension command DLLs (collectible ALC) and refreshes the ribbon buttons,
    /// all without restarting Revit.</summary>
    [RevitCommand("ReloadExtensions",
        Description = "Reloads extension command DLLs (collectible ALC) and refreshes ribbon buttons, " +
                      "without restarting Revit.",
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class ReloadExtensionsCommand : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            AnalyseToolBootstrap.ReloadExtensions();
            RibbonEventHub.Run(uiApp => RibbonHost.RefreshExtensionButtons(GetInstalledExtensions.HostTag()));
            return Task.FromResult<object?>(null);
        }
    }
}
