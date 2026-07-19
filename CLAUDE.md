# AnalyseTool — instructions for AI assistants working on this repo

Revit 2025/2026/2027 add-in: an extensible command platform with a Vue frontend (WebView2),
an MCP server for AI agents, and a public SDK for third-party extensions.

## Architecture (the dependency contract)

```
[callers]                      [platform]              [callees]
WebView2 (App), Mcp.Bridge ──► CommandQueue in Core ──► Tools commands, user extensions
```

| Project | References | Role |
| --- | --- | --- |
| `AnalyseTool.Sdk` | nothing | THE public contract (`IRevitTask`, `IRevitContext`, `RevitPayload`, `[RevitCommand]`). SemVer'd, packed to NuGet. |
| `AnalyseTool.Core` | Sdk | Platform: `CommandQueue` (single entry point for ALL transports), `CommandDispatcher`, extension loader (collectible ALC, type-identity sharing), Roslyn scripting, `CoreServices`. **Headless — no WPF, no dialogs; errors go to Serilog + `ExtensionDiagnostics`.** |
| `AnalyseTool.Tools` | Sdk **only** | Built-in feature commands (Get/Families/Ai/Actions + services). Lives on the same rails as third-party extensions — if a Tools command needs more than the Sdk offers, that is a deliberate Sdk contract decision, never a ProjectReference. |
| `AnalyseTool.Mcp.Bridge` | Core, Sdk | In-Revit MCP transport: TCP bridge that enqueues into the `CommandQueue`. The reference pattern for new transports (e.g. a future SignalR remote): ProjectReference on Core + one `InternalsVisibleTo` line — zero Core changes. |
| `AnalyseTool.Mcp` | none (links `McpWire.cs`) | Out-of-process stdio MCP exe launched by the AI client. Never loads Revit/Core. Wire contract shared with the bridge via the linked `McpWire.cs`. |
| `AnalyseTool.App` | Core, Tools, Mcp.Bridge, Sdk | Host: windows, ribbon (`RibbonHost`), dock pane, `WebView2Transport`, `UserDialogUtils`, bootstrap (`AnalyseToolBootstrap` = stateless composition root; all state lives in `CoreServices`). |
| `AnalyseTool.Launcher` | App (+ Mcp build-order) | Thin Revit entry: loads `AnalyseTool.App.dll` into an isolated ALC, builds the ribbon **via reflection by type name** — when renaming/moving `RibbonHost` or `AnalyseToolCommand`, update the FQN strings in `Launcher/App.cs` and `Launcher/RevitCommands/`. |

Hard rules:
- **Never** add a ProjectReference that violates the table — `src/build/Check-Boundaries.ps1` fails CI.
- **Core and Tools stay headless**: no `UseWPF`, no `System.Windows`, no `MessageBox`. UI commands (folder pickers etc.) belong to App.
- Commands touch the Revit model **only** inside `IRevitContext.RunInRevitAsync`. Core code gets the Revit version from `CoreServices.RevitVersion`, never from an ambient `Context`.
- Transports never talk to `CommandDispatcher`; they enqueue `CommandRequest`s into `CoreServices.Queue` (carries command, payload, source, ct, progress, optional pre-execution gate).
- `SharedData/ToolData` is compiled into several assemblies and must stay `internal` (a public copy causes CS0433).
- Namespaces follow project names (`AnalyseTool.Core.*`, `AnalyseTool.Tools.*`, `AnalyseTool.App.*`).

## Building

```powershell
# Full plugin chain (Debug deploys to %AppData%\Autodesk\Revit\Addins\<year>\ automatically!)
dotnet build src/AnalyseTool.Launcher/AnalyseTool.Launcher.csproj -c "Debug R25"   # or R26 / R27

# Boundary check (same script CI runs first)
powershell -File src/build/Check-Boundaries.ps1
```

- Configurations: `Debug|Release R25/R26/R27` → TFM `net8.0-windows` (R25/26) / `net10.0-windows` (R27), from `src/AnalyseTool.Sdk/build/AnalyseTool.Extension.props`.
- NuGet versions are centralized in `src/Directory.Packages.props` (CPM). `samples/` is deliberately OUTSIDE CPM — it simulates an external extension author. Floating Revit API versions use `VersionOverride`.
- Shared MSBuild deploy logic (MCP exe + clientapp/dist copying) lives in `src/PluginAssets.targets`.
- Releases go through NUKE (`src/build`, run `src/build.cmd`) — targets: BuildClientApp → BuildLauncher → CreateInstaller/CreateBundle → PublishGitHub.

## Guardrails (CI: .github/workflows/ci.yml)

1. `Check-Boundaries.ps1` — dependency contract + headless invariant.
2. Build chain for R25 (net8) and R27 (net10).
3. `dotnet pack` Sdk → build `samples/Acme.Sample` against the packed nupkg (`-p:UseSdkPackage=true`) in an isolated package cache — the external-author simulation. NuGet ignores package-shipped props during restore, so SDK consumers must declare TFM + `Nice3point.Revit.Api.*` themselves (see ONBOARDING.md §4.1).
4. Debug builds of Acme.Sample auto-deploy to `%LOCALAPPDATA%\AnalyseTool\extensions\<year>\` — a live smoke test of the real ALC loading path.

## Docs to keep in sync

- `ONBOARDING.md` — extension author guide (mirrored to the GitHub wiki), also the NuGet README of the Sdk package.
- `src/LLM.md` — paste-into-AI extension authoring instructions; `CreateExtensionTemplate.cs` embeds a generated copy — keep §4 (project setup) consistent in BOTH places.
- `CHANGELOG.md` — ships next to the plugin DLL (Settings window displays it).
