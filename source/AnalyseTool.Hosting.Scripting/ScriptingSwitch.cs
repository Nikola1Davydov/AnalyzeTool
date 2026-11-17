using System.Reflection;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AnalyseTool.Hosting.Scripting;

/// <summary>
///     Turns <see cref="ExecuteCode" /> and <see cref="DescribeApi" /> on and off while Revit runs: on, they are a command
///     group every page and AI client can call; off, they do not exist.
/// </summary>
[PublicAPI]
public interface IScriptingSwitch
{
    /// <summary>Gets whether the code commands are registered now.</summary>
    bool Enabled { get; }

    /// <summary>
    ///     Registers or removes the code commands now; AI clients get <c>tools/list_changed</c>. The setting itself
    ///     (<c>Scripting:Enabled</c>) is the caller's to save.
    /// </summary>
    void SetEnabled(bool enabled);
}

/// <summary>Applies <c>Scripting:Enabled</c> (on in <c>Development</c> by default) when the host starts, then the switch.</summary>
internal sealed class ScriptingSwitch(CommandRegistry registry, IConfiguration configuration, IHostEnvironment environment) : IScriptingSwitch, IHostedService
{
    /// <summary>The command group of the code commands.</summary>
    public const string Group = "scripting";

    private static readonly Type[] CommandTypes = [typeof(ExecuteCode), typeof(DescribeApi)];

    /// <inheritdoc />
    public bool Enabled => registry.GetGroup(Group).Count > 0;

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            registry.RemoveGroup(Group);
            return;
        }

        if (Enabled)
        {
            return;
        }

        var commands = CommandRegistry.Describe(CommandTypes).Select(command =>
        {
            var tool = command.CommandType.GetCustomAttribute<McpToolAttribute>();
            return command with { Annotations = new CommandAnnotations(ExposeToMcp: true, tool?.ReadOnly ?? false, tool?.Destructive ?? false) };
        });
        var problems = registry.SetGroup(Group, commands);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        SetEnabled(configuration.GetValue<bool?>(ScriptingRegistration.EnabledKey) ?? environment.IsDevelopment());
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
