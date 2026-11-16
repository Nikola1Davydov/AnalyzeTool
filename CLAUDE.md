# AnalyseTool — instructions for AI assistants working on this repo

Revit 2025/2026/2027 add-in, version 2.0: built-in tools (parameter report, shared parameters) and the user's own
ribbon buttons — **C# sources that AnalyseTool compiles inside Revit** (DLLs still load), each folder with its **own git
history** that the user controls from the UI. React pages in WebView2, an MCP server inside Revit for AI agents, and the
public `AnalyseTool.Sdk` for extension authors. Built on the `davydov-revit` template
(github.com/Nikola1Davydov/Davydov.Revit.Template); 1.x lives on `main`/`dev` history and in `docs/REWRITE-PLAN.md`.

The rules of the template apply: read [AGENTS.md](AGENTS.md) and [ARCHITECTURE.md](ARCHITECTURE.md), and the skills in
`.agents/skills/` before the task they cover.

## Projects (the dependency contract — `build/Check-Boundaries.ps1` enforces it)

| Project | References | Role |
| --- | --- | --- |
| `source/AnalyseTool.Sdk` | nothing (Newtonsoft, Revit API compile-only) | THE public contract of extensions: `IRevitTask`, `IRevitContext`, `RevitPayload`, `[RevitCommand]`, `IProgressAware`. SemVer'd, packed to NuGet; major = compatibility key. Unchanged from 1.x. |
| `source/AnalyseTool.Hosting` | — | Template module: command pipeline (`ICommandExecutor`, middleware), `CommandRegistry` (scanned commands + run-time groups: `SetGroup`/`RemoveGroup`), `IRevitDispatcher`, lifetime, settings, notifier. Headless. |
| `source/AnalyseTool.Hosting.Mcp` | Hosting | MCP over HTTP inside Revit. Tools = `[McpTool]` commands + run-time commands with `CommandAnnotations.ExposeToMcp`; sends `tools/list_changed`. Headless. |
| `source/AnalyseTool.Hosting.WebView` | Hosting | WebView2 windows, dock pane, transport; `ExternalPage` for extension pages (own virtual host, init script). |
| `source/AnalyseTool.Hosting.Scripting` / `.Visualization` | Hosting(, Mcp) | Template modules: `ExecuteCode`/`DescribeApi`; geometry overlay. Headless. |
| `source/AnalyseTool.Extensions` | Hosting, Hosting.Mcp, Sdk | The user's buttons: catalog of extension folders (1.x layout), Roslyn compiler + cached builds (`bin\<year>\`), collectible load contexts, `SdkCommands` (IRevitTask → pipeline), `ExtensionHost` (load/reload, one registry group per extension), `ExtensionWatcher` (a saved file reloads its extension), git history (`LibGit2ExtensionHistory`), pinning, packages, and the commands in `Features/`. Headless. |
| `source/AnalyseTool` | all of the above | The add-in: `Application` (ribbon), `Host.cs`, built-in commands in `Features/<Slice>/` + services in `Services/<Slice>/` (Elements, Actions, SharedParameters, Parameters), ribbon of extensions (`Infrastructure/Extensions`, AdWindows), app commands of the pages (`Infrastructure/App`), React app in `ClientApp/`. |
| `source/AnalyseTool.Launcher` | add-in (build order) | Thin Revit entry, isolation in an ALC, ribbon proxies (`Proxies.cs`: one line per button class in `Commands/`). |

Hard rules (beyond AGENTS.md):
- An extension command is an `IRevitTask` of the Sdk; never make extensions reference `AnalyseTool.Extensions` or the add-in. If they need more, that is an Sdk contract decision (minor = additive).
- The extension folder format of 1.x stays readable (`plugin.json` schema 1/2, `src\*.cs`, `<year>\*.dll`, legacy `<root>\<year>\<id>`). Write manifests with `ExtensionManifest.Update` so unknown fields survive.
- Git is hidden behind `IExtensionHistory`: the UI and MCP say version / save / restore / pin, never commit or branch. Nothing is lost: restore first saves unsaved work. Pins live in `extensions-state.json`, never in the folder.
- Builds go to `bin\` (git-ignored); a failed compile keeps the last good build loaded and reports diagnostics with file/line.
- Profile: `%LOCALAPPDATA%\AnalyseTool\` (`extensions\`, `extensions-dist\`, `extensions.json`, `extensions-state.json`, `trash\`) — the same places as 1.x.

## Build and test

```powershell
dotnet build AnalyseTool.slnx -c Release.R25          # or R26 / R27; Debug.* deploys to %AppData%\Autodesk\Revit\Addins\<year>\
dotnet run -c Release --project build                 # boundaries, ClientApp, compile, Revit-free tests, revit-bridge tests
dotnet run -c Release --project build -- pack         # + .msi and bundle in output/
dotnet test --project tests/AnalyseTool.Tests/AnalyseTool.Tests.csproj -c Release.R25
dotnet test --project tests/AnalyseTool.Hosting.Tests/AnalyseTool.Hosting.Tests.csproj -c Release.R25
pwsh build/Check-Boundaries.ps1
```

| Tier | Project | Needs |
| --- | --- | --- |
| 1 | `tests/AnalyseTool.Tests` (add-in + Extensions: file formats, catalog, compiler, load/pin/versions on temp profiles, packages, tool contracts) | nothing — `RevitStubs` stand in for the Revit types, `revit-ref` holds the reference assemblies for Roslyn |
| 1 | `tests/AnalyseTool.Hosting.Tests` (pipeline, registry groups, MCP, WebView source, scripting) | nothing |
| 3 | `tests/AnalyseTool.RevitTests` (services on a seeded document) | a licensed Revit: `dotnet test … -c Release.R25` by hand |

Every fixed bug gets a test on its tier.

## Docs to keep in sync

- `source/AnalyseTool.Extensions/Authoring/LLM.md` — the guide for extension authors and agents: copied next to the add-in (updated with it; the `AGENTS.md` / `CLAUDE.md` of every new extension folder point there), served by `GetAuthoringGuide`, mirrored to the wiki.
- `ONBOARDING.md` — extension author guide (wiki home, NuGet README of the Sdk).
- `CHANGELOG.md` — shipped next to the add-in, shown on the About page.
- `docs/REWRITE-PLAN.md` — the 2.0 plan and its open questions.
