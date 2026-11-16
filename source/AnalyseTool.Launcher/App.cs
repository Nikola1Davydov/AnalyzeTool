using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Autodesk.Revit.UI;
using AnalyseTool.Launcher.Proxies;

namespace AnalyseTool.Launcher;

/// <summary>
///     The entry point Revit loads. Loads the add-in into its own <see cref="PluginLoadContext" />
///     and forwards startup and shutdown to the add-in application created inside it.
/// </summary>
public sealed class App : IExternalApplication
{
    private static IExternalApplication? _application;

    /// <summary>Gets the isolated context of the add-in, or <see langword="null" /> when it failed to load.</summary>
    internal static PluginLoadContext? PluginContext { get; private set; }

    /// <inheritdoc />
    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            var launcherPath = typeof(App).Assembly.Location;
            AppContext.SetData(PluginInfo.LauncherPathKey, launcherPath);
            AppContext.SetData(PluginInfo.ProxyNamespaceKey, typeof(ProxyCommand).Namespace);

            // The Launcher's deps.json lists the add-in and all its packages (referenced projects get no deps.json of their own)
            var pluginPath = Path.Combine(Path.GetDirectoryName(launcherPath)!, PluginInfo.AssemblyFile);
            PluginContext = new PluginLoadContext(PluginInfo.Name, launcherPath);

            var pluginAssembly = PluginContext.LoadFromAssemblyPath(pluginPath);
            var applicationType = pluginAssembly.GetType(PluginInfo.ApplicationType, throwOnError: true)!;

            // The single reflection call into the add-in; after this, Revit talks to it through IExternalApplication
            _application = (IExternalApplication)Activator.CreateInstance(applicationType)!;

            var result = _application.OnStartup(application);
            if (result != Result.Succeeded)
            {
                StartupLog.Write(application, $"{PluginInfo.ApplicationType}.OnStartup returned {result}.");
                Unload();
            }

            return result;
        }
        catch (Exception exception)
        {
            // Revit reports a failed external application itself; the file keeps the full exception for support.
            // The add-in's own exception, not the TargetInvocationException of the reflection call, so Revit shows its text
            var actual = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            StartupLog.Write(application, actual.ToString());
            Unload();
            ExceptionDispatchInfo.Capture(actual).Throw();
            throw;
        }
    }

    /// <summary>
    ///     Forgets the add-in after a failed start: a proxy button created before the failure then says the add-in failed
    ///     instead of calling into a half-started one.
    /// </summary>
    private static void Unload()
    {
        PluginContext = null;
        _application = null;
    }

    /// <inheritdoc />
    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            return _application?.OnShutdown(application) ?? Result.Succeeded;
        }
        catch (Exception exception)
        {
            StartupLog.Write(application, $"Shutdown failed:{Environment.NewLine}{exception}");
            return Result.Succeeded;
        }
    }
}
