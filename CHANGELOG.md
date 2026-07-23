# Changelog

## [1.4.4] / 2026-07-23

- ⏳ **Revit busy indicator** — every AnalyseTool window shows a bottom status strip while something runs (command name, source, elapsed time) and warns **proactively** when Revit itself is blocked by an open dialog or edit mode — before you click and wonder why nothing happens. AI agents get the same insight via the new `GetQueueStatus` command (MCP): check it before heavy commands, wait while Revit is busy.
- ⚖️ **License** — the plugin is now licensed under **Apache 2.0** (the `AnalyseTool.Sdk` package stays MIT); NOTICE and third-party attributions ship with the plugin.
- 📦 SDK 1.1.1 — packaging fixes for extension authors (contract unchanged): the authoring props now work in projects with and without Central Package Management, the MIT license text is embedded in the package, and the docs/templates consistently use full parameter names (`revitContext`, `cancellationToken`).
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
