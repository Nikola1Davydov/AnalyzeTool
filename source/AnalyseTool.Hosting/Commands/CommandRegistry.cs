using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyseTool.Hosting.Commands;

/// <summary>
///     The registered commands, by name (case-insensitive).
/// </summary>
[PublicAPI]
public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandDescriptor> _static;
    private readonly Dictionary<Type, CommandDescriptor> _staticByType;
    private readonly Dictionary<string, IReadOnlyList<CommandDescriptor>> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private Snapshot _snapshot;

    internal CommandRegistry(IEnumerable<CommandDescriptor> commands, IReadOnlyList<string> warnings)
    {
        var list = commands.ToList();
        _static = list.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
        _staticByType = list.ToDictionary(command => command.CommandType);
        _snapshot = new Snapshot(_static, _staticByType);
        Warnings = warnings;
    }

    /// <summary>
    ///     Raised after <see cref="SetGroup" /> or <see cref="RemoveGroup" /> changed the commands — for transports that
    ///     announce their command list (MCP <c>tools/list_changed</c>).
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Gets all registered commands: the scanned ones and every group.</summary>
    public IReadOnlyCollection<CommandDescriptor> Commands => _snapshot.ByName.Values;

    /// <summary>Gets a number that grows with every change of the commands.</summary>
    public int Version => _snapshot.Version;

    /// <summary>Gets problems found while scanning that did not stop the registration, for the startup log.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Finds a command by name.</summary>
    public CommandDescriptor? Find(string name)
    {
        return _snapshot.ByName.GetValueOrDefault(name);
    }

    /// <summary>Finds a command by its class.</summary>
    public CommandDescriptor? Find(Type commandType)
    {
        return _snapshot.ByType.GetValueOrDefault(commandType);
    }

    /// <summary>
    ///     Replaces the commands of a group — the commands that come and go at run time, such as those of an extension. A
    ///     command whose name is taken by a scanned command or another group is skipped and reported.
    /// </summary>
    /// <param name="group">The owner of the commands, for example an extension id.</param>
    /// <param name="commands">The new commands of the group; an empty list removes the group.</param>
    /// <returns>Why some commands were skipped; empty when all were registered.</returns>
    public IReadOnlyList<string> SetGroup(string group, IEnumerable<CommandDescriptor> commands)
    {
        var problems = new List<string>();
        lock (_lock)
        {
            var taken = _groups
                .Where(entry => !string.Equals(entry.Key, group, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Value)
                .ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);

            var accepted = new List<CommandDescriptor>();
            foreach (var command in commands)
            {
                if (_static.ContainsKey(command.Name))
                {
                    problems.Add($"The command '{command.Name}' of '{group}' is skipped: a built-in command has this name.");
                }
                else if (taken.TryGetValue(command.Name, out var owner))
                {
                    problems.Add($"The command '{command.Name}' of '{group}' is skipped: {owner.Group ?? "another group"} registered it first.");
                }
                else if (accepted.Any(other => string.Equals(other.Name, command.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"The command '{command.Name}' is declared more than once in '{group}'.");
                }
                else
                {
                    accepted.Add(command with { Group = group });
                }
            }

            if (accepted.Count == 0)
            {
                _groups.Remove(group);
            }
            else
            {
                _groups[group] = accepted;
            }

            Rebuild();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return problems;
    }

    /// <summary>Removes the commands of a group.</summary>
    public void RemoveGroup(string group)
    {
        SetGroup(group, []);
    }

    /// <summary>Gets the commands of a group; empty for an unknown group.</summary>
    public IReadOnlyList<CommandDescriptor> GetGroup(string group)
    {
        lock (_lock)
        {
            return _groups.GetValueOrDefault(group) ?? [];
        }
    }

    private void Rebuild()
    {
        var byName = new Dictionary<string, CommandDescriptor>(_static, StringComparer.OrdinalIgnoreCase);
        var byType = new Dictionary<Type, CommandDescriptor>(_staticByType);
        foreach (var command in _groups.Values.SelectMany(commands => commands))
        {
            byName[command.Name] = command;
            byType.TryAdd(command.CommandType, command);
        }

        _snapshot = new Snapshot(byName, byType, _snapshot.Version + 1);
    }

    private sealed record Snapshot(
        Dictionary<string, CommandDescriptor> ByName,
        Dictionary<Type, CommandDescriptor> ByType,
        int Version = 0);
}

/// <summary>
///     Finds command classes by reflection and checks them: every problem is collected and reported at startup instead of
///     being skipped with a log line.
/// </summary>
internal static class CommandScanner
{
    private static readonly MethodInfo InvokeWithoutRequestMethod = typeof(CommandScanner).GetMethod(nameof(InvokeWithoutRequest), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo InvokeWithRequestMethod = typeof(CommandScanner).GetMethod(nameof(InvokeWithRequest), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    ///     Builds the descriptors of the command classes among <paramref name="types" />.
    /// </summary>
    /// <param name="types">The candidate types.</param>
    /// <param name="isBuiltIn">Whether a type is a built-in command of the module, which may use the <c>Host.</c> prefix.</param>
    /// <returns>The descriptors and the errors; with any error the registration must stop.</returns>
    public static (IReadOnlyList<CommandDescriptor> Commands, IReadOnlyList<string> Errors) Scan(IEnumerable<Type> types, Func<Type, bool> isBuiltIn)
    {
        var commands = new List<CommandDescriptor>();
        var errors = new List<string>();

        foreach (var type in types.Distinct())
        {
            if (type is not { IsClass: true, IsAbstract: false } || type.ContainsGenericParameters)
            {
                continue;
            }

            var interfaces = type.GetInterfaces().Where(IsCommandInterface).ToList();
            if (interfaces.Count == 0)
            {
                continue;
            }

            if (interfaces.Count > 1)
            {
                errors.Add($"{type.FullName} implements ICommand more than once; a class is exactly one command.");
                continue;
            }

            var name = CommandNaming.GetName(type);
            if (name.Contains('.') && !isBuiltIn(type))
            {
                errors.Add($"The command name '{name}' ({type.FullName}) contains a dot; dots are reserved for the built-in {CommandNaming.BuiltInPrefix}* commands.");
                continue;
            }

            var arguments = interfaces[0].GetGenericArguments();
            var requestType = arguments.Length == 2 ? arguments[0] : null;
            var responseType = arguments[^1];

            if (requestType is { IsInterface: true } or { IsAbstract: true })
            {
                errors.Add($"{type.FullName}: the request type {requestType.FullName} is an interface or abstract class; System.Text.Json cannot create it.");
                continue;
            }

            var description = type.GetCustomAttribute<CommandAttribute>()?.Description;
            commands.Add(new CommandDescriptor(name, type, requestType, responseType, description, CreateInvoker(type, requestType, responseType)));
        }

        foreach (var duplicate in commands.GroupBy(command => command.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            errors.Add($"The command name '{duplicate.Key}' is declared more than once: {string.Join(", ", duplicate.Select(command => command.CommandType.FullName))}.");
        }

        return (commands, errors);
    }

    /// <summary>
    ///     Returns the loadable types of an assembly; types that fail to load (a missing dependency) become warnings.
    /// </summary>
    public static IEnumerable<Type> GetLoadableTypes(Assembly assembly, ICollection<string> warnings)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var reasons = exception.LoaderExceptions.OfType<Exception>().Select(loaderException => loaderException.Message).Distinct();
            warnings.Add($"Some types of {assembly.GetName().Name} could not be loaded and were skipped: {string.Join("; ", reasons)}");
            return exception.Types.OfType<Type>();
        }
    }

    private static bool IsCommandInterface(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(ICommand<>) || definition == typeof(ICommand<,>);
    }

    private static CommandInvoker CreateInvoker(Type commandType, Type? requestType, Type responseType)
    {
        var method = requestType is null
            ? InvokeWithoutRequestMethod.MakeGenericMethod(commandType, responseType)
            : InvokeWithRequestMethod.MakeGenericMethod(commandType, requestType, responseType);

        return method.CreateDelegate<CommandInvoker>();
    }

    private static Task<object?> InvokeWithoutRequest<TCommand, TResponse>(IServiceProvider services, CommandContext context, JsonSerializerOptions serializerOptions)
        where TCommand : ICommand<TResponse>
    {
        var command = services.GetRequiredService<TCommand>();
        return Box(command.ExecuteAsync(context));
    }

    private static Task<object?> InvokeWithRequest<TCommand, TRequest, TResponse>(IServiceProvider services, CommandContext context, JsonSerializerOptions serializerOptions)
        where TCommand : ICommand<TRequest, TResponse>
    {
        var payload = context.RawPayload;
        var missing = payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;

        // No payload: the defaults when every input is optional; otherwise the caller forgot it — also for a struct,
        // which would silently get zeros
        var request = !missing ? payload.Deserialize<TRequest>(serializerOptions)
            : EmptyPayload<TRequest>.Allowed ? JsonSerializer.Deserialize<TRequest>("{}", serializerOptions)
            : default;

        if ((missing && !EmptyPayload<TRequest>.Allowed) || request is null)
        {
            throw new CommandException(CommandErrorCodes.BadRequest, $"The command '{context.CommandName}' expects a payload of type {typeof(TRequest).Name}.");
        }

        var command = services.GetRequiredService<TCommand>();
        return Box(command.ExecuteAsync(request, context));
    }

    private static async Task<object?> Box<T>(Task<T> task)
    {
        return await task.ConfigureAwait(false);
    }

    /// <summary>
    ///     Whether a request type may come without a payload: some public constructor takes only optional parameters — a
    ///     record whose every input has a default, a class with a parameterless constructor, a struct without one of its own.
    /// </summary>
    internal static bool AllowsEmptyPayload(Type requestType)
    {
        var constructors = requestType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        // A struct's implicit parameterless constructor is not listed: without declared ones it is the only one
        return (requestType.IsValueType && constructors.Length == 0)
               || constructors.Any(constructor => constructor.GetParameters().All(parameter => parameter.HasDefaultValue));
    }

    /// <summary>The answer of <see cref="AllowsEmptyPayload" />, computed once per request type.</summary>
    private static class EmptyPayload<TRequest>
    {
        public static readonly bool Allowed = AllowsEmptyPayload(typeof(TRequest));
    }
}
