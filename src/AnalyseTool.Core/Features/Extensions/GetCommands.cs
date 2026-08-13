using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>
    /// Lists every registered command (built-in + from extensions) so a web-extension author can
    /// discover what they may call via <c>AT.invoke(name, payload)</c> and what payload each takes.
    /// Surfaced in the Settings "Commands" table; also handy from the console:
    /// <c>await AT.invoke("GetCommands")</c>.
    /// </summary>
    [RevitCommand(
        Description = "Lists all registered commands (built-in + extensions) callable via AT.invoke, " +
                      "each with its source, description, flags and payload schema.",
        ReadOnly = true,
        HiddenFromMcp = true)] // author/Settings introspection; the AI already gets its own tool list
    internal sealed class GetCommands : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            // ONE scan for the whole listing. Both facts below come from the extension manifests on
            // disk, and all 60-odd rows ask the same two questions.
            IReadOnlyList<ExtensionDescriptor> found = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion);
            HashSet<string> declared = CommandButtons.ManifestDeclared(found);

            // What each extension is made of. The launcher shows generated SCRIPTS and nothing else,
            // because a DLL extension ships its own page and a ribbon button — listing its commands in a
            // generic launcher offers a second, worse way in.
            Dictionary<string, string> kinds = new(StringComparer.OrdinalIgnoreCase);

            // Extensions whose ribbon button opens a PAGE. Their commands are that page's backend — the
            // two IRevitTasks behind one form are one tool in two steps — so the launcher lists the page's
            // button, not the steps. Same rule as the dll exclusion, keyed on what a thing IS rather than
            // how it was built: a generated script that grew a form is exactly as page-owned as a DLL.
            HashSet<string> pageBacked = new(StringComparer.OrdinalIgnoreCase);

            foreach (ExtensionDescriptor descriptor in found)
            {
                kinds[descriptor.Manifest.Id] = descriptor.DeclaresDll ? "dll"
                    : descriptor.HasScript ? "script"
                    : "js";
                if (descriptor.OpensPage) pageBacked.Add(descriptor.Manifest.Id);
            }

            var commands = CoreServices.Queue.RegisteredCommands
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new
                {
                    name = c.Name,
                    source = c.Source, // "core" for built-ins, else the extension id
                    // "core" | "script" | "dll" | "js" — where the command came from, not what it does.
                    kind = string.Equals(c.Source, "core", StringComparison.Ordinal) ? "core"
                        : kinds.TryGetValue(c.Source, out string? k) ? k
                        : "unknown",
                    // This command backs an extension page rather than standing on its own.
                    backsPage = pageBacked.Contains(c.Source),
                    description = c.Description,
                    readOnly = c.ReadOnly,
                    destructive = c.Destructive,
                    exposedToMcp = c.ExposeToMcp,
                    // Whether this command has a ribbon button right now: the user's override if they
                    // made one, else whatever the extension's manifest declares. The launcher renders
                    // the toggle from this, so it must be the EFFECTIVE answer, not either half.
                    onRibbon = CommandButtons.Override(c.Name) ?? declared.Contains(c.Name),
                    // Both whole, uncapped: this is the introspection callers reason about (the Settings
                    // table, and the pipeline graph validator that has to compare one command's output
                    // against the next one's input). The MCP listing gets a compacted copy instead.
                    inputSchema = SafeParse(c.InputSchemaJson),
                    outputSchema = SafeParse(c.OutputSchemaJson),
                })
                .ToList();

            return Task.FromResult<object?>(new { commands });
        }

        private static JToken SafeParse(string json)
        {
            try { return JToken.Parse(json); }
            catch { return JValue.CreateNull(); }
        }
    }
}
