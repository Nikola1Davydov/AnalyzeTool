using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>Enables/disables one extension (both zones). The state lives host-side in
    /// <c>extensions-state.json</c> (see <see cref="ExtensionStateStore"/>), and the toggle applies
    /// immediately: commands reload and the ribbon refreshes without a Revit restart.</summary>
    [RevitCommand(
        Description = "Enables or disables an installed extension; applies immediately via reload.",
        InputType = typeof(SetExtensionEnabled.Request),
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class SetExtensionEnabled : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(data?.Id))
                throw new InvalidOperationException("Extension id is required.");

            string id = data.Id.Trim();
            ExtensionStateStore.SetEnabled(id, data.Enabled);

            // Apply now: extension commands unload/reload, and the host's ExtensionsReloaded
            // handler refreshes the ribbon — buttons appear/disappear live.
            CoreServices.ReloadExtensions();

            return Task.FromResult<object?>(new { id, enabled = data.Enabled });
        }

        internal sealed record Request
        {
            [Description("Extension id (as reported by GetInstalledExtensions).")]
            public string Id { get; set; } = string.Empty;

            [Description("True to enable, false to disable.")]
            public bool Enabled { get; set; } = true;
        }
    }
}
