using System.Reflection;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Services.Updates;

namespace AnalyseTool.Infrastructure.App;

/// <summary>The version of AnalyseTool, as About shows it.</summary>
public static class AppVersion
{
    /// <summary>Gets the installed version without the commit («2.0.1»).</summary>
    public static string Current
    {
        get
        {
            var assembly = typeof(AppVersion).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
                   ?? assembly.GetName().Version?.ToString() ?? "";
        }
    }
}

/// <summary>Whether a newer AnalyseTool is released: the dot on the profile and the line on About.</summary>
[Command(Description = "Returns whether a newer AnalyseTool is released on GitHub (asked at most once a day).")]
public sealed class GetUpdate(UpdateCheck updates) : ICommand<UpdateInfo>
{
    /// <inheritdoc />
    public Task<UpdateInfo> ExecuteAsync(CommandContext context)
    {
        return updates.GetAsync(AppVersion.Current, context.CancellationToken);
    }
}
