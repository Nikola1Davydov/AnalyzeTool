using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Moves a single command between the two places it can live: the ribbon tab, or the launcher's
    /// shared list and nothing else.
    ///
    /// This is the user's control, not the author's — which is why it writes
    /// <see cref="CommandButtons"/> and not the extension's <c>plugin.json</c>. A manifest can only be
    /// written for an extension these commands created, and it does not exist at all for a built-in
    /// command; a preference that only worked for a third of the list would be worse than none.
    /// </summary>
    [RevitCommand(
        Description = "Puts a command on the ribbon or takes it off, leaving its extension untouched. " +
                      "Every command stays callable from the launcher, from MCP and from JS either way " +
                      "— this only decides whether it also gets a button.",
        InputType = typeof(SetCommandButton.Request),
        OutputType = typeof(CommandButtonResult),
        HiddenFromMcp = true)] // where the user's own buttons go is the user's business
    internal sealed class SetCommandButton : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? req = ctx.Payload.As<Request>();
            string command = req?.Command?.Trim() ?? string.Empty;
            if (command.Length == 0)
                return Task.FromResult<object?>(CommandButtonResult.Failed("A command name is required."));

            CommandRegistration? registration = CoreServices.Queue.GetRegistration(command);
            if (registration is null)
                return Task.FromResult<object?>(CommandButtonResult.Failed($"No command named '{command}'."));

            bool declared = CommandButtons.ManifestDeclared(CoreServices.RevitVersion).Contains(command);
            bool wanted = req!.OnRibbon;

            // The store holds OVERRIDES of the manifest, not a copy of the state. When the user's
            // choice already matches what the extension declares, the override is CLEARED instead of
            // written — otherwise an extension that later renames or drops its own button would be
            // silently outvoted by a preference nobody remembers setting.
            if (wanted == declared)
                CommandButtons.Set(command, null);
            else
                CommandButtons.Set(command, wanted, CommandButtons.Label(command), registration.Description);

            CoreServices.RaiseRibbonButtonsChanged();

            return Task.FromResult<object?>(new CommandButtonResult(
                true, command, wanted, declared, null));
        }

        internal sealed class Request
        {
            [Description("Full command name, as listed by GetCommands — e.g. \"niko.sheets.RenameSheets\".")]
            public string? Command { get; set; }

            [Description("True to give the command a ribbon button, false to leave it in the launcher only.")]
            public bool OnRibbon { get; set; }
        }
    }

    internal sealed record CommandButtonResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("command")] string? Command,
        [property: JsonProperty("onRibbon")] bool OnRibbon,
        [property: JsonProperty("declaredByManifest")] bool DeclaredByManifest,
        [property: JsonProperty("error")] string? Error)
    {
        public static CommandButtonResult Failed(string error) => new(false, null, false, false, error);
    }
}
