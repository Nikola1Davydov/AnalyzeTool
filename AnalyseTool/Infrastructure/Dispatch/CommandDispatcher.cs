using AnalyseTool.Sdk;
using AnalyseTool.Utils;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace AnalyseTool.Infrastructure.Dispatch
{
    internal sealed class CommandDispatcher
    {
        private readonly Dictionary<string, CommandRegistration> _commands
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly RevitTaskHub _hub;

        public CommandDispatcher(RevitTaskHub hub) => _hub = hub;

        public IReadOnlyCollection<CommandRegistration> RegisteredCommands => _commands.Values;

        public bool IsRegistered(string command) => _commands.ContainsKey(command);

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
            string baseName = type.GetCustomAttribute<RevitCommandAttribute>()?.Name ?? type.Name;
            string name = string.IsNullOrEmpty(prefix) ? baseName : $"{prefix}.{baseName}";
            if (_commands.TryGetValue(name, out CommandRegistration? existing))
            {
                UserDialogUtils.Error(
                    $"Command name conflict: '{name}' is already registered from '{existing.Source}'. " +
                    $"Skipping registration from '{source}'.");
                return;
            }
            _commands[name] = new CommandRegistration(name, type, source);
        }
    }

    internal sealed record CommandRegistration(string Name, Type CommandType, string Source);
}
