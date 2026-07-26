using AnalyseTool.App.Common.Extensions;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.App.Features
{
    /// <summary>Lists the host's togglable ribbon buttons (AnalyseTool / Family Manager / Component)
    /// with their visibility. The Manage stack (Settings / Reload / Report a bug) is not togglable —
    /// the user must always keep a way back into Settings.</summary>
    [RevitCommand(
        Description = "Lists the host ribbon buttons that can be shown/hidden, with their current visibility.",
        ReadOnly = true,
        HiddenFromMcp = true)] // local UI preference, not for the AI
    internal sealed class GetHostButtons : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            var buttons = RibbonHost.StaticButtonInfos()
                .Select(b => new { key = b.Key, name = b.Name, visible = HostButtonState.IsVisible(b.Key) })
                .ToList();
            return Task.FromResult<object?>(new { buttons });
        }
    }

    /// <summary>Shows/hides one host ribbon button. UI-only: the commands behind the button stay
    /// registered and callable (MCP, AT.invoke, dock pane). Applies live, persists across restarts.</summary>
    [RevitCommand(
        Description = "Shows or hides one of the host's main ribbon buttons; applies immediately.",
        InputType = typeof(SetHostButtonVisible.Request),
        HiddenFromMcp = true)] // local UI preference, not for the AI
    internal sealed class SetHostButtonVisible : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(req?.Key))
                throw new InvalidOperationException("Button key is required.");

            string key = req.Key.Trim();
            if (!RibbonHost.StaticButtonInfos().Any(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Unknown host button '{key}'.");

            HostButtonState.SetVisible(key, req.Visible);

            // The ribbon belongs to the Revit UI thread.
            await ctx.RunInRevitAsync<object?>(_ =>
            {
                RibbonHost.ApplyStaticButtonVisibility();
                return null;
            });

            return new { key, visible = req.Visible };
        }

        internal sealed record Request
        {
            [Description("Button key from GetHostButtons (e.g. \"AnalyseToolFamilies\").")]
            public string Key { get; set; } = string.Empty;

            [Description("True to show, false to hide the ribbon button.")]
            public bool Visible { get; set; } = true;
        }
    }
}
