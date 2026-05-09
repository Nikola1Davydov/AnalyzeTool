using AnalyseTool.Utils;
using System.Reflection;

namespace AnalyseTool.Common.FeaturesBase
{
    internal class CommandsFactory
    {
        private static readonly Dictionary<string, Type> _commands = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IRevitTask).IsAssignableFrom(t))
            .ToDictionary(
                x => x.Name,
                x => x,
                StringComparer.OrdinalIgnoreCase);

        public static IRevitTask CreateRevitCommand(string command)
        {
            if (!_commands.TryGetValue(command, out Type? commandType))
            {
                UserDialogUtils.Error($"The command {command} is not recognized.");
                throw new NotImplementedException($"The command {command} is not registered.");
            }

            return (IRevitTask)Activator.CreateInstance(commandType)!;
        }
    }
}
