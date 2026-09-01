using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace AnalyseTool.Core.Common.Extensions
{
    /// <summary>What a writer wants changed in <c>plugin.json</c>. Every field is optional: a null means
    /// "leave whatever is already there", which is what lets two commands build one extension between
    /// them — a C# command from SaveAsCommand and a web page from SaveExtensionUi.</summary>
    internal sealed record ManifestEdit
    {
        public string? ButtonName { get; init; }
        public string? Tooltip { get; init; }
        public string? Tab { get; init; }
        public string? Panel { get; init; }

        /// <summary>The command the extension registers, from SaveAsCommand.</summary>
        public string? CommandName { get; init; }

        /// <summary>The page's entry file, from SaveExtensionUi.</summary>
        public string? EntryHtml { get; init; }

        public bool? Dockable { get; init; }

        // ---- Vendor metadata and button shape, from the Edit form in the extension manager. Same
        // ---- null-means-leave rule; for these an EMPTY string means "remove the field" — a person
        // ---- clearing "Website" in a form expects it gone, not kept.
        public string? Description { get; init; }
        public string? Publisher { get; init; }
        public string? Website { get; init; }
        public string? SupportUrl { get; init; }
        public string? UpdateFeed { get; init; }
        /// <summary>Ribbon shape: "push" (written as absent — it is the default), "stacked", "pulldown".</summary>
        public string? Kind { get; init; }
        public int? Order { get; init; }

        /// <summary>Drop the ribbon button entirely. An extension with many commands does not want one
        /// button per command — it wants none, or one page that lists them.</summary>
        public bool RemoveButton { get; init; }
    }

    /// <summary>
    /// Writes <c>plugin.json</c> by MERGING into whatever is already on disk, rather than replacing it.
    /// Two things depend on that. An extension is built by more than one command — the C# side and the
    /// web side arrive separately, and whichever writes second must not erase the first. And a manifest
    /// carries fields no writer here knows about (publisher, icon, updateFeed, entryAssembly); a
    /// from-scratch rewrite silently drops them, which is the kind of loss nobody notices until a
    /// published extension stops updating.
    /// </summary>
    internal static class ExtensionManifestWriter
    {
        public static void Write(string directory, string id, ManifestEdit edit)
        {
            string path = Path.Combine(directory, "plugin.json");
            JObject manifest = Read(path);

            manifest["id"] = id;
            if (manifest["version"] is null) manifest["version"] = "1.0.0";

            SetOrRemove(manifest, "description", edit.Description);
            SetOrRemove(manifest, "publisher", edit.Publisher);
            SetOrRemove(manifest, "website", edit.Website);
            SetOrRemove(manifest, "supportUrl", edit.SupportUrl);
            SetOrRemove(manifest, "updateFeed", edit.UpdateFeed);

            JObject ui = manifest["ui"] as JObject ?? new JObject();
            JObject button = ui["button"] as JObject ?? new JObject();

            Set(button, "name", edit.ButtonName);
            Set(button, "tooltip", edit.Tooltip);
            Set(ui, "tab", edit.Tab);
            Set(ui, "panel", edit.Panel);
            Set(ui, "entryHtml", edit.EntryHtml);
            if (edit.Dockable is bool dockable) ui["dockable"] = dockable;
            if (edit.CommandName is not null) button["command"] = edit.CommandName;
            if (edit.Kind is not null)
            {
                // "push" is the default the host falls back to, so it is written as absence — a manifest
                // should not carry a field that says "the usual".
                string kind = edit.Kind.Trim().ToLowerInvariant();
                if (kind.Length == 0 || kind == "push") button.Remove("kind"); else button["kind"] = kind;
            }
            if (edit.Order is int order)
            {
                if (order == 0) button.Remove("order"); else button["order"] = order;
            }

            // THE RULE, in one place so both writers obey it: an extension with a page opens the page
            // when its ribbon button is clicked, and one without runs its command. The host decides this
            // by whether ui.button.command is set (see RibbonHost.CreateButton), so adding a page has to
            // clear it — otherwise the button someone just built a form for keeps firing the backend
            // command directly and the form is never seen.
            if (ui["entryHtml"] is not null) button.Remove("command");

            // An EMPTY button is not the same as no button: the host treats the presence of ui.button as
            // "this extension has a ribbon entry" (ExtensionDescriptor.HasUi), so writing {} would put a
            // nameless button on the ribbon. A button needs a name to exist at all.
            if (edit.RemoveButton || button["name"] is null)
                ui.Remove("button");
            else
                ui["button"] = button;

            // An extension with no page and no button has no "ui" — do not leave an empty object behind.
            if (ui.HasValues) manifest["ui"] = ui; else manifest.Remove("ui");

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, manifest.ToString(Formatting.Indented));
        }

        private static void Set(JObject target, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            target[name] = value!.Trim();
        }

        /// <summary>Null leaves the field alone; empty removes it; anything else replaces it.</summary>
        private static void SetOrRemove(JObject target, string name, string? value)
        {
            if (value is null) return;
            if (string.IsNullOrWhiteSpace(value)) target.Remove(name);
            else target[name] = value.Trim();
        }

        /// <summary>An unreadable manifest is treated as absent: refusing to write over a corrupt file
        /// would leave the extension permanently unfixable through these commands.</summary>
        private static JObject Read(string path)
        {
            try
            {
                if (File.Exists(path)) return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
            }
            return new JObject();
        }
    }
}
