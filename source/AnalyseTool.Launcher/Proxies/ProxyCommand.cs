using Autodesk.Revit.UI;

namespace AnalyseTool.Launcher.Proxies;

/// <summary>
///     A ribbon button proxy: Revit creates it from the Launcher assembly, and it forwards the click
///     to the same-named <see cref="IExternalCommand" /> inside the add-in's isolated context.
/// </summary>
/// <remarks>Declare one line per button in <c>Proxies.cs</c>; the add-in checks at startup that every button has one.</remarks>
public abstract class ProxyCommand : IExternalCommand
{
    /// <inheritdoc />
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (!PluginTypes.TryCreate(GetType().Name, out IExternalCommand? command, out var error))
        {
            TaskDialog.Show(PluginInfo.Name, error);
            return Result.Failed;
        }

        return command.Execute(commandData, ref message, elements);
    }
}

/// <summary>
///     An availability proxy: forwards to the same-named <see cref="IExternalCommandAvailability" /> inside the
///     add-in's isolated context. A missing target or an exception makes the button unavailable.
/// </summary>
public abstract class ProxyAvailability : IExternalCommandAvailability
{
    private IExternalCommandAvailability? _target;

    /// <inheritdoc />
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        try
        {
            if (_target is null && !PluginTypes.TryCreate(GetType().Name, out _target, out _))
            {
                return false;
            }

            return _target!.IsCommandAvailable(applicationData, selectedCategories);
        }
        catch
        {
            // Revit polls availability constantly; a failing check must not break the ribbon
            return false;
        }
    }
}
