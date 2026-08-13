using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Extensions.Scripting;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Serilog;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;

namespace AnalyseTool.Core.Features.Scripting
{
    /// <summary>
    /// Promotes a working C# snippet (e.g. one just run via <see cref="ExecuteRevitCode"/>) into a
    /// PERMANENT script extension: wraps a bare body into a named IRevitTask class (or saves a full
    /// class as-is), writes <c>Command.cs</c> + <c>plugin.json</c> into a chosen extension root, then
    /// reloads — so the code becomes a ribbon button + a named command callable from JS and MCP.
    ///
    /// Gated by the same C#-execution toggle as ExecuteRevitCode (and hidden from MCP while off).
    /// </summary>
    [RevitCommand(
        Description = "Saves a working C# snippet as a permanent script extension: creates a ribbon button " +
                      "plus a named command callable from JS/MCP, then reloads. The snippet is a bare body or a " +
                      "full IRevitTask. Disabled by default — enable C# execution in AnalyseTool Settings. " +
                      "MODIFIES the extensions on disk and reloads them; it does not touch the Revit model. " +
                      "Cost: compiles and writes files, then a full extension reload.",
        InputType = typeof(Request),
        OutputType = typeof(SaveCommandResult),
        Destructive = true)]
    internal sealed class SaveAsCommand : IRevitTask
    {
        public const string CommandName = nameof(SaveAsCommand);

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            // Every refusal leaves through the same door. This is the command an agent iterates on, and
            // a loop that has to tell a thrown message apart from a returned one gets one of them wrong.
            // (No frontend consumer to break — checked; this command is MCP-only.)
            if (!CodeExecutionSettings.Enabled)
                return Task.FromResult<object?>(SaveCommandResult.Failed(
                    "C# code execution is disabled. Enable it in AnalyseTool Settings to save commands."));

            Request? req = ctx.Payload.As<Request>();
            if (req is null || string.IsNullOrWhiteSpace(req.Code))
                return Task.FromResult<object?>(SaveCommandResult.Failed("No code provided."));
            if (string.IsNullOrWhiteSpace(req.Id))
                return Task.FromResult<object?>(SaveCommandResult.Failed("Extension id is required."));
            if (string.IsNullOrWhiteSpace(req.Name))
                return Task.FromResult<object?>(SaveCommandResult.Failed("Button name is required."));

            string id = req.Id.Trim();
            if (!ExtensionFolder.IsValidId(id))
                return Task.FromResult<object?>(SaveCommandResult.Failed(
                    "Id may contain only letters, digits, '.', '-' and '_'."));

            // Scripts are version-independent, so they live directly under the root (no year folder).
            string? root = ExtensionFolder.ResolveTargetRoot(req.TargetRoot);
            if (root is null)
                return Task.FromResult<object?>(SaveCommandResult.Failed(
                    $"'{req.TargetRoot}' is not a registered extension source. Leave targetRoot empty for the default root."));

            string? directory = ExtensionFolder.ResolveExtensionDirectory(root, id);
            if (directory is null)
                return Task.FromResult<object?>(SaveCommandResult.Failed(
                    "Invalid extension id (path escapes the extensions folder)."));

            // Overwrite is what makes this iterable. Without it "now also group by type" forced a new id
            // or a manual delete, so a generated command could never be refined — and refining is the
            // whole point of generating one. Guarded, though: only a folder that looks like OUR OWN
            // output is replaceable, so a hand-written extension or a DLL extension that happens to
            // share the id is refused rather than quietly flattened.
            bool exists = Directory.Exists(directory);
            if (exists && !req.Overwrite)
                return Task.FromResult<object?>(SaveCommandResult.Failed(
                    $"An extension folder already exists: {id}. Pass overwrite:true to replace it."));
            if (exists && !ExtensionFolder.IsGeneratedFolder(directory))
                return Task.FromResult<object?>(SaveCommandResult.Failed(
                    $"'{id}' exists but was not created by these commands — it holds files they never " +
                    "write. Refusing to overwrite; choose another id."));

            // Body → named class (or keep a full class the AI already wrote).
            bool isFullClass = RoslynScriptCompiler.LooksLikeFullCommand(req.Code);
            string className = DeriveClassName(req.Name);
            string source = isFullClass
                ? req.Code
                : BuildCommandClass(req.Code, DeriveNamespace(id), className, req.Description, req.ReadOnly, req.Destructive);

