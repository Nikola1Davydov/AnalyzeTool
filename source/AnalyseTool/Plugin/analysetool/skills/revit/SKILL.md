---
name: revit
description: Work with Revit through AnalyseTool — read or change the open model, or create, edit and fix AnalyseTool ribbon buttons (C# commands that AnalyseTool compiles inside Revit). Use when the user mentions Revit, the open model, AnalyseTool, or a Revit button/command/tool to build.
---

# AnalyseTool in Revit

The `analysetool` MCP server of this plugin is AnalyseTool, a Revit add-in, running inside every open Revit
(2025, 2026, 2027) on one address. Its tools are the commands of AnalyseTool and of the user's buttons.

## Before anything

- No answer from the server (connection refused, `revit_not_open`): Revit is closed, or AnalyseTool was not opened
  in it yet — AnalyseTool starts its server the first time the user opens it (the **Buttons** button on the ribbon).
  Ask the user to open it; do not try to start Revit yourself.
- Several Revit versions open: call `ListRevitVersions`. Every tool takes `"revit": "2026"` to run in that version;
  without it a call goes to the Revit the user had in front last. Say which version you used when it matters.

## Buttons

A button is C# that AnalyseTool compiles itself — no .NET SDK, no project, no DLL. **Read the guide first:** call
`GetAuthoringGuide` once per task (the same text as `LLM.md` next to the add-in) and follow it; it is the contract.

The folders are the ribbon (`%LOCALAPPDATA%\AnalyseTool\extensions\`):

```
Sheets.tab\Numbering.panel\Renumber.pushbutton\   bundle.json, Renumber.cs, index.html, .git\
```

- New button: `CreateExtension` (name, what it does, `tab`, `panel`; `CommandWithPage` for a page with its command,
  `Command` for C# only). It returns the folder: edit its `*.cs` there, or with `WriteExtensionFile`.
- Saving a file reloads the button. Check with `BuildExtension` (errors with file and line) or `GetExtensions`.
  A failed build keeps the last good one running.
- Every button folder is a git history the user manages (versions, restore, pin) from AnalyseTool. Save a version
  with `SaveExtensionVersion` when a change works; do not run git in the folder yourself.
- The Revit API only inside `RunInRevitAsync`. Every Transaction and TransactionGroup starts AND ends inside ONE
  `RunInRevitAsync` — Revit rolls back what is open when a call returns. One undo step: one call. Work of minutes:
  chunks, one transaction each (several undo steps — say so).
- A page reads the result with the C# names as written (`result.Sheets`, not `result.sheets`).
- A command you just added may not be in your tool list yet: call it with `RunCommand { name, payload }` (a dry run
  first when it changes the model).

## The open model

Use the read-only tools first (they say so in their annotations) and show the user what you found before you call a
tool that changes the model. A changing tool runs in a transaction the user can undo in Revit.
