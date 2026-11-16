---
name: verify-in-revit
description: >
  Check a change in a running Revit through the add-in's MCP server instead of assuming it works: build, start Revit, call
  the command, look at the view, read the log.
  USE FOR: confirming a new or changed command, page or button works; finding why a command fails in Revit; looking at
  the model before and after; checking a Revit API member before using it.
  DO NOT USE FOR: writing the command itself (add-command); unit tests that need no Revit.
---

# Verify in Revit

A Debug build of the add-in runs an MCP server inside Revit (`AnalyseTool.Hosting.Mcp`). Through it you can call the
add-in's commands, see the active view and read the add-in's log — so check your change in Revit before you say it works.

## Step 1: Build and start Revit

```
dotnet build AnalyseTool.slnx -c Debug.R26
```

* Revit must be closed: it locks the deployed add-in, and the build cannot replace it. Ask the user to close Revit, or
  to save their work first.
* A Debug build deploys the add-in to `%AppData%\Autodesk\Revit\Addins\<year>\`. New or changed commands appear only
  after Revit starts again.
* Start Revit with a model, or ask the user to open one: `& "C:\Program Files\Autodesk\Revit 2026\Revit.exe" "<model>.rvt"`.
  Pages load from the Vite dev server: `npm run dev` in `source/AnalyseTool/ClientApp`.

## Step 2: Connect

`%LOCALAPPDATA%\AnalyseTool\mcp.json` has the one address of every Revit version (`http://localhost:48075/mcp`), the
token and the command that adds the server to Claude Code (`claudeCode`); the token survives Revit restarts. With
several Revit versions open, pass `"revit": "2026"` to a tool (`ListRevitVersions` lists them). A client reads the tool list when it connects: after a
Revit restart, reconnect the server (`/mcp` in Claude Code) — that also brings in new commands. Without the file or with
`ECONNREFUSED`, Revit is not running, or the server is off (`Mcp:Enabled`), or the port is busy — the log says which.

## Step 3: Check

| Tool | Use |
| --- | --- |
| `GetActiveDocument` | Revit runs, which model is open |
| your command | marked with `[McpTool]` — call it with real input; an error comes back as `code: message` |
| `GetSelection` / `SelectElements` | what the user selected; select and zoom to what you found |
| `InspectElement` | properties and parameters of an element by id — follow references by id and nested objects by `path` (`Location.Curve`); reads only, no code |
| `RenderView` | a PNG of the active view — look at the result instead of guessing; the selection is highlighted; `width` 1280 by default |
| `ShowGeometry` / `ClearGeometry` | draw elements, boxes, lines or points over the 3D views and plans without changing the model — a colour per group, then `RenderView`; clear when done |
| `GetLogEntries` | the add-in log: `afterSequence` 0, then the `lastSequence` you got; `minLevel` `Warning` for failures |
| `DescribeApi` | a Revit API type of this Revit version, with its members — before using a member you are not sure of |
| `ExecuteCode` | C# on the Revit thread (`doc`, `uidoc`, `uiapp`) — for what `InspectElement` and the commands do not show |

A command that is not marked with `[McpTool]` is not visible; check it through `ExecuteCode`, or ask the user to press
the button and then read `GetLogEntries`.

## Rules

* Change the model only as much as the check needs, and say what you changed. Try a change with `ExecuteCode` and
  `dryRun` first: it reports `changes` and rolls back. A real run is one undo step for the user — Ctrl+Z.
* Report what you saw — the result, the picture, the log lines — not what should happen.
* To point at something, prefer `SelectElements` or `ShowGeometry` over changing the model; `ClearGeometry` when done.
* If Revit or the server is not available, say so and what you checked without it; do not claim the change works.
