using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// The read half of the Edit form in the extension manager: what a person may change in an
    /// extension's <c>plugin.json</c>, in the shape the form shows it.
    ///
    /// Distinct from <see cref="GetInstalledExtensions"/> on purpose: the list sanitizes and derives
    /// (a display name falls back to the id, links are stripped when unsafe), while a form has to
    /// show the fields as they are written, or the user "saves" a derived value into the file.
    /// </summary>
    [RevitCommand(
        Description = "Reads the editable fields of one extension's plugin.json for the Edit form: vendor " +
                      "metadata and the single ribbon button. Read-only, no Revit access.",
        ReadOnly = true,
        InputType = typeof(ExtensionIdRequest),
        HiddenFromMcp = true)] // the manager's own form; an agent edits through UpdateExtensionManifest
    internal sealed class GetExtensionManifest : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            ExtensionDescriptor descriptor = EditExtensionManifest.Find(ctx.Payload.As<ExtensionIdRequest>()?.Id);
            ExtensionManifest m = descriptor.Manifest;
            ExtensionUi? ui = m.Ui;
            // The form edits the SINGULAR button. A manifest that went to ui.buttons has several, each
            // with its own placement — that is a hand-edit, and the form says so instead of guessing
            // which one the user meant.
            bool usesButtons = ui?.Buttons is { Count: > 0 };
            ExtensionButton? button = usesButtons ? null : ui?.Button;

            return Task.FromResult<object?>(new
            {
                id = m.Id,
                version = m.Version,
                directory = descriptor.Directory,
                // Installed packages belong to their publisher: the next update would overwrite any
                // edit, so the form opens read-only for them and says why.
                editable = descriptor.Zone == ExtensionZone.Dev,
                description = m.Description,
                publisher = m.Publisher,
                website = m.Website,
                supportUrl = m.SupportUrl,
                updateFeed = m.UpdateFeed,
                hasUi = ui is not null,
                usesButtons,
                hasButton = button is not null,
                button = button is null ? null : new
                {
                    name = button.Name,
                    tooltip = button.Tooltip,
                    kind = button.ResolvedKind.ToString().ToLowerInvariant(),
                    order = button.Order,
                    tab = ui!.Tab,
                    panel = ui.Panel,
                    dockable = ui.Dockable,
                },
            });
        }
    }

    /// <summary>
    /// The write half: the Edit form's Save. Merges into <c>plugin.json</c> through
    /// <see cref="ExtensionManifestWriter"/>, so fields the form does not know (entryAssembly, devUrl,
    /// icon, a second button) survive untouched, then reloads so the ribbon picks the change up.
    ///
    /// Not the same command as <see cref="UpdateExtensionManifest"/>, although they share the writer:
    /// that one is the AGENT's tool, gated by the C# switch and limited to folders the agent itself
    /// generated. This one is a person editing their own extension, and neither gate applies — the
    /// only rule is the zone: a dev folder is yours to edit, an installed package is not.
    /// </summary>
    [RevitCommand(
        Description = "Saves the Edit form of the extension manager into an extension's plugin.json: " +
                      "description, publisher, links, update feed, and the ribbon button's name, tooltip, " +
                      "tab, panel, shape and dock setting. Dev-zone extensions only. Reloads extensions " +
                      "afterwards so the ribbon follows.",
        Destructive = true,
        InputType = typeof(EditExtensionManifest.Request),
        HiddenFromMcp = true)]
    internal sealed class EditExtensionManifest : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? throw new InvalidOperationException("Payload is missing.");
            ExtensionDescriptor descriptor = Find(req.Id);

            if (descriptor.Zone != ExtensionZone.Dev)
                throw new InvalidOperationException(
                    $"'{descriptor.Manifest.Id}' is an installed package — its manifest belongs to the publisher, " +
                    "and the next update would overwrite the change. Only your own (dev) extensions can be edited.");

            // A manifest with several buttons is edited by hand; the form never sends button fields for
            // it, but the rule lives here too so a stray payload cannot turn ui.buttons into ui.button.
            bool usesButtons = descriptor.Manifest.Ui?.Buttons is { Count: > 0 };
            bool hasButton = !usesButtons && descriptor.Manifest.Ui?.Button is not null;

            ExtensionManifestWriter.Write(descriptor.Directory, descriptor.Manifest.Id, new ManifestEdit
            {
                Description = req.Description,
                Publisher = req.Publisher,
                Website = req.Website,
                SupportUrl = req.SupportUrl,
                UpdateFeed = req.UpdateFeed,
                // Button fields only where a single button already exists: writing a name into an
                // extension that has none would put a button on the ribbon that opens nothing.
                ButtonName = hasButton ? req.Name : null,
                Tooltip = hasButton ? req.Tooltip : null,
                Tab = hasButton ? req.Tab : null,
                Panel = hasButton ? req.Panel : null,
                Dockable = hasButton ? req.Dockable : null,
                Kind = hasButton ? req.Kind : null,
                Order = hasButton ? req.Order : null,
            });

            Serilog.Log.Information("EditExtensionManifest: rewrote plugin.json for {Id}", descriptor.Manifest.Id);
            CoreServices.ReloadExtensions(); // commands + (via ExtensionsReloaded) the ribbon

            return Task.FromResult<object?>(new { ok = true, id = descriptor.Manifest.Id, directory = descriptor.Directory });
        }

        /// <summary>The extension by id, or an error that names it — shared by both halves.</summary>
        internal static ExtensionDescriptor Find(string? id)
        {
            id = id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("An extension id is required.");
            return ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion)
                       .FirstOrDefault(d => string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"No extension with id '{id}'.");
        }

        internal sealed class Request
        {
            [Description("Extension id, as listed by GetInstalledExtensions.")]
            public string? Id { get; set; }

            [Description("Description shown in listings. Empty removes it; omit to leave it.")]
            public string? Description { get; set; }

            [Description("Publisher / author display name. Empty removes it; omit to leave it.")]
            public string? Publisher { get; set; }

            [Description("Vendor homepage (http/https). Empty removes it; omit to leave it.")]
            public string? Website { get; set; }

            [Description("Support link (http/https). Empty removes it; omit to leave it.")]
            public string? SupportUrl { get; set; }

            [Description("Update feed: \"github:owner/repo\" or an https URL. Empty removes it; omit to leave it.")]
            public string? UpdateFeed { get; set; }

            [Description("Ribbon button label — also the extension's display name. Omit to leave it.")]
            public string? Name { get; set; }

            [Description("Button tooltip. Omit to leave it.")]
            public string? Tooltip { get; set; }

            [Description("Ribbon tab the button sits in. Omit to leave it.")]
            public string? Tab { get; set; }

            [Description("Ribbon panel within the tab. Omit to leave it.")]
            public string? Panel { get; set; }

            [Description("Button shape: \"push\" (large, the default), \"stacked\" (small, three per column) or \"pulldown\". Omit to leave it.")]
            public string? Kind { get; set; }

            [Description("Sort order within the panel: lower comes first; 0 means no preference and goes after the numbered ones. Omit to leave it.")]
            public int? Order { get; set; }

            [Description("Open the page in the shared dock pane instead of its own window. Omit to leave it.")]
            public bool? Dockable { get; set; }
        }
    }

    /// <summary>The one-field payload of the read commands that address an extension by id.</summary>
    internal sealed class ExtensionIdRequest
    {
        [Description("Extension id, as listed by GetInstalledExtensions.")]
        public string? Id { get; set; }
    }
}
