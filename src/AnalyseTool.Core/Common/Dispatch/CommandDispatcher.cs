using AnalyseTool.Sdk;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Reflection;

namespace AnalyseTool.Core.Common.Dispatch
{
    internal sealed class CommandDispatcher
    {
        private readonly Dictionary<string, CommandRegistration> _commands = new(StringComparer.OrdinalIgnoreCase);
        private readonly RevitTaskHub _hub;

        public CommandDispatcher(RevitTaskHub hub) => _hub = hub;

        public IReadOnlyCollection<CommandRegistration> RegisteredCommands => _commands.Values;

        public bool IsRegistered(string command) => _commands.ContainsKey(command);

        /// <summary>Resolved registration for a command name, or null. Used by the CommandQueue to
        /// show a pre-execution gate the command's metadata (ReadOnly/Destructive/…).</summary>
        public CommandRegistration? GetRegistration(string command) =>
            _commands.TryGetValue(command, out CommandRegistration? reg) ? reg : null;

        /// <summary>Removes all extension-provided commands (keeps built-ins) so they can be reloaded.</summary>
        public void ClearExtensions()
        {
            List<string> toRemove = _commands
                .Where(kv => !string.Equals(kv.Value.Source, "core", StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .ToList();

            foreach (string key in toRemove)
                _commands.Remove(key);
        }

        /// <summary>Registers the built-in commands of the given platform assemblies (Core, Tools, App).
        /// All of them share the source "core" — they ship with the plugin, unlike extensions.</summary>
        public void RegisterBuiltIns(params Assembly[] assemblies)
        {
            foreach (Assembly assembly in assemblies)
            foreach (Type type in assembly.GetTypes())
            {
                if (!IsRegistrable(type)) continue;
                TryRegister(type, source: "core", prefix: null);
            }
        }

        public void RegisterExtension(Assembly extensionAssembly, string extensionId)
        {
            foreach (Type type in extensionAssembly.GetTypes())
            {
                if (!IsRegistrable(type)) continue;
                // Extension commands are namespaced as "<id>.<name>" to avoid collisions with
                // core commands and between extensions.
                TryRegister(type, source: extensionId, prefix: extensionId);
            }
        }

        public Task<object?> DispatchAsync(string command, JToken payload, CancellationToken ct) =>
            DispatchAsync(command, payload, ct, progress: null);

        /// <summary>
        /// Dispatches a command, optionally wiring a <paramref name="progress"/> sink. A fresh command
        /// instance is created per call, so injecting the sink into an <see cref="IProgressAware"/> command
        /// is race-free. The sink is bound by the caller (the transport) to the originating window.
        /// </summary>
        public async Task<object?> DispatchAsync(
            string command, JToken payload, CancellationToken ct, IProgress<ProgressInfo>? progress)
        {
            if (!_commands.TryGetValue(command, out CommandRegistration? reg))
                throw new InvalidOperationException($"The command '{command}' is not registered.");

            IRevitTask instance = (IRevitTask)Activator.CreateInstance(reg.CommandType)!;
            if (progress is not null && instance is IProgressAware aware) aware.Progress = progress;

            RevitContext context = new RevitContext(_hub, payload);
            return await instance.ExecuteAsync(context, ct);
        }

        private static bool IsRegistrable(Type type)
        {
            if (type.IsAbstract || type.IsInterface) return false;
            return typeof(IRevitTask).IsAssignableFrom(type);
        }

        private void TryRegister(Type type, string source, string? prefix)
        {
            RevitCommandAttribute? attr = type.GetCustomAttribute<RevitCommandAttribute>();
            // No [RevitCommand] at all, or [RevitCommand] without an explicit name -> use the class name.
            string baseName = string.IsNullOrEmpty(attr?.Name) ? type.Name : attr!.Name!;
            string name = string.IsNullOrEmpty(prefix) ? baseName : $"{prefix}.{baseName}";
            if (_commands.TryGetValue(name, out CommandRegistration? existing))
            {
                // Log-only (no dialog from Core): a conflict means the later registration is skipped,
                // which the author notices in the Settings command list / extension diagnostics.
                Log.Error("Command name conflict: {Name} is already registered from {Existing}; " +
                          "skipping registration from {Source}", name, existing.Source, source);
                return;
            }

            _commands[name] = new CommandRegistration(
                name, type, source,
                attr?.Description,
                attr?.ReadOnly ?? false,
                attr?.Destructive ?? false,
                BuildInputSchema(attr?.InputType),
                ExposeToMcp: !(attr?.HiddenFromMcp ?? false));
        }

        /// <summary>Generates a JSON Schema for the declared input type (via Microsoft.Extensions.AI, already
        /// referenced) so MCP clients know which arguments the command takes. No input → empty object.</summary>
        private static string BuildInputSchema(Type? inputType)
        {
            try
            {
                if (inputType != null)
                {
                    string json = AIJsonUtilities.CreateJsonSchema(inputType).GetRawText();
                    // Keep tools/list small: deeply-nested DTOs (e.g. lists of element/parameter
                    // models) generate huge schemas that bloat every listing without helping the AI
                    // much. Over the cap, fall back to a permissive object schema; the command's
                    // Description carries the shape instead.
                    if (json.Length <= 4096) return json;
                    return "{\"type\":\"object\",\"additionalProperties\":true}";
                }
            }
            catch { /* fall through to the empty-object schema */ }
            return "{\"type\":\"object\",\"properties\":{}}";
        }
    }

    internal sealed record CommandRegistration(
        string Name,
        Type CommandType,
        string Source,
        string? Description = null,
        bool ReadOnly = false,
        bool Destructive = false,
        string InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
        bool ExposeToMcp = true);
}
