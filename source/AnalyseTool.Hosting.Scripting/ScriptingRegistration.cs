using System.IO;
using AnalyseTool.Hosting.Scripting;
using AnalyseTool.Hosting.Scripting.Execution;
using AnalyseTool.Hosting.Scripting.Reference;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Registers the AnalyseTool.Hosting.Scripting module.
/// </summary>
[PublicAPI]
public static class ScriptingRegistration
{
    /// <summary>The configuration key that turns scripting on or off.</summary>
    public const string EnabledKey = "Scripting:Enabled";

    /// <param name="commands">The commands of <c>AddRevitCommands</c>.</param>
    extension(RevitCommandsBuilder commands)
    {
        /// <summary>
        ///     Adds the code commands: <see cref="ExecuteCode" />, C# compiled with Roslyn and run inside Revit, and
        ///     <see cref="DescribeApi" />, the Revit API of the running version for writing that code. They exist only while
        ///     scripting is on — <c>Scripting:Enabled</c>, on in the <c>Development</c> environment by default — and
        ///     <see cref="IScriptingSwitch" /> turns them on and off while Revit runs.
        /// </summary>
        /// <param name="builder">The host application builder: its configuration decides at start, its services get the runner.</param>
        /// <returns>The <see cref="RevitCommandsBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     Off, the commands are not registered at all: no page and no AI client can reach them. Any code runs with the
        ///     user's rights: turn it on only where that is wanted.
        /// </remarks>
        /// <example>
        ///     <code>
        ///     builder.AddRevitCommands(commands => commands
        ///         .FromAssemblyOf&lt;Application&gt;()
        ///         .AddScripting(builder));
        ///     </code>
        /// </example>
        public RevitCommandsBuilder AddScripting(IHostApplicationBuilder builder)
        {
            builder.Services.AddSingleton<ScriptRunner>();
            builder.Services.AddSingleton(new ScriptJournal(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                builder.Environment.ApplicationName,
                "scripts")));
            builder.Services.AddSingleton(_ => new ApiReference([typeof(Autodesk.Revit.DB.Document).Assembly, typeof(Autodesk.Revit.UI.UIApplication).Assembly]));
            // A command group, not scanned commands: the switch adds and removes them at run time
            builder.Services.AddScoped<ExecuteCode>();
            builder.Services.AddScoped<DescribeApi>();
            builder.Services.AddSingleton<ScriptingSwitch>();
            builder.Services.AddSingleton<IScriptingSwitch>(services => services.GetRequiredService<ScriptingSwitch>());
            builder.Services.AddHostedService(services => services.GetRequiredService<ScriptingSwitch>());
            return commands;
        }
    }
}
