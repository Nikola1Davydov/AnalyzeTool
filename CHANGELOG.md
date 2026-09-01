# Changelog

## [Unreleased]

- 📐 **DWG without importing it.** Three new commands read a `.dwg`/`.dxf` **outside** Revit and let you create native geometry from the part you actually need. `GetDwgStructure` answers what is in the file — layers with per-type counts, blocks with how often they are placed, units, version, extents — before anything touches the document. `GetDwgEntities` returns the geometry itself as plain numbers. `ImportDwgAsCurves` turns the layers you pick into ordinary Revit detail or model curves. Nothing is imported or linked, so no `Import-*` line styles, foreign text types or nested blocks enter the project, and there is no `ImportInstance` to slow regeneration down. A bulged polyline segment becomes a real arc, not thirty short lines. DWG layers are matched to line styles of the same name, and the layers with no match are listed rather than silently defaulted.
- 🦀 The reader is a separate process (`analysetool-dwg`, in the plugin's `dwg` folder) built on the pure-Rust `acadrust` codec — no ODA membership, no RealDWG, no AutoCAD. It runs out of process on purpose: DWG is a reverse-engineered format and a malformed file can kill a parser, which in-process would mean taking Revit and an unsaved model with it. **Building it needs a Rust toolchain, and not having one is a warning rather than a build error** — the rest of the plugin is unaffected and the DWG commands say what is missing.
- ⚠️ Known limits, stated up front: everything lands on one elevation, only geometry with a +Z extrusion direction is converted, and text, points and block references are counted and reported instead of created — each needs a mapping decision that does not belong in a default. Civil 3D proxy objects (alignments, corridors, pipe networks) are a closed format no open library reads. A drawing that reports UNITLESS is refused until you say which unit it is in, because nothing can infer that.

## [1.5.0] / 2026-08-13

- 🤖 **Ask your AI to write a command — and to fix one that is broken.** The generate → run → refine loop is closed: a connected agent can now overwrite a command it wrote earlier (a second attempt used to need a new id or a manual delete), read the C# back, see exactly why a script failed to compile, save an HTML/CSS/JS page together with the ribbon button that opens it, and read the authoring guide it is expected to follow. In practice: a colleague's script that does not work in your project is now something you can hand to your AI with "look at it and fix it".
- 🧭 **`GetModelOverview` — the call that stops an agent guessing.** One read returns the document title, Revit version, **UI language**, display units, workshared flag, active view, levels and a per-category **instance** count. Both traps it closes are silent ones: category names are localised (on a German model the walls are `Wände`, and a filter for "Walls" returns nothing at all), and a length is millimetres or internal feet depending on the model.
- 📤 **Every command declares what it returns.** `OutputType` joins `InputType` in the SDK (**1.2**, additive — older extensions are unaffected), and MCP publishes the two as a pair, so a caller reads the response shape instead of inferring it from the description.
- 📝 **Every tool description says what it costs and whether it writes.** All 34 AI-visible commands now answer three questions in one order: read-only or **modifies the model**, what it costs (reads a table / scans something named / extracts geometry), and which command produces the ids it takes. Nineteen of them never said whether they wrote to the model at all.
- 🚦 **Bad arguments are caught before the command runs**, against the schema the command itself published, and every MCP error carries a machine-readable `code` — so an agent branches on "unknown command" versus "invalid arguments" instead of matching English text. A misspelled command or category comes back with a "did you mean".
- ⚠️ **Write-safety — Revit warnings reach you instead of being thrown away.** A modal Revit dialog raised inside a batch freezes the whole tool, so warnings are dismissed automatically — but until now they were also discarded, which is defensible for a button a human is watching and useless for anything unattended. Every write site now **records** them: the bulk parameter write, isolate, and all of delete / purge / rename / workset / load / place in the Family Manager. `SetDataToParameters` answers `{ ok, written, skipped, warnings }` — "500 written" and "460 written, 40 quietly dropped" used to look identical.
- ☠️ **`Destructive` now marks the commands that really are destructive** — the seven Family Manager commands that change model content. The flag was carrying extension management and parameter writes but not deletion or purge, so any rule keyed on it gated the harmless case and waved the irreversible one through. `PlaceFamilyInstance` is flagged too, and its description now states that it blocks on a human click, so an unattended agent knows not to call it.
- 🐛 **Fixed `GetElements` answering with the wrong elements.** `{ category: "Wände", limit: 10 }` returned ten wall **types** and not a single wall: instances and types were merged and then cut off the front, and system types sort first. Now `elementKind` selects instances (the default), types or both; `count` sits next to `returned` so truncation is visible; every element carries `familyId` / `familyName` so results can be joined with `GetFamilies`; and an unknown category is an error with a suggestion instead of an empty list that reads exactly like "this category is empty".
- ⏹️ **Cancel a running command from the UI.** The minute-long AI call is interruptible, and the cancellation reaches the model instead of merely abandoning the answer.
- 💬 **AI answers stream as they are written** instead of showing nothing until the last token arrives.
- 🧾 **New "Scripts" pane** — one ribbon button opens the list of your script commands: run one straight from its row, or step into its form if it takes input and back out to the list when you are done. The left border shows at a glance which commands ask for input, and a destructive one asks for confirmation first. Extensions that ship their own interface stay out of the list — they already have their own button.
- 📌 **You choose where each command lives** — pin any command to the ribbon or leave it in the Scripts list, one small toggle per command, no manifest editing. Works for built-in commands and installed packages just as well as for your own scripts.
- 📁 **You choose where generated scripts are saved.** Settings picks the dev folder new scripts land in, and refining a script that already exists writes it back to the folder it lives in — so a shared team folder added as an extension source keeps working, instead of the fix landing somewhere else.
- 🗑️ **Dev extensions can be removed from Settings** instead of by hand in Explorer. A folder that looks like a working copy (it contains a `.git`) is refused.
- 🧹 Settings lost the **"Create structure"** button. It created an empty `extensions` folder inside a folder you picked and then registered it — a `mkdir` behind a file dialog, for a folder "Add path" already accepts whether you made it yourself or not.
- 👀 **Two extensions claiming the same command id no longer fail silently** — diagnostics name the extension that won and the one it shadowed. That is the answer to "I edited the script and nothing changed".
- 🔒 **Security — the MCP server no longer confirms that hidden commands exist.** Commands hidden from the AI were kept out of the tool list, but a guessed name still reached the argument validator, whose reply listed the parameters — telling an agent that the C#-execution switch exists and what it takes. A hidden command is now indistinguishable from one that does not exist. Switching C# execution on remains something only a person can do.
- 🔒 **Security — `plugin.json` could be overwritten as though it were a page asset**, taking the extension's entry assembly, update feed and vendor fields with it. It is refused by name now, and saving a page no longer replaces the files beside it unasked.
- ⏱️ The MCP client no longer waits forever on a Revit that is blocked (a 10-minute ceiling), and a caller's own cancellation is still reported as cancellation rather than as a timeout.
- 🧱 CI gained a **command schema contract** gate — every command must describe itself, declare the input it reads and the output it returns. Writing it found 11 commands with undescribed input fields, which are fields an agent was already guessing at.

## [1.4.5] / 2026-07-27

- 🧩 **Extension manager** — Settings now installs, removes, enables/disables and updates extensions instead of only listing them. Two zones are kept apart: **Installed** packages the manager owns, and your own **Dev** folders it never touches. Install from a `.zip` (with a third-party consent prompt), see an update badge when a newer version is published, and read per-extension diagnostics when something fails to load.
- 📦 **One package for every Revit version** — an extension is now a single zip whose per-year binaries live inside it (`MyExt\2025\MyExt.dll`), with `plugin.json`, scripts, `ui/` and the icon in the root. No more one folder per Revit year: the extension folder sits directly under the extensions root and the year is a subfolder of it. Old `extensions\<year>\<id>\` folders keep loading unchanged.
- 🚚 **Publishing pipeline for authors** — `dotnet build -t:PackExtension` builds every Revit year and produces the release zip; a `github:owner/repo` feed in `plugin.json` is enough for the manager to offer updates. No server, no marketplace.
- 🏷️ **Manifest v2** (additive, old manifests keep working) — `description`, `publisher`, `website`, `supportUrl`, `icon` and `updateFeed`; `ui.button.command` lets a ribbon button run a command instead of opening a page.
- 🛠️ Fixed **"New template → C#"** producing a project that built into `bin\` instead of the extension folder — following the documented `dotnet build` gave an extension the host could not load. The generated project now derives its output folder, its Revit API packages and its target framework from a single `RevitVersion` property, so `dotnet build -p:RevitVersion=2026` retargets it without editing a file.
- 🔍 An extension that was never built now reads **"Not built"** with instructions, instead of **"Incompatible"** — which sent authors looking for a Revit-version problem that was not there. "Incompatible" is kept for its real case and now names the years the extension does ship.
- 📖 `ONBOARDING.md` and the paste-into-AI `LLM.md` rewritten for the new layout, including a migration section for extensions built against the old one.
- 🔒 **Security — the MCP server now enforces which commands an AI may run.** Commands marked as hidden from the AI (plugin management, and the C# code-execution switch) were filtered out of the tool *list* but still executed if called by name — so a connected agent could switch on C# execution and run arbitrary code in Revit. Listing and invoking now share one rule.
- 🔒 **Security — the MCP bridge requires a token.** It listens on localhost, which keeps the network out but not other programs running under your account; every request now has to carry a per-machine secret. **If you already use the MCP server, re-copy the config snippet from Settings → MCP server** — it gained a `--token` argument, and clients without it are refused.
- 🔒 **Security — vendor links in `plugin.json` are restricted to `http`/`https`.** A `javascript:` address in an extension's `website`/`supportUrl` ran as script inside the Settings page when clicked, with access to every plugin command. Such links are now dropped, on the host side and in the UI.
- 🧱 Stability — installing, updating, enabling or removing an extension while another command is running no longer risks "command is not registered" errors or leaked load contexts: the command registry is concurrent and reloads are serialized.
- 🔌 The MCP bridge survives a failed connection attempt instead of silently ending the session (Settings kept showing "running"), rejects oversized messages, and parses incoming data in one pass.
- ⏳ The Revit busy indicator no longer polls in a window that was closed, and idles at a much slower cadence; a failed toggle in Settings now reports the error and snaps back instead of showing a state the plugin never accepted.

## [1.4.4] / 2026-07-23

- ⏳ **Revit busy indicator** — every AnalyseTool window shows a bottom status strip while something runs (command name, source, elapsed time) and warns **proactively** when Revit itself is blocked by an open dialog or edit mode — before you click and wonder why nothing happens. AI agents get the same insight via the new `GetQueueStatus` command (MCP): check it before heavy commands, wait while Revit is busy.
- ⚖️ **License** — the plugin is now licensed under **Apache 2.0** (the `AnalyseTool.Sdk` package stays MIT); NOTICE and third-party attributions ship with the plugin.
- 📦 SDK 1.1.1 — packaging fixes for extension authors (contract unchanged): the authoring props now work in projects with and without Central Package Management, the MIT license text is embedded in the package, and the docs/templates consistently use full parameter names (`revitContext`, `cancellationToken`).
- 🤝 New template — **every** template flavour (UI-only included) now ships `LLM.md`, the paste-into-AI authoring guide; previously only C# templates got it.
- 🧱 Internal: the codebase was restructured into feature slices with a headless core; both transports — the WebView UI and the MCP server — now reach commands through one shared queue. CI logic moved into the Nuke build, so `build.cmd Ci` runs the exact CI checks locally.

## [1.4.3] / 2026-07-14

- 🏗️ **Revit 2027 support** — the plugin (and the extension SDK / build configs) now covers Revit 2025, 2026 and 2027.
- 🌐 **Multiple AI providers** — connect any OpenAI-compatible endpoint (OpenAI, OpenRouter, Groq, Mistral — or local LM Studio / vLLM) next to the built-in local Ollama: add providers in Settings (base URL + API key + test connection), pick the provider & model once and every AI feature uses it. API keys are stored encrypted (Windows DPAPI) on your machine and never leave it; clear messages for rate-limit / key / credit errors.
- 🔤 **Naming rules** — compose family/type names from real data with reusable templates, e.g. `{category|abbr}_{param:Material|abbr}_{param:Width}x{param:Height}` → `Möb_Alu_1000x2000`: token builder with the actual parameters of your selection, a shared abbreviation dictionary, live preview on real elements, and one-click apply with review. Rules are deterministic — same input, same names, no AI required to apply.
- ✨ **AI creates the rule from one example** — type the name you WANT for a sample element and the AI reverse-engineers the template and the abbreviations for you; you review the live preview and save.
- ☑️ **Multi-select & bulk rename** — select many families or types (checkboxes) and rename them all at once: by naming rule or with a free-text AI instruction (one request for the whole list → consistent scheme), with an editable review table, name-conflict detection and live progress. Bulk delete and the workset move now live in the same contextual bar.
- 🧲 Component palette — auto-reloads when you switch the active document (and shows a clean "no open document" state); right-click a family → **View in 3D** (the interactive viewer, straight from the palette); tidier toolbar (view & settings moved into the collapsible source row).
- 🛠️ Fixed the dockable pane sometimes staying **black** when Revit restored it at startup — the pane now initializes reliably, recovers from browser-process crashes, and shows a visible error with a Retry button instead of an empty surface.
- 🔧 Fixed saved filter rules not working for family **types** in the Family Manager.
- 📖 Settings — new **"What's new"** button next to the plugin version opens the changelog (shipped with the plugin).

## [1.4.2] / 2026-07-02

- 🧱 **Family Manager** — a new second ribbon button opening a dedicated window to browse, audit and manage the project's families.
  - 🖼️ Gallery & Table views of every family with category, type and instance counts, in-place / unused flags, lazy-loaded thumbnails and a category filter.
  - 🧊 3D preview — click a family to open an interactive Three.js viewer (approximate material colours and transparency, correct placement of nested families) alongside a panel of its types and parameters, with a refresh button that rebuilds the cached geometry.
  - 🧩 Family Types view — families' types (including system families) grouped by name, with type thumbnails, Select / Isolate / Rename / Delete, a "move all instances to another workset" action and one-click "Purge unused types" (a family's last type is always kept, as Revit requires).
  - 🧹 Actions — Select, Isolate, Rename, Delete and Purge-unused, straight from the table, gallery or detail view, with live progress bars on long deletes.
  - 🤖 AI rename — the rename dialog can ask your Ollama / saved cloud model to suggest a better family or type name.
  - 🔖 Saved filter rules — build reusable field/condition rules, pin them as one-click quick filters across the views.
  - ⚡ Client-side caching — previews and meshes are cached in the WebView and invalidated automatically when a family changes.
- 🧲 **Component palette (dockable)** — a new ribbon button opens a dockable pane (docks next to the Project Browser) for placing families: types grouped by family with previews, gallery/table views, search, its own saved quick-filter rules and persisted grouping/sorting; click a type to start Revit's placement.
- 📚 **Family library** — the palette's Library mode browses your .rfa folders (add/remove folders, per-folder filter), shows each file's embedded thumbnail and the Revit version it was saved in, flags what's already in the document, and loads families into the project with a progress bar; files saved in a newer Revit are marked as not loadable.
- 🧩 Extensions — a JS extension can declare `"dockable": true` to show its page inside the shared dockable pane (toggle open / switch / close) instead of a separate window; picked up live via Reload.
- 🛠️ SDK 1.1 — new opt-in `IProgressAware` contract: long-running commands report progress and the UI shows a live progress bar; extensions built against SDK 1.0 keep working unchanged.
- 🎛️ One shared AI model — pick the Ollama / cloud model once in Settings (with an Ollama status indicator and saved cloud models); every window shows and uses the same model.
- 🔗 Settings — the plugin version now shows an "update available" badge and download link when a newer release exists (same check as the main window).

- 🛠️ Fixed the "New template → C#" scaffold — it generated code that didn't compile (an invalid lambda in `Hello.cs`); the template now produces a correct, ready-to-build command. (#38)
- 🤝 AI-assisted authoring — each generated template now ships an `LLM.md`, and the README and SDK include the same guide: paste it into Claude/ChatGPT and it writes AnalyseTool extensions for you.
- 📖 Docs — the README and extension guide now point to the GitHub Wiki, with project paths updated for the new `src/` layout.

## [1.4.0] / 2026-06-20

- 🧩 Extension system — add your own commands and UI **without rebuilding the plugin**: C# command DLLs, JS/HTML UI pages and ribbon buttons, dropped into `extensions\<RevitYear>\` and loaded live via Reload.
- 📝 Script extensions — drop a plain `.cs` file (no project, no build) and it's compiled on the fly into a working command + ribbon button ("pyRevit-for-C#").
- 📦 SDK on NuGet — write extensions with `dotnet add package AnalyseTool.Sdk`; full authoring guide on the GitHub Wiki.
- 🤖 MCP server — expose every command (built-in and from your extensions) to AI clients such as Claude Desktop over the Model Context Protocol; enable it in Settings.
- 🧪 (Experimental) AI C# execution + Save-as-command — let the AI run C# in Revit and promote a working snippet into a permanent command. Off by default; enable in Settings → C# code execution.
- ⚙️ New Settings page — host Environment (Revit / SDK / plugin versions), Extension paths (multiple source roots), a searchable Commands catalog, and MCP controls.
- 🧰 "New template" — scaffold an extension (UI-only / C# / Combo) right from Settings.
- 🪛 Reworked ribbon — stacked Settings / Reload / Report-a-bug buttons (Report-a-bug opens the GitHub issues page).
- 🩺 Added a diagnostics log file (`%LOCALAPPDATA%\AnalyseTool\logs`) and a clear prompt if the WebView2 Runtime is missing.
- 🔧 Robustness & fixes — MCP stability, parameter null-guards, typo cleanups.
- 🗑️ Removed the Document Health page.

## [1.3.0] / 2026-05-10

- 🆕 Added a new "Home" page. Infinite Kanban, where you can view diagrams and tables in one place.
- 🔄 Added a visual "thinking" indicator for buttons during background operations.
- 🔧 Fixed minor bugs in parameter validation and background operations.
- ⚙️ Improved internal processes to make UI actions more responsive.
- ⚙️ Made minor UI refinements for better clarity and consistency.
- ✏️ Added parameter editing functionality.
- 🤖 Added Ollama integration, which enables free local AI usage (Ollama installation is required).
- 🧠 Added AI mode: you can edit parameters with AI or analyze them with AI.

## [1.2.1] / 2025-12-06

- 🆕 Added a new page "Parameter Value Check".
- 🔄 Added a visual "thinking" indicator for buttons to inform users that background operations are running.
- 🔧 Fixed a bug with Revit 2026; the plugin now works correctly with this version.
- 🔧 Fixed several minor bugs related to parameter validation and background operations.
- ⚙️ Improved several internal processes to make UI actions more responsive.
- ⚙️ Minor UI refinements for better clarity and consistency.

## [1.2.0] / 2025-11-29

- Added a new web-based visual interface.
- Added a new code architecture: backend C# and frontend JavaScript/TypeScript with Vue.
- Added a diagram.
- Added a new page "About".
- Removed Revit 2024 support (focus on newer versions).

## [1.1.0] / 2025-09-19

- Added support for Revit 2026.
- Added a brand-new visual interface.
- Made all parameters visible (not only shared parameters).
- Added element selection via right-click on rows.
- Added category selection via ComboBox instead of loading all at once.
- Added parameter filtering by Instance/Type and BuiltIn/Shared/Project.
- Removed Revit 2023 support (focus on newer versions).

## [1.0.0] / 2024-09-18

- First public release of AnalyseTool plugin.
