using Autodesk.Revit.UI;
using Nice3point.Revit.Extensions.UI;

namespace AnalyseTool.Infrastructure.Ribbon;

/// <summary>
///     Ribbon helpers that keep buttons inside the Launcher's isolated context.
/// </summary>
/// <remarks>
///     Revit creates a button's command class itself, from the assembly path and type name of the button.
///     <c>AddPushButton&lt;T&gt;</c> from Nice3point.Revit.Extensions points to the assembly of <c>T</c>,
///     so Revit would load the add-in assembly a second time into its own context, bypassing the isolation.
///     These helpers point the button to the same-named proxy in the Launcher instead.
/// </remarks>
[PublicAPI]
public static class RibbonButtons
{
    private const string CommandProxyBase = "ProxyCommand";
    private const string AvailabilityProxyBase = "ProxyAvailability";

    /// <summary>
    ///     Adds a push button for <typeparamref name="TCommand" /> to the panel.
    /// </summary>
    /// <typeparam name="TCommand">The command class; the Launcher must have a proxy with the same name.</typeparam>
    /// <param name="panel">The ribbon panel.</param>
    /// <param name="text">The button text.</param>
    /// <returns>The added button.</returns>
    /// <remarks>
    ///     The internal button name is <c>TCommand.FullName</c>, as in Nice3point.Revit.Extensions. Revit binds
    ///     keyboard shortcuts and the Quick Access Toolbar to it, so renaming or moving the class resets them.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The Launcher has no proxy for <typeparamref name="TCommand" />.</exception>
    public static PushButton AddButton<TCommand>(this RibbonPanel panel, string text) where TCommand : IExternalCommand, new()
    {
        var launcher = LauncherInfo.Current;
        if (launcher is null)
        {
            return panel.AddPushButton<TCommand>(text);
        }

        var command = typeof(TCommand);
        var proxyName = launcher.GetProxyName(command, CommandProxyBase);
        var buttonData = new PushButtonData(command.FullName, text, launcher.Path, proxyName);

        return (PushButton)panel.AddItem(buttonData);
    }

    /// <summary>
    ///     Sets the availability class of the button.
    /// </summary>
    /// <typeparam name="TAvailability">The availability class; the Launcher must have a proxy with the same name.</typeparam>
    /// <param name="button">The button.</param>
    /// <returns>The same button, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The Launcher has no proxy for <typeparamref name="TAvailability" />.</exception>
    /// <summary>
    ///     Describes a button without adding it, for <c>AddStackedItems</c>: small buttons in a column of three. The class
    ///     points to the Launcher's proxy, as <see cref="AddButton{TCommand}" /> does.
    /// </summary>
    public static PushButtonData CreateButtonData<TCommand>(string text) where TCommand : IExternalCommand, new()
    {
        var command = typeof(TCommand);
        var launcher = LauncherInfo.Current;
        return launcher is null
            ? new PushButtonData(command.FullName, text, command.Assembly.Location, command.FullName)
            : new PushButtonData(command.FullName, text, launcher.Path, launcher.GetProxyName(command, CommandProxyBase));
    }

    public static PushButton SetAvailability<TAvailability>(this PushButton button) where TAvailability : IExternalCommandAvailability, new()
    {
        var launcher = LauncherInfo.Current;
        if (launcher is null)
        {
            return button.SetAvailabilityController<TAvailability>();
        }

        button.AvailabilityClassName = launcher.GetProxyName(typeof(TAvailability), AvailabilityProxyBase);
        return button;
    }
}