            // Compile once up front so we never write code that won't load.
            ScriptCompileResult compiled = RoslynScriptCompiler.CompileSnippet(source, "validate_" + id, req.Description);
            if (!compiled.Success)
                return Task.FromResult<object?>(SaveCommandResult.Failed("Compilation failed.", compiled.Errors));

            // The button must invoke the actual registered command name (<id>.<baseName>), resolved the
            // same way the dispatcher does — from the compiled type's [RevitCommand] name or class name.
            CommandShape shape = InspectCommand(compiled);
            string commandName = $"{id}.{shape.BaseName}";

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Command.cs"), source);
            // Merged, not rewritten: a page saved by SaveExtensionUi, and any vendor metadata, must
            // survive a re-save of the code. The writer also decides what the ribbon button does.
            ExtensionManifestWriter.Write(directory, id, new ManifestEdit
            {
                ButtonName = req.Name,
                Tooltip = req.Description,
                Tab = req.Tab,
                Panel = req.Panel,
                CommandName = commandName,
            });

            Log.Information("SaveAsCommand: {Action} command {Command} at {Directory}",
                exists ? "updated" : "created", commandName, directory);

            // Reload picks up the new script (compiles it); the host's ExtensionsReloaded handler
            // refreshes the ribbon so the new button appears.
            CoreServices.ReloadExtensions();

            return Task.FromResult<object?>(new SaveCommandResult(
                true, !exists, commandName, directory, null, null, SchemaWarnings(shape, isFullClass)));
        }

        /// <summary>
        /// A generated command that declares neither InputType nor OutputType is callable but opaque:
        /// over MCP its arguments and its answer both come through as free-form, so it cannot be chained
        /// and its payload cannot be validated. Reported as a WARNING rather than a refusal — the command
        /// works, it is just second-class — and only for a full class, because a wrapped bare body takes
        /// no arguments and returns whatever the body returns, so there is genuinely nothing to declare.
        /// </summary>
        private static IReadOnlyList<string>? SchemaWarnings(CommandShape shape, bool isFullClass)
        {
            if (!isFullClass) return null;

            List<string> warnings = new();
            if (!shape.DeclaresInput)
                warnings.Add("The command declares no InputType. If it reads ctx.Payload, declare the " +
                             "type so callers know what to send and the host can validate it.");
            if (!shape.DeclaresOutput)
                warnings.Add("The command declares no OutputType. Declare the type it returns so callers " +
                             "know the shape without guessing it from the description.");
            return warnings.Count == 0 ? null : warnings;
        }

        /// <summary>What the compiled command turned out to be: the name the dispatcher will register it
        /// under, and whether it described its own input and output.</summary>
        private sealed record CommandShape(string BaseName, bool DeclaresInput, bool DeclaresOutput);

        /// <summary>Inspects the compiled assembly — the name exactly as the dispatcher will resolve it
        /// (attribute name, else class name), plus the declared schemas. Reflection over metadata only:
        /// nothing in the generated command runs here.</summary>
        private static CommandShape InspectCommand(ScriptCompileResult compiled)
        {
            ExtensionLoadContext alc = new("inspect_" + Guid.NewGuid().ToString("N"));
            try
            {
                Assembly assembly = alc.LoadImage(compiled.Assembly!, compiled.Pdb);
                Type? type = assembly.GetTypes().FirstOrDefault(t =>
                    typeof(IRevitTask).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (type is null) return new CommandShape("Command", false, false);

                RevitCommandAttribute? attr = type.GetCustomAttribute<RevitCommandAttribute>();
                string baseName = string.IsNullOrEmpty(attr?.Name) ? type.Name : attr!.Name!;
                return new CommandShape(baseName, attr?.InputType is not null, attr?.OutputType is not null);
            }
            finally
            {
                try { alc.Unload(); } catch { /* lingers until GC; fine */ }
            }
        }


        /// <summary>Wraps a bare statement body into a complete, re-editable IRevitTask class file.</summary>
        private static string BuildCommandClass(string body, string ns, string className,
            string? description, bool readOnly, bool destructive)
        {
            string desc = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                string.IsNullOrWhiteSpace(description) ? $"{className} command." : description!, quote: true);
            // The snippet is pasted inside the RunInRevitAsync lambda, so every line carries that depth.
            string indentedBody = string.Join("\n",
                body.Replace("\r\n", "\n").Split('\n').Select(line => "            " + line));

            // A raw string literal rather than concatenation: this text has to COMPILE — Roslyn builds
            // it at runtime — so a mistake surfaces to the user as a broken generated command. It
            // should therefore read as C# here, not as escaped fragments joined by '+'.
            // The blank line before the closing delimiter is the generated file's trailing newline.
            return $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Autodesk.Revit.DB;
            using Autodesk.Revit.UI;
            using AnalyseTool.Sdk;

            namespace {{ns}};

            [RevitCommand(Description = {{desc}}, ReadOnly = {{Bool(readOnly)}}, Destructive = {{Bool(destructive)}})]
            public sealed class {{className}} : IRevitTask
            {
                public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken) =>
                    revitContext.RunInRevitAsync<object?>(uiapp =>
                    {
                        var uidoc = uiapp.ActiveUIDocument;
                        var doc = uidoc != null ? uidoc.Document : null;

            {{indentedBody}}

                        return null;
                    });
            }

