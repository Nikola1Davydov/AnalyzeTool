# Changelog

## [1.4.0-beta.1] / 2026-06-06

- ⚠️ Beta release — please report issues on GitHub; see `TESTING.md` for what to try and how to report.
- 🧩 Extension system — add your own commands and UI without rebuilding the plugin: C# command DLLs, JS/HTML UI pages and ribbon buttons, dropped into `extensions\<RevitYear>\` and loaded live via Reload.
- 📝 Script extensions — drop a plain `.cs` file (no project, no build) and it's compiled on the fly into a working command + ribbon button ("pyRevit-for-C#").
- 🤖 MCP server — expose every command (built-in and from your extensions) to AI clients such as Claude Desktop over the Model Context Protocol; enable it in Settings.
- 🧪 (Experimental) AI C# execution + Save-as-command — let the AI run C# in Revit and promote a working snippet into a permanent command. Off by default; enable in Settings → C# code execution.
- ⚙️ New Settings page — host Environment (Revit / SDK / plugin versions), Extension paths (multiple source roots), a searchable Commands catalog, and MCP controls.
- 🧰 "New template" — scaffold an extension (UI-only / C# / Combo) right from Settings.
- 📚 Author guide (`ONBOARDING.md`) plus command discovery so web-extension authors can see what they can call.
- 🔧 Robustness & fixes — MCP stability, parameter null-guards, typo cleanups.
- 🗑️ Removed the Document Health page.

## [1.3.0] / 2026-05-10

- 🆕 Added a new "Parameter Value Check" page.
- 🔄 Added a visual "thinking" indicator for buttons during background operations.
- 🔧 Fixed compatibility with Revit 2026.
- 🔧 Fixed minor bugs in parameter validation and background operations.
- ⚙️ Improved internal processes to make UI actions more responsive.
- ⚙️ Made minor UI refinements for better clarity and consistency.
- 🆕 Added a new page: Infinite Kanban, where you can view diagrams and tables in one place.
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
