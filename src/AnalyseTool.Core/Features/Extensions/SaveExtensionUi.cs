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
    /// Writes the web half of an extension: an HTML page (plus whatever CSS/JS goes with it) and the
    /// manifest entry that puts it behind a ribbon button.
    ///
    /// SaveAsCommand covers C# and only C#, so an agent that could generate a whole form had no way to
    /// save it and had to hand the files to a person to copy. The two together are the point: save the
    /// backend command with SaveAsCommand, save the form here, and the button opens the form while the
    /// form calls the command through <c>AT.invoke</c>.
    ///
    /// Hand-authored pages only. Files are written flat into the extension folder, so a framework build
    /// with an assets/ tree does not belong here — that is a project a person builds and installs.
    /// </summary>
    [RevitCommand(
        Description = "Saves a web UI into an extension folder: an entry HTML page plus any CSS/JS " +
                      "beside it, and the plugin.json entry that gives it a ribbon button. Use with " +
                      "SaveAsCommand to build a command WITH a form — save the C# first, the page " +
                      "second, and the button will open the page while the page calls the command via " +
                      "AT.invoke('<id>.<Command>', payload). Pass overwrite:true to replace an existing " +
                      "page. Files are written flat, so hand-authored HTML/CSS/JS only — not a " +
                      "framework build with an assets folder. Requires C# execution to be enabled in " +
                      "AnalyseTool Settings.",
        Destructive = true,
        InputType = typeof(SaveExtensionUi.Request),
        OutputType = typeof(SaveUiResult))]
    internal sealed class SaveExtensionUi : IRevitTask
    {
        /// <summary>Wire name, referenced by the MCP bridge to gate this tool's visibility.</summary>
        public const string CommandName = nameof(SaveExtensionUi);

        /// <summary>What may be written. An allowlist, not a denylist: the extensions folder is loaded
        /// by the host, so the question is not "is this dangerous" but "is this one of the few kinds of
        /// file a hand-written page is made of".</summary>
        private static readonly string[] AllowedExtensions =
            { ".html", ".htm", ".css", ".js", ".mjs", ".json", ".svg", ".md", ".txt" };

        private const int MaxFileChars = 512 * 1024;
        private const int MaxFiles = 20;

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            if (!CodeExecutionSettings.Enabled)
                return Task.FromResult<object?>(SaveUiResult.Failed(
                    "C# code execution is disabled. Enable it in AnalyseTool Settings to save extension files."));

            Request? req = ctx.Payload.As<Request>();
            if (req is null || req.Files is not { Count: > 0 })
                return Task.FromResult<object?>(SaveUiResult.Failed("No files provided."));
            if (req.Files.Count > MaxFiles)
                return Task.FromResult<object?>(SaveUiResult.Failed($"Too many files (max {MaxFiles})."));
            if (string.IsNullOrWhiteSpace(req.Id))
                return Task.FromResult<object?>(SaveUiResult.Failed("Extension id is required."));

            string id = req.Id!.Trim();
            if (!ExtensionFolder.IsValidId(id))
                return Task.FromResult<object?>(SaveUiResult.Failed(
                    "Id may contain only letters, digits, '.', '-' and '_'."));

            string entryHtml = string.IsNullOrWhiteSpace(req.EntryHtml) ? "index.html" : req.EntryHtml!.Trim();
            foreach (UiFile file in req.Files)
            {
                string? problem = Validate(file);
                if (problem is not null) return Task.FromResult<object?>(SaveUiResult.Failed(problem));
            }
            if (!req.Files.Any(f => string.Equals(f.Name?.Trim(), entryHtml, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<object?>(SaveUiResult.Failed(
                    $"None of the files is named '{entryHtml}'. The entry page has to be among them."));

            // Same rule as SaveAsCommand: an id that already exists resolves to its own folder. A page
            // saved for a command that lives in a shared folder belongs beside that command, not in a
            // second folder of the same name that shadows it.
            SaveTarget target = ExtensionFolder.ResolveSaveDirectory(id, req.TargetRoot);
            if (target.Directory is null)
                return Task.FromResult<object?>(SaveUiResult.Failed(target.Error!));

            string directory = target.Directory;

            bool exists = Directory.Exists(directory);
            if (exists && !req.Overwrite && File.Exists(Path.Combine(directory, entryHtml)))
                return Task.FromResult<object?>(SaveUiResult.Failed(
                    $"'{id}' already has a page at {entryHtml}. Pass overwrite:true to replace it."));
            if (exists && !ExtensionFolder.IsGeneratedFolder(directory))
                return Task.FromResult<object?>(SaveUiResult.Failed(
                    $"'{id}' exists but was not created by these commands — it holds files they never " +
                    "write. Refusing to add to it; choose another id."));

            Directory.CreateDirectory(directory);
            List<string> written = new();
            foreach (UiFile file in req.Files)
            {
                string name = file.Name!.Trim();
                File.WriteAllText(Path.Combine(directory, name), file.Content ?? string.Empty);
                written.Add(name);
            }

            // Merged, not rewritten: the C# side may already have put a command and a button here.
            // The writer clears button.command because the folder now has a page — see the rule there.
            ExtensionManifestWriter.Write(directory, id, new ManifestEdit
            {
                ButtonName = req.Name,
                Tooltip = req.Description,
                Tab = req.Tab,
                Panel = req.Panel,
                EntryHtml = entryHtml,
                Dockable = req.Dockable,
            });

            Serilog.Log.Information("SaveExtensionUi: wrote {Count} file(s) for {Id} at {Directory}",
                written.Count, id, directory);
            CoreServices.ReloadExtensions();

            return Task.FromResult<object?>(new SaveUiResult(
                true, id, directory, entryHtml, written, null));
        }

        private static string? Validate(UiFile file)
        {
            string? problem = ExtensionFolder.ValidateFileName(file.Name, AllowedExtensions);
            if (problem is not null) return problem;

            return (file.Content?.Length ?? 0) > MaxFileChars
                ? $"'{file.Name?.Trim()}' is larger than {MaxFileChars / 1024} KB."
                : null;
        }

        internal sealed class Request
        {
            [Description("Extension id — the folder name and the command namespace, e.g. \"niko.sheets\". " +
                         "Use the SAME id as the SaveAsCommand call to give a command its own form.")]
            public string? Id { get; set; }

            [Description("Ribbon button label. Required when creating; omit to keep an existing one.")]
            public string? Name { get; set; }

            [Description("Button tooltip.")]
            public string? Description { get; set; }

            [Description("The files to write, flat in the extension folder: " +
                         "[{ name, content }]. One of them must be the entry page.")]
            public List<UiFile>? Files { get; set; }

            [Description("Entry page file name. Default \"index.html\".")]
            public string? EntryHtml { get; set; }

            [Description("Ribbon tab to place the button on. Empty = the default tab.")]
            public string? Tab { get; set; }

            [Description("Ribbon panel to place the button on. Empty = the default panel.")]
            public string? Panel { get; set; }

            [Description("Show the page inside AnalyseTool's shared dockable pane instead of its own " +
                         "window.")]
            public bool Dockable { get; set; }

            [Description("Replace an existing page of the same id. Only a folder these commands created " +
                         "can be written to; anything else is refused.")]
            public bool Overwrite { get; set; }

            [Description("Optional registered source root to save into. Leave it EMPTY: an id that " +
                         "already exists then resolves to its own folder — including a shared team " +
                         "folder — and a new one to the folder chosen in Settings.")]
            public string? TargetRoot { get; set; }
        }
    }

    /// <summary>One file to write into the extension folder.</summary>
    internal sealed class UiFile
    {
        [Description("File name, no folders — e.g. \"index.html\" or \"app.js\".")]
        public string? Name { get; set; }

        [Description("The file's full text content.")]
        public string? Content { get; set; }
    }

    internal sealed record SaveUiResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("id")] string? Id,
        [property: JsonProperty("directory")] string? Directory,
        [property: JsonProperty("entryHtml")] string? EntryHtml,
        [property: JsonProperty("files")] IReadOnlyList<string> Files,
        [property: JsonProperty("error")] string? Error)
    {
        public static SaveUiResult Failed(string error) =>
            new(false, null, null, null, new List<string>(), error);
    }
}