            """;
        }

        private static string Bool(bool value) => value ? "true" : "false";

        /// <summary>"acme.count-walls" → "Acme.CountWalls" (valid namespace).</summary>
        private static string DeriveNamespace(string id)
        {
            IEnumerable<string> parts = id
                .Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Capitalize);
            string ns = string.Join(".", parts);
            return string.IsNullOrEmpty(ns) ? "Script" : ns;
        }

        /// <summary>"Count Walls" → "CountWalls"; falls back to "Command" if nothing usable remains.</summary>
        private static string DeriveClassName(string name)
        {
            StringBuilder sb = new();
            bool upperNext = true;
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else
                {
                    upperNext = true; // word boundary
                }
            }

            string result = sb.ToString();
            if (result.Length == 0 || !char.IsLetter(result[0]))
                result = "Command" + result;
            return result;
        }

        private static string Capitalize(string s) =>
            s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);



        internal sealed class Request
        {
            /// <summary>The C# to save — a bare body (wrapped into a class) or a full IRevitTask.</summary>
            [Description("The C# to save: either a bare method body, which gets wrapped into a class, " +
                         "or a full IRevitTask.")]
            public string Code { get; set; } = string.Empty;

            /// <summary>Stable extension id / folder name (e.g. "acme.count-walls").</summary>
            [Description("Stable extension id, also used as the folder name, e.g. \"acme.count-walls\".")]
            public string Id { get; set; } = string.Empty;

            /// <summary>Ribbon button label.</summary>
            [Description("Ribbon button label.")]
            public string Name { get; set; } = string.Empty;

            [Description("Description of the saved command — becomes its [RevitCommand] Description, " +
                         "so it is what a later caller selects the command by.")]
            public string? Description { get; set; }

            [Description("Ribbon tab to place the button on. Empty = the default tab.")]
            public string? Tab { get; set; }

            [Description("Ribbon panel to place the button on. Empty = the default panel.")]
            public string? Panel { get; set; }

            /// <summary>Optional registered source root to save into; empty = default root.</summary>
            [Description("Optional registered source root to save into. Empty = the default root.")]
            public string? TargetRoot { get; set; }

            [Description("Marks the saved command as read-only, i.e. it does not modify the model.")]
            public bool ReadOnly { get; set; }

            [Description("Marks the saved command as destructive, i.e. it deletes or overwrites.")]
            public bool Destructive { get; set; }

            [Description("Replace an existing command of the same id — how a generated command gets " +
                         "refined. Only a folder created by this command (Command.cs + plugin.json and " +
                         "nothing else) can be replaced; anything else is refused.")]
            public bool Overwrite { get; set; }
        }
    }

    /// <summary>
    /// Outcome of saving a generated command. Typed rather than anonymous because this is the command an
    /// agent iterates on: it has to branch on whether the code compiled, whether it replaced something,
    /// and what to call next — and reading that out of prose is how a loop goes wrong.
    ///
    /// <see cref="Diagnostics"/> are Roslyn errors: the code never reached disk. <see cref="Warnings"/>
    /// are the opposite — the command IS saved and working, but something about it will limit it later.
    /// </summary>
    internal sealed record SaveCommandResult(
        [property: JsonProperty("ok")] bool Ok,
        [property: JsonProperty("created")] bool Created,
        [property: JsonProperty("command")] string? Command,
        [property: JsonProperty("directory")] string? Directory,
        [property: JsonProperty("error")] string? Error,
        [property: JsonProperty("diagnostics")] IReadOnlyList<string>? Diagnostics,
        [property: JsonProperty("warnings")] IReadOnlyList<string>? Warnings)
    {
        public static SaveCommandResult Failed(string error, IReadOnlyList<string>? diagnostics = null) =>
            new(false, false, null, null, error, diagnostics, null);
    }
}
