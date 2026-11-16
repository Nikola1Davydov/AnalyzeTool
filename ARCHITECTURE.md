# Architecture

How the add-in starts, where the code goes and why. The rules an agent must not break are in [AGENTS.md](AGENTS.md).

## Projects

| Project | Role |
| --- | --- |
| `source/AnalyseTool.Launcher` | The only assembly Revit loads. Starts the add-in in its own `AssemblyLoadContext`; holds no button: the ribbon is built by the add-in. References nothing but the Revit API. |
| `source/AnalyseTool` | The add-in: `Application`, `Host`, buttons (`Ribbon/analysetool/plugin.json`), commands (`Features/`), services (`Services/`), the React app (`ClientApp/`), plumbing (`Infrastructure/`). |
| `source/AnalyseTool.ServiceDefaults` | Logging, configuration layers, JSON options, DI validation. |
| `source/AnalyseTool.Hosting` | Module: the command pipeline, `IRevitDispatcher`, Revit lifetime, user settings, notifications. No WPF. |
| `source/AnalyseTool.Hosting.WebView` | Module: WebView2 windows, the dock pane, the transport to the pipeline and `revit-bridge` for the pages. |
| `source/AnalyseTool.Hosting.Mcp` | Module: an MCP server inside Revit, the transport of AI clients to the pipeline. No WPF. |
| `source/AnalyseTool.Hosting.Scripting` | Module: the `ExecuteCode` command — C# compiled with Roslyn and run inside Revit. No WPF. |
| `source/AnalyseTool.Hosting.Visualization` | Module: `IGeometryOverlay` — geometry drawn over the views with DirectContext3D, nothing added to the model. No WPF. |
| `source/AnalyseTool.Sdk` | The public contract of extensions (`IRevitTask`, `IRevitContext`, `RevitPayload`, `[RevitCommand]`), packed to NuGet. |
| `source/AnalyseTool.Extensions` | Module: the user's buttons — tab folders laid out as the ribbon (`<Tab>.tab\<Panel>.panel\<Button>.pushbutton\`, one extension and one git history per button) and 1.x folders with a `plugin.json`; compiled from C# or loaded as DLLs, reloaded when they change. No WPF. |
| `build/` | The build pipeline (ModularPipelines): boundary check, `ClientApp`, compile, `pack`. |
| `install/` | The `.msi` builder (WixSharp). |

The modules are the add-in's own code, not a dependency: change them as the add-in needs. `AnalyseTool.Hosting`,
`AnalyseTool.Hosting.Mcp`, `AnalyseTool.Hosting.Scripting` and `AnalyseTool.Hosting.Visualization` stay headless, so
the pipeline can run without the UI.

## Startup and isolation

1. Revit loads `AnalyseTool.Launcher.addin` and calls `App.OnStartup` of the Launcher.
2. The Launcher creates a `PluginLoadContext` from its `deps.json` and loads `AnalyseTool.dll` into it. Every
   dependency — Microsoft.Extensions.*, Nice3point libraries, WebView2, Serilog — loads there too, so another add-in with
   other versions of them cannot clash. The Revit API and the shared framework come from Revit's own context.
3. The Launcher publishes its path and the namespace of its proxies through `AppContext` (keys end with the add-in name)
   and creates `AnalyseTool.Application` by name — the single reflection call into the add-in.
4. `Application` builds and starts the `Host` (`Host.cs`), then the ribbon (`AppRibbon`).

If any step fails, the Launcher writes the exception to `%LOCALAPPDATA%\AnalyseTool\logs\<year>\startup-error.txt`
and rethrows it; Revit reports the failed external application itself.

## Ribbon

