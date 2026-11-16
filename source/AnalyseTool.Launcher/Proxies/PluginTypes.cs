using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace AnalyseTool.Launcher.Proxies;

/// <summary>
///     Finds and creates add-in classes by simple name inside <see cref="App.PluginContext" />.
/// </summary>
internal static class PluginTypes
{
    private static readonly ConcurrentDictionary<(string Name, Type Contract), Type?> Cache = new();

    /// <summary>
    ///     Creates the add-in class named <paramref name="name" /> that implements <typeparamref name="TContract" />.
    /// </summary>
    /// <returns><see langword="true" /> when the instance was created; otherwise <paramref name="error" /> explains why.</returns>
    public static bool TryCreate<TContract>(string name, [NotNullWhen(true)] out TContract? instance, out string error) where TContract : class
    {
        instance = null;

        if (App.PluginContext is null)
        {
            error = $"{PluginInfo.Name} failed to start. The reason is in startup-error.txt in %LOCALAPPDATA%\\{PluginInfo.Name}\\logs.";
            return false;
        }

        Type? type;
        try
        {
            type = Cache.GetOrAdd((name, typeof(TContract)), static key => Find(key.Name, key.Contract));
        }
        catch (InvalidOperationException exception)
        {
            // Two add-in classes with the proxy's name: a message for the user, not an unhandled exception inside Revit
            error = exception.Message;
            return false;
        }

        if (type is null)
        {
            error = $"{PluginInfo.Name} has no {typeof(TContract).Name} class named '{name}'.";
            return false;
        }

        instance = (TContract)Activator.CreateInstance(type)!;
        error = string.Empty;
        return true;
    }

    private static Type? Find(string name, Type contract)
    {
        var candidates = App.PluginContext!.Assemblies
            .SelectMany(GetLoadableTypes)
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name == name && contract.IsAssignableFrom(type))
            .ToList();

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => null,
            _ => throw new InvalidOperationException(
                $"Several {contract.Name} classes are named '{name}': {string.Join(", ", candidates.Select(type => type.FullName))}. Proxy names must be unique.")
        };
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
