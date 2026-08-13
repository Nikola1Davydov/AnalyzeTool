using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Extensions.Scripting;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Edits a generated extension's <c>plugin.json</c> without touching its code: rename the button,
    /// move it to another tab or panel, or take it off the ribbon entirely.
    ///
    /// The reason this exists is the ribbon, not the manifest. Each extension gets at most ONE button
    /// (the host keys them by extension id), so a session that saves ten commands as ten extensions puts
    /// ten buttons on the ribbon — and there is no reason those ten should not have been ONE extension.
    /// Two commands fix that between them: SaveAsCommand's <c>fileName</c> puts several commands in one
    /// folder, and this one tidies up what the earlier, one-per-folder saves left behind.
    /// </summary>
    [RevitCommand(
        Description = "Edits a generated extension's plugin.json without touching its code: button name, " +
                      "tooltip, ribbon tab and panel, dockable, or removeButton to take it off the ribbon " +
                      "altogether. Use it to tidy up a ribbon that has collected one button per generated " +
                      "command — an extension with no button keeps every command callable from MCP and " +
                      "from JS. Only extensions these commands created can be edited. " +
                      "Requires C# execution to be enabled in AnalyseTool Settings.",
        Destructive = true,
        InputType = typeof(UpdateExtensionManifest.Request),
        OutputType = typeof(UpdateManifestResult))]
    internal sealed class UpdateExtensionManifest : IRevitTask
    {
        /// <summary>Wire name, referenced by the MCP bridge to gate this tool's visibility.</summary>
        public const string CommandName = nameof(UpdateExtensionManifest);

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            if (!CodeExecutionSettings.Enabled)
                return Task.FromResult<object?>(UpdateManifestResult.Failed(
                    "C# code execution is disabled. Enable it in AnalyseTool Settings to edit extensions."));

            Request? req = ctx.Payload.As<Request>();
            string? id = req?.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                return Task.FromResult<object?>(UpdateManifestResult.Failed("An extension id is required."));

            ExtensionDescriptor? descriptor = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion)
                .FirstOrDefault(d => string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));
            if (descriptor is null)
                return Task.FromResult<object?>(UpdateManifestResult.Failed($"No extension with id '{id}'."));

            // Same rule as the save commands: only a folder these commands could have produced. Editing
            // the manifest of an installed third-party extension is someone else's business, and a
            // reload would put back whatever its package says anyway.
            if (!ExtensionFolder.IsGeneratedFolder(descriptor.Directory))
                return Task.FromResult<object?>(UpdateManifestResult.Failed(
                    $"'{id}' was not created by these commands — it holds files they never write. " +
                    "Edit its plugin.json by hand."));

            ExtensionManifestWriter.Write(descriptor.Directory, descriptor.Manifest.Id, new ManifestEdit
            {
                ButtonName = req!.Name,
                Tooltip = req.Tooltip,
                Tab = req.Tab,
                Panel = req.Panel,
                Dockable = req.Dockable,
                RemoveButton = req.RemoveButton,
            });

            Serilog.Log.Information("UpdateExtensionManifest: rewrote plugin.json for {Id}", id);
            CoreServices.ReloadExtensions();

            string manifest = ReadManifest(descriptor.Directory);
            return Task.FromResult<object?>(new UpdateManifestResult(
                true, descriptor.Manifest.Id, descriptor.Directory, manifest, null));
        }

        private static string ReadManifest(string directory)
        {
            try { return File.ReadAllText(Path.Combine(directory, "plugin.json")); }
            catch (IOException) { return string.Empty; }
        }

        internal sealed class Request
        {
            [Description("Extension id, as listed by GetInstalledExtensions or GetExtensionDiagnostics.")]
            public string? Id { get; set; }

            [Description("New ribbon button label. Omit to leave it as it is.")]
            public string? Name { get; set; }

            [Description("New button tooltip. Omit to leave it as it is.")]
            public string? Tooltip { get; set; }

            [Description("Move the button to this ribbon tab. Omit to leave it where it is.")]
            public string? Tab { get; set; }

            [Description("Move the button to this ribbon panel. Omit to leave it where it is.")]
            public string? Panel { get; set; }

            [Description("Show the extension's page in the shared dockable pane instead of its own window.")]
            public bool? Dockable { get; set; }

            [Description("Take the extension off the ribbon entirely. Its commands stay registered and " +
                         "callable from MCP and from JS — only the button goes.")]
            public bool RemoveButton { get; set; }
        }
    }

    internal sealed record UpdateManifestResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("id")] string? Id,
        [property: JsonProperty("directory")] string? Directory,
        [property: JsonProperty("manifest")] string? Manifest,
        [property: JsonProperty("error")] string? Error)
    {
        public static UpdateManifestResult Failed(string error) => new(false, null, null, null, error);
    }
}
