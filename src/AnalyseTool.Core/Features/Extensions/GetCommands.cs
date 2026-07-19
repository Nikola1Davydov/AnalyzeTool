using AnalyseTool.Core.Common.Bootstrap;
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
            var commands = CoreServices.Dispatcher.RegisteredCommands
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new
                {
                    name = c.Name,
                    source = c.Source, // "core" for built-ins, else the extension id
                    description = c.Description,
                    readOnly = c.ReadOnly,
                    destructive = c.Destructive,
                    exposedToMcp = c.ExposeToMcp,
                    inputSchema = SafeParse(c.InputSchemaJson),
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
