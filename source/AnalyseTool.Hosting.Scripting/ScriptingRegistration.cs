using System.IO;
using AnalyseTool.Hosting.Scripting;
using AnalyseTool.Hosting.Scripting.Execution;
using AnalyseTool.Hosting.Scripting.Reference;
using Microsoft.Extensions.Configuration;
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
        ///     Adds the code commands when scripting is on — in the <c>Development</c> environment by default, otherwise with
        ///     <c>Scripting:Enabled</c>: <see cref="ExecuteCode" />, C# compiled with Roslyn and run inside Revit, and
        ///     <see cref="DescribeApi" />, the Revit API of the running version for writing that code.
        /// </summary>
        /// <param name="builder">The host application builder: its configuration decides, its services get the runner.</param>
        /// <returns>The <see cref="RevitCommandsBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     Off, the command is not registered at all: no page and no AI client can reach it. Read once at startup — a changed
        ///     setting needs a Revit restart. Any code runs with the user's rights: turn it on only where that is wanted.
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
            var enabled = builder.Configuration.GetValue<bool?>(EnabledKey) ?? builder.Environment.IsDevelopment();
            if (!enabled)
            {
                return commands;
            }

            builder.Services.AddSingleton<ScriptRunner>();
            builder.Services.AddSingleton(new ScriptJournal(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                builder.Environment.ApplicationName,
                "scripts")));
            builder.Services.AddSingleton(_ => new ApiReference([typeof(Autodesk.Revit.DB.Document).Assembly, typeof(Autodesk.Revit.UI.UIApplication).Assembly]));
            return commands.FromTypes(typeof(ExecuteCode), typeof(DescribeApi));
        }
    }
}
