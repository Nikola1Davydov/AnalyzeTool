---
name: add-ribbon-button
description: >
  Add a ribbon button to the add-in: the button class, its proxy in the Launcher, its icon and availability.
  USE FOR: a new button or panel, enabling a button only with an open document or project, a button icon, a Debug-only button.
  DO NOT USE FOR: the work behind the button that reads or changes the model (add-command); a page the button opens (add-page).
---

# Add a ribbon button

Revit creates the class of a button from the assembly path of the button. The add-in lives in the Launcher's
`AssemblyLoadContext`, so a button points to a same-named proxy in the Launcher, and the proxy forwards the click into
the add-in. A button therefore always has two parts: the class in the add-in and one proxy line in the Launcher.

## Step 1: Write the button class in `Commands/`

```csharp
using Autodesk.Revit.Attributes;
using AnalyseTool.Hosting.Commands;
using Nice3point.Revit.Toolkit.External;

namespace AnalyseTool.Commands;

/// <summary>
///     Renames the selected families.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class RenameFamiliesCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Host.GetService<ICommandSender>().Send<RenameFamilies>();
    }
}
```

* Keep the button thin: send a pipeline command (`ICommandSender.Send<T>()`, see add-command) or open a page
  (`Host.GetService<IWindows>().Open("/route")`, see add-page).
* `Execute` runs on the Revit thread, so a short direct use of the Revit API is allowed here. `ActiveUIDocument` is
  allowed in a button: the click belongs to the active document.
* A button cannot take constructor dependencies — Revit creates it — so it resolves services with `Host.GetService<T>()`.
* The class name and namespace are the internal name of the button. Revit binds keyboard shortcuts and the Quick Access
  Toolbar to it: never rename or move a released button.

## Step 2: Add the proxy to the Launcher

One line in `source/AnalyseTool.Launcher/Proxies.cs`, with the same simple class name:

```csharp
[Transaction(TransactionMode.Manual)] public sealed class RenameFamiliesCommand : ProxyCommand;
```

The proxy name must be unique among the button classes of the add-in. If the proxy is missing, the add-in fails at
startup with «The Launcher has no proxy for '…'. Add this line to Proxies.cs …».

## Step 3: Put the button on the ribbon

In `Application.CreateRibbon` (`source/AnalyseTool/Application.cs`):

```csharp
panel.AddButton<RenameFamiliesCommand>("Rename\nfamilies")
    .SetAvailability<ProjectAvailability>()
    .WithIcons("RenameFamilies");
```

* `AddButton<T>` and `SetAvailability<T>` come from `Infrastructure/Ribbon`; never use `AddPushButton<T>` of
  Nice3point.Revit.Extensions — it points the button to the add-in assembly and bypasses the isolation.
* A new panel: `Application.CreatePanel("Panel name", "AnalyseTool")`.
* `\n` in the text breaks the label into two lines.

## Step 4: Availability (optional)

* `DocumentAvailability` — enabled while a document is open; `ProjectAvailability` — while a project (not a family) is
  open. Both are in `Infrastructure/Ribbon/Availability.cs` and already have proxies.
* A custom rule: a class implementing `IExternalCommandAvailability` in the add-in, plus a proxy line in `Proxies.cs`:

```csharp
public sealed class SelectionAvailability : ProxyAvailability;
```

Revit asks availability very often: keep the check trivial. An exception in it counts as «unavailable».

## Step 5: Icon (optional)

Two PNG files in `source/AnalyseTool/Resources/Icons/`: `<Name>16.png` (16×16) and `<Name>32.png` (32×32). They are
embedded resources (`Resources\Icons\*.png`), so nothing else is needed; `WithIcons("<Name>")` sets both.

## Debug-only buttons

Wrap the button class and its `AddButton` call in `#if DEBUG`. Keep the proxy line without `#if`: a proxy without a
button does no harm.

## Check

Build `Debug.R2x` and start Revit: the button is on the tab, the click works, availability follows the document.
