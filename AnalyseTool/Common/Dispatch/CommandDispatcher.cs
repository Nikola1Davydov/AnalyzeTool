using AnalyseTool.Sdk;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace AnalyseTool.Common.Dispatch
{
    internal sealed class CommandDispatcher
    {
        private readonly Dictionary<string, CommandRegistration> _commands
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly RevitTaskHub _hub;

        public CommandDispatcher(RevitTaskHub hub) => _hub = hub;

        public IReadOnlyCollection<CommandRegistration> RegisteredCommands => _commands.Values;

        public bool IsRegistered(string command) => _commands.ContainsKey(command);

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

        public void RegisterBuiltIns(Assembly coreAssembly)
        {
            foreach (Type type in coreAssembly.GetTypes())
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

        public async Task<object?> DispatchAsync(string command, JToken payload, CancellationToken ct)
        {
            if (!_commands.TryGetValue(command, out CommandRegistration? reg))
                throw new InvalidOperationException($"The command '{command}' is not registered.");

            IRevitTask instance = (IRevitTask)Activator.CreateInstance(reg.CommandType)!;
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
            string baseName = attr?.Name ?? type.Name;
            string name = string.IsNullOrEmpty(prefix) ? baseName : $"{prefix}.{baseName}";
            if (_commands.TryGetValue(name, out CommandRegistration? existing))
            {
                UserDialogUtils.Error(
                    $"Command name conflict: '{name}' is already registered from '{existing.Source}'. " +
                    $"Skipping registration from '{source}'.");
                return;
            }

            Type? inputType = GetInputType(type);
            _commands[name] = new CommandRegistration(
                name, type, source,
                attr?.Description,
                attr?.ReadOnly ?? false,
                attr?.Destructive ?? false,
                BuildInputSchema(inputType));
        }

        /// <summary>If the command implements <c>IRevitTask&lt;TInput&gt;</c>, returns TInput (for schema gen).</summary>
        private static Type? GetInputType(Type type)
        {
            Type? iface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRevitTask<>));
            return iface?.GetGenericArguments().FirstOrDefault();
        }

        /// <summary>Generates a JSON Schema for the typed input (via Microsoft.Extensions.AI, already
        /// referenced) so MCP clients know which arguments the command takes. No input → empty object.</summary>
        private static string BuildInputSchema(Type? inputType)
        {
            try
            {
                if (inputType != null)
                    return AIJsonUtilities.CreateJsonSchema(inputType).GetRawText();
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
        string InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}");
}