Every button is declared one way — an entry of `ui.buttons` in a `plugin.json` — and built one way. AnalyseTool's own
buttons are in `Ribbonnalysetool\plugin.json` next to the add-in (`Ribbonnalysetool.dev\` adds the Dev panel in
Debug builds); the user's are in their extension folders.

* `ButtonCatalog` lists the folders: the built-in ones first, then every extension that has buttons.
* A button says where it shows: `"ribbon": false` makes it a tile of the pane only, `"pane": false` a ribbon button only.
  AnalyseTool's own ribbon is one button — Buttons (the pane); refresh (`RefreshButtons`) is ↻ in its header, the rest are tiles or links of
  the pane's profile. A folder (extension) is created empty (`CreateExtensionFolder`) and gets buttons one by one
  (`AddExtensionButton`: C# in `src\` and/or a page, the entry in `plugin.json`, a version each).
* The structure of the pane is the ribbon (`ButtonCatalog.Place`): a folder of the user is a tab of its name, its sections
  (`panel` of a button, ordered by `ui.panels`) are the panels; a package keeps the tab its author chose; AnalyseTool's own
  buttons go where their plugin.json says. Moving a button is `MoveExtensionButton` (another panel, or another folder with
  its C# file and page — the command is renamed), a panel is `SetExtensionPanel`.
* What the ribbon loads is the user's (`RibbonVisibility`, from `pane-profile.json`): `hiddenFolders` (a folder's tab off),
  `hiddenButtons` (a button off), `ribbon` (a button on although plugin.json says `"ribbon": false`). ☆ is the `dock` array,
  for the pane only. Saving the profile rebuilds the ribbon.
* `AppRibbon` builds the buttons with AdWindows (the Revit API cannot add or remove buttons after startup), on the
  AnalyseTool tab unless a button names another, and rebuilds them on `ExtensionHost.Changed`.
* `ButtonRunner` handles every click: it counts it (`ButtonUsageStore`, `%LOCALAPPDATA%\AnalyseToolutton-usage.json`),
  then runs the button's pipeline command with its `args` (source `ribbon`: a failure is shown to the user) and shows
  the result, or opens the button's page. The built-in buttons open their pages with `OpenWindow { route, dock? }`.
* The dock pane (`/dock`) shows the same catalog as tiles (`GetPaneButtons`); a tap is `RunButton { key }` — the same
  `ButtonRunner`, counted the same, the result shown in the pane. Its profile (dock of favourites, order, tile size,
  theme) is `pane-profile.json` in the profile folder (`GetPaneProfile` / `SavePaneProfile`).
* The Statistics page (`/statistics`, `GetButtonStatistics`) joins the catalog with the counts: every button, clicked or
  not, and the buttons of removed extensions until a reset.

Since no button is a Revit `IExternalCommand`, Revit never creates a class of ours, and the Launcher's `Proxies.cs` is
empty. The price: Revit keyboard shortcuts cannot be bound to these buttons.

## Host

`Host.cs` is the `Program.cs` of the add-in: a Generic Host with dependency injection, configuration and logging.

* `AddServiceDefaults()` — logging, configuration layers with `IUserSettings`, JSON options, DI validation at `Build()`.
* `AddRevitHosting(application)` — `IRevitDispatcher`, `IRevitLifetime`, `IUserNotifier`.
* `AddRevitCommands(...)` — the command registry, the middleware of the pipeline, `ICommandExecutor` and `ICommandSender`.
* `AddWebView(application, ...)` — windows, the dock pane and the page source.
* `AddMcp(application, basePort)` — the MCP server for AI clients.
* `commands.AddScripting(builder)` inside `AddRevitCommands` — the `ExecuteCode` command, when scripting is on.
* `AddVisualization()` — `IGeometryOverlay`, geometry drawn over the views.

Classes get their dependencies through the constructor. Only `Application`, which Revit creates itself, resolves
services with `Host.GetService<T>()`.

## Commands and the pipeline

A command is a class in `Features/<slice>/` that implements `ICommand<TResponse>` or `ICommand<TRequest, TResponse>`,
like a controller of an ASP.NET Core app. The registry finds command classes by reflection at startup and reports every
problem at once; the command name is the class name without the `Command` suffix, or the name from `[Command]`.

A call goes `ExceptionHandling → CommandLogging → DocumentCheckMiddleware → command` and always runs on the thread pool,
never on the Revit thread. Exceptions become a result with an error code: `CommandException(code, message)` for an
expected refusal, anything else becomes `failed`. Pages call commands through `revit-bridge`, buttons through
`ICommandSender`; a failure of a button call is shown with `IUserNotifier`.

## Revit thread and the document

The Revit API works only on the Revit thread. A command reaches it through `IRevitDispatcher.InvokeAsync(...)` — one
long-lived external event — or, for the active document, through `ActiveDocument`:

* `InvokeAsync(document => ...)` and `InvokeUIAsync(uiDocument => ...)` run work on the Revit thread; transactions are
  allowed inside.
* The first access fixes the document of the call. If the user switches documents before a later access, that access
  fails with `document_changed`; with no document open, with `no_document`.
* `DocumentCheckMiddleware` fixes the document up front from the `Document` header a page sends, so data loaded from one
  document is never applied to another.
* `InvokeBatchedAsync(items, batchSize, work, progress, cancellationToken)` runs long work in batches: between batches
  Revit gets control back, the page shows progress and the user can cancel.

## Pages

`ClientApp/` is a React app (Vite, TanStack Query). A page is a hash route: `IWindows.Open("/route")` shows it in a
window — one window per route — and `IWindows.ShowDockPane("/route")` in the dock pane. In Debug the pages come from
the Vite dev server (`ClientApp:DevServerUrl`, `http://localhost:5173` by default), in Release from `ClientApp/dist`
through the virtual host `https://app.analysetool.local`.

`revit-bridge` sends a request over WebView2 messaging and matches the response by its id; progress and cancellation go
the same way. The hooks in `ClientApp/src/revit/hooks.ts` add the `Document` header: `useActiveDocument()`,
`useCommand(name, payload)` to read, `useCommandMutation(name)` to run with progress and `cancel()`. Outside Revit, in a
normal browser, `ClientApp/src/mocks.ts` answers the calls.

Any page loaded in the WebView can call commands, so by default only the add-in's own pages load: another address opens
in the system browser, and messages from another origin are dropped. `AllowAnyNavigation()` in `AddWebView` turns the
protection off — `Host.cs` of this template calls it.

## AI clients (MCP)

`AnalyseTool.Hosting.Mcp` is an MCP server inside Revit: Claude Code, Cursor or VS Code connect to
`http://localhost:<port>/mcp` over Streamable HTTP while Revit runs; no separate process. A tool is a pipeline command
marked with `[McpTool]` — the call goes through the same middleware as a page or a button, with the source `mcp` — and
nothing else is visible to the client. A command failure is a tool result with `isError` and `code: message`.

* On in Debug, off in Release; `Mcp:Enabled` in the settings turns it on or off.
* The port: the base port of `AddMcp` for Revit 2025, plus one per later version, so several Revit versions run side by
  side; `Mcp:Port` overrides it. A second Revit of the same version finds the port busy, logs a warning and runs without MCP.
* Only `localhost`, a bearer token on every request, and no request with an `Origin` header — a web page in the user's
  browser cannot reach the model.
* **One address for every Revit version** (`basePort - 1`, 48075): the first Revit holds it, the others retry every
  five seconds; each Revit also keeps its own port. `McpPeers` registers each Revit in `%LOCALAPPDATA%\AnalyseTool\mcp\`
  (version, port, when its window was in front); every tool takes `revit`, a call for another version is sent on to it
  as an MCP client, a call without one goes to the Revit used last; `ListRevitVersions` lists them.
* `%LOCALAPPDATA%\AnalyseTool\mcp.json` holds that address, the one token (kept across Revit sessions) and the
  `claude mcp add …` command. The plugin for Claude Code and Codex in `source/AnalyseTool/Plugin/` (copied next to the add-in; one plugin folder, a manifest and an MCP file for each client) reads
  the token there with a `headersHelper`.
* `IMcpEndpoint.GetInfo()` returns the same details for a page; in Debug the «MCP» tab of the Dev page shows them.
* A command that returns `McpImage` gives the client a picture, not JSON.
* The tools of the template: `GetActiveDocument`, `GetSelection`, `SelectElements`, `InspectElement` (the properties and
  parameters of an element, one level at a time, as RevitLookup shows them), `RenderView` (a PNG of the active view), `ShowGeometry` / `ClearGeometry` (geometry drawn over the views), `CountElementsByCategory`; in Debug `GetLogEntries` (the add-in log); with scripting `ExecuteCode` and
  `DescribeApi` (the Revit API of the running version, by reflection and `RevitAPI.xml`). How an agent checks its work
  with them — the `verify-in-revit` skill.
* `McpHttpServer` serves both protocol revisions: the `initialize` handshake gets a session (`Mcp-Session-Id`), the
  session-less revision of 2026 gets a server per request. The protocol itself is the SDK's (`ModelContextProtocol.Core`);
  its ASP.NET Core package does not fit — Revit has no ASP.NET Core.

## Running C# (Scripting)

`ExecuteCode` compiles a C# method body with Roslyn and runs it once on the Revit thread — the scratchpad of an AI client
over MCP, and of the «Commands» tab of the Dev page (`{ "code": "return doc.Title;" }`).

* The body sees `uiapp`, `uidoc`, `doc` and `cancellationToken` and may `return` a value; leading `using` directives are
  lifted above the generated class. Errors come back as `compile_failed` or `script_failed`, with the line of the body.
* It compiles on the thread pool against the assemblies the add-in runs with, runs and serializes the result on the Revit
  thread, and unloads the script's assembly afterwards. The whole script is one Revit call: Revit waits until it returns.
* With an open document the script runs inside one transaction group: its changes are one undo step, an exception rolls
  them back, and `dryRun` runs the code, reports what it changed and rolls back. The answer has `value`, `changes`
  (added, modified, deleted elements) and `rolledBack`.
* Every run is a JSON line in `%LOCALAPPDATA%\AnalyseTool\scripts\<date>.jsonl`: time, document, outcome,
  changes and the code.
* Any code runs with the user's rights. The command exists only while scripting is on: in Debug by default, otherwise with
  `Scripting:Enabled` — read at startup. Off, neither a page nor an AI client can reach it.

## Showing geometry (Visualization)

`IGeometryOverlay` draws geometry over the views of a document — what a command found or computed — without touching the
model: no elements, no transaction, no undo step, nothing saved. It is a DirectContext3D server, registered at the first
`Show` and kept until Revit closes; with nothing to show it declines every view.

* `OverlayGeometry` collects what to draw in one colour: an element (`Add(element)` — its solids, meshes and curves),
  any `GeometryObject`, a `BoundingBoxXYZ`, a segment, a point (a small cross). It reads the geometry at once, on the Revit
  thread, and keeps only points: when the element changes, show it again. The faces and edges of an element move out
  along their normals by `ElementOffset` (about 6 mm): drawn exactly on the element, they would be hidden by it.
* Faces are drawn with the transparency of the geometry, edges and curves opaque. Coordinates are in feet, in model
  coordinates.
* The geometry stays until `Clear()` or until its document closes. It is drawn in 3D views and plans and in their
  exported images, so `RenderView` shows it. The cut of an element covers what lies behind the cut plane, and nothing
  beyond the view range is drawn, so in a plan the server presses what is above the cut plane just onto it (what is
  below, in a ceiling plan): a cut wall shows as its coloured footprint.
* The commands `ShowGeometry` and `ClearGeometry` in `Features/Views/` give it to pages and AI clients: elements by id,
  boxes, segments and points, a colour per call.

```csharp
overlay.Show(document, new OverlayGeometry(new Color(255, 0, 0), transparency: 60).Add(new BoundingBoxXYZ { Min = min, Max = max }));
```

## Settings and logs

Configuration layers, a later one overrides an earlier one:

1. `appsettings.json` next to the add-in;
2. `appsettings.{Environment}.json` — `Development` in Debug, `Production` in Release;
3. `%AppData%\AnalyseTool\<year>\usersettings.json` — what the user changed, written by `IUserSettings.UpdateAsync`;
4. environment variables prefixed with the add-in name in upper case, dots replaced with underscores.

Code reads settings with `IOptions<T>` / `IOptionsMonitor<T>`. `IUserSettings.UpdateAsync` writes the whole section, so
once written, `appsettings.json` no longer changes that section for this user.

Logs go to the Revit journal (from `Information` in Debug, from `Warning` in Release) and to
`%LOCALAPPDATA%\AnalyseTool\logs\<year>\log-<date>.txt` (from `Information`, a file per day). Both levels are set in
the `Logging` section.

A Debug build also keeps the last records in memory (`LogBuffer`) for the «Dev page» button of the «Dev» ribbon panel:
the `/dev` route of the dock pane shows the live log, runs any registered command with a JSON payload typed by hand
and, on its «MCP» tab, shows how to connect an AI client — the command for Claude Code and the JSON for other clients.
The page polls `GetLogEntries`; the records of that polling are dropped from the buffer.

## Boundaries

`build/Check-Boundaries.ps1` runs on every pipeline run and fails it on:

* a project or package reference that the table at the top of the script does not allow;
* WPF in `AnalyseTool.Hosting`, `AnalyseTool.Hosting.Mcp`, `AnalyseTool.Hosting.Scripting` or
  `AnalyseTool.Hosting.Visualization`, or in `Features/` and `Services/` of the add-in;
* `ActiveUIDocument` in `Features/` and `Services/`, outside `ActiveDocument.cs` and lines marked `// AllowActiveDocument:`;
* a type of the add-in or the modules in the Launcher code.

Comments and strings do not count. Run it alone with `pwsh build/Check-Boundaries.ps1`.

## Extensions: the user's buttons

New buttons live in tabs, laid out as the ribbon (`Manifest/ButtonFolder.cs`); every button folder is an extension of
its own, id `<tab>.<button>`, with its own build and history:

```
Sheets.tab\                bundle.json: name, layout of the panels; _shared\*.cs for every button of the tab
  Numbering.panel\         bundle.json: name, layout of the buttons
    Renumber.pushbutton\   .git\ — its own history
      bundle.json           name, tooltip, icon, "command": "<Class>" | "page", dockable
      Renumber.cs           its C#
      AGENTS.md             where the authoring guide is: LLM.md next to the add-in, updated with it
      CLAUDE.md             @AGENTS.md, for Claude Code
      .editor\              a .csproj for IntelliSense only (git-ignored)
```

`ButtonFolder.ToPluginJson` reads a button folder as a one-button `plugin.json`, so build, load, history, statistics and
MCP treat it like any extension. A folder with a `plugin.json` is the other form: DLL projects, packages, AnalyseTool's
own buttons, and folders of 1.x (in the pane «Older buttons», moved into tabs with `MoveToTabAsync`):

```
acme.sample  .git\
  plugin.json      id, "build": "author", entryAssembly, ui.tab / ui.buttons
  2025\Acme.Sample.dll   its build per Revit year (versioned: the DLLs are the extension)
```

* **Compile.** `ExtensionCompiler` compiles the `*.cs` of a button folder and `_shared\` of its tab (a `plugin.json` folder: `src\**\*.cs`) with Roslyn against what the add-in runs with (the Revit API of
  the running Revit, `AnalyseTool.Sdk`, Newtonsoft) and `lib\*.dll`; `ExtensionBuilder` writes `bin\<year>\<id>.<hash>.dll`
  only when the sources changed. A compile error keeps the last good build loaded; the diagnostics carry file and line.
* **DLLs** (`entryAssembly`, `<year>\X.dll`) load as in 1.x. Both kinds load from bytes into a collectible
  `ExtensionLoadContext`; the Sdk and Newtonsoft resolve to the add-in's own copies (type identity).
* **Commands.** `SdkCommands` turns each `IRevitTask` into a `CommandDescriptor` named `<id>.<Name>` with
  `CommandAnnotations` (MCP exposure, hints, input/output schemas); `ExtensionHost` registers them as the registry group of
  the extension, so they run through the same pipeline as built-in commands and appear or disappear without a restart.
* **Live.** `ExtensionWatcher` watches the roots: a saved file reloads its extension a moment later; `ExtensionHost.Changed`
  rebuilds the ribbon buttons (AdWindows) and MCP announces `tools/list_changed`.
* **Versions.** `IExtensionHistory` (LibGit2Sharp, in process): save a version, history (of a file), diff, restore (the
  unsaved state is saved first, the restored state becomes a new version) and pin a source extension to a version — it then
  compiles the sources of that commit; the pin lives in `extensions-state.json`, not in the folder.
* **Pages.** A button without `command` opens the extension's HTML in a window or the dock pane under its own virtual host
  (`<id>.ext.analysetool.local`) with `window.AT.invoke(command, payload)` injected — the API of 1.x.
* **Packages.** A `.zip` with one extension installs into `extensions-dist\`; an update keeps the folder's history.

