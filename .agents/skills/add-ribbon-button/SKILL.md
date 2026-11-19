---
name: add-ribbon-button
description: >
  Add a ribbon button of AnalyseTool itself: one entry in Ribbon\analysetool\plugin.json — the same format as an extension's button.
  USE FOR: a new button or panel of the add-in, its icon, a Debug-only button, what a click runs.
  DO NOT USE FOR: the work behind the button that reads or changes the model (add-command); a page the button opens (add-page);
  a button of the user's extensions (they write their own plugin.json; see source/AnalyseTool.Extensions/Authoring/LLM.md).
---

# Add a ribbon button

Every ribbon button — AnalyseTool's own and every extension's — is declared the same way: an entry of `ui.buttons` in a
`plugin.json`. One piece of code (`Infrastructure/Ribbon/AppRibbon.cs`, AdWindows) builds them all, and one piece
(`ButtonRunner`) handles every click: it counts the click for the Statistics page, then runs the button's pipeline
command with its `args`, or opens its page. There is no button class, no proxy in the Launcher and no Revit API ribbon code.

## Step 1: Write the command behind it (if it is new)

A button runs a pipeline command — see add-command. Opening a page needs no new command: `OpenWindow` does it.

## Step 2: Declare the button

In `source/AnalyseTool/Ribbon/analysetool/plugin.json` (Debug-only buttons: `Ribbon/analysetool.dev/plugin.json`, copied
only in Debug configurations):

```json
{
  "name": "Rename\nfamilies",
  "panel": "Families",
  "icon": "glyph:E8AC",
  "tooltip": "Renames the selected families.",
  "command": "RenameFamilies",
  "args": { "prefix": "FAM_" }
}
```

* `command` — any pipeline command by name; `args` is its request (the JSON a page would send). The result, when it is
  text or an object, is shown after the click; `Empty` and `{}` show nothing.
* A page of the add-in: `"command": "OpenWindow", "args": { "route": "/families" }` — add `"dock": true` for the dock pane.
  The window title and size of a route are in `AppWindows.Open` (`Infrastructure/App/AppCommands.cs`).
* A web address: `"command": "OpenUrl", "args": { "url": "https://…" }`.
* `ribbon` / `pane` — `false` keeps it off the ribbon or out of the dock pane. The ribbon of AnalyseTool keeps only
  Buttons (the pane): a new built-in button is `"ribbon": false` (a tile of the pane) unless the user asks.
* `panel` / `tab` — where it goes; the panels appear in the order of their first button, before the panels of extensions.
* `kind` — `push` (large, the default), `stacked` (small, three per column) or `pulldown` (with `items`).
* `icon` — `glyph:XXXX` (a Segoe MDL2 Assets code point, crisp at any DPI) or a PNG next to the `plugin.json`.
* `\n` in the name breaks the label; statistics show it as a space. The name (with the folder id) is the key of the
  button's statistics: renaming a button starts its counts anew.

## Step 3: Check

`tests/AnalyseTool.Tests/Ribbon/ButtonTests.cs` checks that every built-in button runs a command that exists. Build
`Debug.R2x` and start Revit: the button is on the tab, the click works, and it shows up on the Statistics page.

## What this model cannot do

AdWindows buttons are not Revit commands: Revit cannot bind a keyboard shortcut to them, and there is no availability
class — a command that needs a document says so itself (`ActiveDocument` fails with a clear message). If a button truly
needs a Revit keyboard shortcut, it would have to be an `IExternalCommand` with a proxy in the Launcher's `Proxies.cs`
(the comment there shows the line) — ask before adding one, it breaks the one-model rule.
