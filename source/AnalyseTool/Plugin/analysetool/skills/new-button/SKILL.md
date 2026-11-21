---
name: new-button
description: Create a new AnalyseTool ribbon button for Revit from a description of what it should do.
argument-hint: "<what the button should do> [tab] [panel]"
disable-model-invocation: true
---

# New AnalyseTool button

Make a working Revit button for: $ARGUMENTS

1. Call `GetAuthoringGuide` and follow it.
2. If several Revit versions are open (`ListRevitVersions`), ask which one to test in, unless the user said it.
3. Pick a short button name (one or two words) and a tab and panel: the ones the user named, else an existing tab
   that fits (`GetExtensions` lists the buttons and their tabs), else a new tab named after the topic.
4. `CreateExtension` with that name, a one-sentence description, the tab and the panel. Use `CommandWithPage` when the
   user has to enter or choose something or see a table; `Command` when one click does it all.
5. Write the C# (and the page) in the folder it returned. Keep it to what was asked; read-only unless the task is to
   change the model.
6. `BuildExtension` until it has no errors, then try it with `RunCommand { name: "<tab>.<button>.<Class>", payload }` (dry run first). Then tell the user the button is on the ribbon and in the AnalyseTool pane
   under that tab, and how to try it. Once the run shows it works, save a version (`SaveExtensionVersion`) with a
   message that says what the button does — restore is always there, so a version never needs asking.
