using AnalyseTool.Common;
using AnalyseTool.Common.Bootstrap;
using AnalyseTool.Common.Extensions;
using AnalyseTool.Common.Extensions.Scripting;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.IO;
using System.Reflection;
using System.Text;

namespace AnalyseTool.Features.Scripting
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
                      "full IRevitTask. Disabled by default — enable C# execution in AnalyseTool Settings.",
        InputType = typeof(Request),
        Destructive = true)]
    internal sealed class SaveAsCommand : IRevitTask
    {
        public const string CommandName = nameof(SaveAsCommand);

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            if (!CodeExecutionSettings.Enabled)
                throw new InvalidOperationException(
                    "C# code execution is disabled. Enable it in AnalyseTool Settings to save commands.");

            Request? req = ctx.Payload.As<Request>();
            if (req is null || string.IsNullOrWhiteSpace(req.Code))
                throw new InvalidOperationException("No code provided.");
            if (string.IsNullOrWhiteSpace(req.Id))
                throw new InvalidOperationException("Extension id is required.");
            if (string.IsNullOrWhiteSpace(req.Name))
                throw new InvalidOperationException("Button name is required.");

            string id = req.Id.Trim();
            if (!IsValidId(id))
                throw new InvalidOperationException("Id may contain only letters, digits, '.', '-' and '_'.");

            string version = Context.UiApplication.Application.VersionNumber; // year, e.g. "2025"
            string root = ResolveTargetRoot(req.TargetRoot);
            string directory = Path.Combine(root, version, id);
            if (Directory.Exists(directory))
                throw new InvalidOperationException($"An extension folder already exists: {id}");

            // Body → named class (or keep a full class the AI already wrote).
            bool isFullClass = RoslynScriptCompiler.LooksLikeFullCommand(req.Code);
            string className = DeriveClassName(req.Name);
            string source = isFullClass
                ? req.Code
                : BuildCommandClass(req.Code, DeriveNamespace(id), className, req.Description, req.ReadOnly, req.Destructive);

            // Compile once up front so we never write code that won't load.
            ScriptCompileResult compiled = RoslynScriptCompiler.CompileSnippet(source, "validate_" + id, req.Description);
            if (!compiled.Success)
                return Task.FromResult<object?>(new { created = false, error = "Compilation failed.", diagnostics = compiled.Errors });

            // The button must invoke the actual registered command name (<id>.<baseName>), resolved the
            // same way the dispatcher does — from the compiled type's [RevitCommand] name or class name.
            string commandName = $"{id}.{ResolveCommandBaseName(compiled)}";

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Command.cs"), source);
            File.WriteAllText(Path.Combine(directory, "plugin.json"), BuildManifest(id, req, commandName));

            // Reload picks up the new script (compiles it) and the ribbon refresh adds its button.
            AnalyseToolBootstrap.ReloadExtensions();
            RibbonEventHub.Run(uiApp => RibbonHost.RefreshExtensionButtons(uiApp.Application.VersionNumber));

            return Task.FromResult<object?>(new
            {
                created = true,
                directory,
                command = commandName,
            });
        }

        /// <summary>Inspects the compiled assembly to find the command's base name exactly as the
        /// dispatcher will register it (attribute name, else class name).</summary>
        private static string ResolveCommandBaseName(ScriptCompileResult compiled)
        {
            ExtensionLoadContext alc = new("inspect_" + Guid.NewGuid().ToString("N"));
            try
            {
                Assembly assembly = alc.LoadImage(compiled.Assembly!, compiled.Pdb);
                Type? type = assembly.GetTypes().FirstOrDefault(t =>
                    typeof(IRevitTask).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (type is null) return "Command";

                RevitCommandAttribute? attr = type.GetCustomAttribute<RevitCommandAttribute>();
                return string.IsNullOrEmpty(attr?.Name) ? type.Name : attr!.Name!;
            }
            finally
            {
                try { alc.Unload(); } catch { /* lingers until GC; fine */ }
            }
        }

        private static string BuildManifest(string id, Request req, string commandName)
        {
            var manifest = new
            {
                id,
                version = "1.0.0",
                ui = new
                {
                    tab = string.IsNullOrWhiteSpace(req.Tab) ? null : req.Tab!.Trim(),
                    panel = string.IsNullOrWhiteSpace(req.Panel) ? null : req.Panel!.Trim(),
                    button = new
                    {
                        name = req.Name!.Trim(),
                        tooltip = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim(),
                        command = commandName,
                    },
                },
            };

            return JsonConvert.SerializeObject(manifest, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
            });
        }

        /// <summary>Wraps a bare statement body into a complete, re-editable IRevitTask class file.</summary>
        private static string BuildCommandClass(string body, string ns, string className,
            string? description, bool readOnly, bool destructive)
        {
            string desc = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                string.IsNullOrWhiteSpace(description) ? $"{className} command." : description!, quote: true);
            string indentedBody = string.Join("\n",
                body.Replace("\r\n", "\n").Split('\n').Select(line => "            " + line));

            return
                "using System;\n" +
                "using System.Collections.Generic;\n" +
                "using System.Linq;\n" +
                "using System.Threading;\n" +
                "using System.Threading.Tasks;\n" +
                "using Autodesk.Revit.DB;\n" +
                "using Autodesk.Revit.UI;\n" +
                "using AnalyseTool.Sdk;\n\n" +
                $"namespace {ns};\n\n" +
                $"[RevitCommand(Description = {desc}, ReadOnly = {Bool(readOnly)}, Destructive = {Bool(destructive)})]\n" +
                $"public sealed class {className} : IRevitTask\n" +
                "{\n" +
                "    public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>\n" +
                "        ctx.RunInRevitAsync<object?>(uiapp =>\n" +
                "        {\n" +
                "            var uidoc = uiapp.ActiveUIDocument;\n" +
                "            var doc = uidoc != null ? uidoc.Document : null;\n\n" +
                indentedBody + "\n\n" +
                "            return null;\n" +
                "        });\n" +
                "}\n";
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

        private static bool IsValidId(string id) =>
            id.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');

        /// <summary>Returns a registered extension source root (default when unspecified); rejects
        /// anything not registered so we never scaffold where the host wouldn't scan.</summary>
        private static string ResolveTargetRoot(string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return ExtensionSources.DefaultRoot;

            string full = Path.GetFullPath(requested.Trim());
            bool registered = ExtensionSources.Roots()
                .Any(r => string.Equals(Path.GetFullPath(r), full, StringComparison.OrdinalIgnoreCase));

            if (!registered)
                throw new InvalidOperationException($"Target root is not a registered extension source: {requested}");

            return full;
        }

        internal sealed class Request
        {
            /// <summary>The C# to save — a bare body (wrapped into a class) or a full IRevitTask.</summary>
            public string Code { get; set; } = string.Empty;

            /// <summary>Stable extension id / folder name (e.g. "acme.count-walls").</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Ribbon button label.</summary>
            public string Name { get; set; } = string.Empty;

            public string? Description { get; set; }
            public string? Tab { get; set; }
            public string? Panel { get; set; }

            /// <summary>Optional registered source root to save into; empty = default root.</summary>
            public string? TargetRoot { get; set; }

            public bool ReadOnly { get; set; }
            public bool Destructive { get; set; }
        }
    }
}
