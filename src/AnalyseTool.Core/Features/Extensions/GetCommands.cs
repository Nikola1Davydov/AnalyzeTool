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
            // Once for the whole listing, not once per command: reading it means scanning every
            // manifest on disk, and all 60-odd rows ask the same question.
            HashSet<string> declared = CommandButtons.ManifestDeclared(CoreServices.RevitVersion);

            var commands = CoreServices.Queue.RegisteredCommands
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new
                {
                    name = c.Name,
                    source = c.Source, // "core" for built-ins, else the extension id
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
