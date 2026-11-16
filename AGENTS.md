# AnalyseTool

A Revit 2025–2027 add-in: Nice3point SDK and Toolkit, an isolated Launcher, a command pipeline in the style of ASP.NET
Core and a React UI in WebView2. How it fits together — [ARCHITECTURE.md](ARCHITECTURE.md).

## Skills

Read the skill before the task it covers:

| Task | Skill |
| --- | --- |
| A ribbon button, its availability or icon | [.agents/skills/add-ribbon-button/SKILL.md](.agents/skills/add-ribbon-button/SKILL.md) |
| A command the pages or a button call: reading or changing the model | [.agents/skills/add-command/SKILL.md](.agents/skills/add-command/SKILL.md) |
| A page, a window or the dock pane | [.agents/skills/add-page/SKILL.md](.agents/skills/add-page/SKILL.md) |
| Checking a change in a running Revit: call commands, see the view, read the log | [.agents/skills/verify-in-revit/SKILL.md](.agents/skills/verify-in-revit/SKILL.md) |

## Non-negotiables

* The Revit API is used only on the Revit thread. A command runs on the thread pool and reaches the model only through
  `ActiveDocument` or `IRevitDispatcher`; a ribbon button (`ExternalCommand.Execute`) already runs on the Revit thread.
* A command takes the document from `ActiveDocument`, never from `ActiveUIDocument`: the user may switch documents
  while the command runs, and `ActiveDocument` then fails with `document_changed` instead of touching another model.
  `build/Check-Boundaries.ps1` rejects `ActiveUIDocument` in `Features/` and `Services/`; a line that really needs it is
  marked `// AllowActiveDocument: <reason>`.
* `Features/` and `Services/` contain no WPF (`System.Windows`, `MessageBox`): the UI is the React app, and a message to
  the user goes through `IUserNotifier`.
* The Launcher (`source/AnalyseTool.Launcher`) uses no type of the add-in or the modules — only names in
  strings. Their types would load into the Revit context and bypass the add-in's `AssemblyLoadContext`.
* `AnalyseTool.Hosting`, `AnalyseTool.Hosting.Mcp`, `AnalyseTool.Hosting.Scripting` and `AnalyseTool.Hosting.Visualization`
  stay headless: no WPF.
* An AI client (MCP) sees only commands marked with `[McpTool]`: open a command to it deliberately, never by default.
* `ExecuteCode` runs any C# with the user's rights. It exists only where scripting is on — Debug, or `Scripting:Enabled`;
  never turn it on by default in Release.
* A ribbon button is a class in `Commands/` and a same-named proxy line in the Launcher's `Proxies.cs`. A button class
  keeps its name and namespace: Revit binds keyboard shortcuts and the Quick Access Toolbar to them.
* Logging goes through source-generated `[LoggerMessage]` methods, not the `Log*` extension methods of `ILogger`.
* A project reference or a package reference goes into the table of `build/Check-Boundaries.ps1` first; the pipeline
  fails on a reference the table does not allow.

## Extensions (the user's buttons)

* An extension is a folder with `plugin.json`: C# sources in `src\` that `AnalyseTool.Extensions` compiles inside Revit
  (the default for a new one), or a DLL per Revit year. Its commands implement `IRevitTask` of `AnalyseTool.Sdk` and
  join the pipeline as a registry group named after the extension id (`<id>.<Command>`).
* Every extension folder has its own git history behind `IExtensionHistory`; versions are saved, compared, restored and
  pinned from the pages (`/extensions/<id>`) and over MCP. Pins live in `extensions-state.json`.
* The ribbon buttons of extensions are AdWindows items (`Infrastructure/Extensions/ExtensionRibbon.cs`) that follow
  `ExtensionHost.Changed`; the add-in's own buttons are template buttons with proxies.
* AI clients also see the commands of extensions, unless `[RevitCommand(HiddenFromMcp = true)]` — as in 1.x.
* `AnalyseTool.Extensions` stays headless and never references the add-in.

## Build and check

```
dotnet build AnalyseTool.slnx -c Debug.R26
dotnet run -c Release --project build
dotnet run -c Release --project build -- pack
```

A Debug build deploys the add-in to `%AppData%\Autodesk\Revit\Addins\<year>\` and loads the pages from the Vite dev
server (`npm run dev` in `source/AnalyseTool/ClientApp`). The pipeline checks the boundaries, builds `ClientApp`
and every `Release.R2x`; `pack` adds the `.msi` installers and the Autodesk bundle in `output/`.
