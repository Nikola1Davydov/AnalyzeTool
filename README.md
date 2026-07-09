# AnalyseTool for Revit

[![github release version](https://img.shields.io/github/v/release/Nikola1Davydov/AnalyzeTool.svg?include_prereleases)](https://github.com/Nikola1Davydov/AnalyzeTool/releases/latest)
[![License](https://img.shields.io/github/license/Nikola1Davydov/AnalyzeTool)](https://github.com/Nikola1Davydov/AnalyzeTool/blob/main/LICENSE)
![Static Badge](https://img.shields.io/badge/revitVersion-2025--2027-blue)
[![LINKEDIN](https://img.shields.io/badge/LINKEDIN-_NikolaiDavydov-ff1414)](https://linkedin.com/in/nikolai-davydov-4359bba1)

AnalyseTool is a free Revit plugin for family management, parameter analysis, filtering, bulk editing, and AI-assisted workflows.

The project focuses on free AI usage through local Ollama models, so you can run AI workflows without paid subscriptions — your model data stays on your machine.

![AnalyseTool Screenshot](img/Overview.png)

## Quick start

1. **Install** — download the latest installer from [Releases](https://github.com/Nikola1Davydov/AnalyzeTool/releases/latest), close Revit, run it.
2. **Open** — start Revit → **AnalyseTool** ribbon tab → three buttons: **AnalyseTool** (analysis), **Family Manager** (families), **Component** (placement palette).
3. **(Optional) AI** — install [Ollama](https://ollama.com/download), keep it running, then pick a model once in **Settings**. It's free and stays on your machine.
4. YouTube: https://www.youtube.com/@AnalyseTool-Revit

## Compatibility

- Revit 2025-2027 on Windows.

## The ribbon

Open the **AnalyseTool** tab — it has three main buttons:

- **AnalyseTool** — the main window: parameters, analytics, bulk editing and AI workflows.
- **Family Manager** — browse, audit and clean up the project's families.
- **Component** — a dockable palette for placing families and loading them from your libraries.

## Key Features

**🧱 Families**
- **Family Manager** — gallery & table views with thumbnails, an interactive 3D preview, family types (including system families), rename (with AI suggestions), delete, purge-unused with a progress bar, and saved filter rules.
- **Component palette** — a dockable pane (docks next to the Project Browser) for placing families: types grouped by family with previews, gallery/table views, search and quick-filter rules.
- **Family library** — browse your `.rfa` folders, see each file's thumbnail and the Revit version it was saved in, and load families into the project in one click.

**📊 Parameters**
- Category-based parameter exploration with filters (Instance/Type, BuiltIn/Shared/Project).
- Parameter Filled/Empty analytics with chart-driven selection.
- Parameter Value Check workflow.
- Infinite Canvas workflow with AI-assisted edits.
- Select / Isolate actions directly in Revit.

**🤖 AI**
- Free local AI via Ollama models — no paid subscription required.
- One shared model across the whole plugin, optional cloud models.

**🧩 Extensibility**
- Add your own commands, pages and ribbon buttons without rebuilding the plugin (see below).
- Update check against GitHub Releases.

## Your data stays local

AI runs against models on **your** machine via Ollama — nothing about your model is sent to us or to any AnalyseTool service. Cloud models are opt-in and go directly to the provider you configure.

## Installation

1. Download the latest installer from [Releases](https://github.com/Nikola1Davydov/AnalyzeTool/releases/latest) — pick **SingleUser** (current user, no admin) or **MultiUser** (all users, needs admin).
2. If you switch between SingleUser and MultiUser, **uninstall the previous AnalyseTool first** (otherwise you may end up with two copies / duplicate ribbon).
3. Close Revit, then run the installer. Windows SmartScreen may warn "unknown publisher" (the build isn't code-signed yet) — choose **More info → Run anyway**.
4. Start Revit and open the **AnalyseTool** ribbon tab.

## AI Requirement

To use AI features, you must install Ollama first:

- Download Ollama: https://ollama.com/download
- Keep Ollama running, so the plugin can access models.

## AI Model Settings

Pick the AI model once in **Settings** — it's shared across every AnalyseTool window (with an Ollama status indicator):

- `Local models` (recommended): load local Ollama models for free AI usage.
- `Cloud models` (optional): add a model name manually if you want to use an external provider; saved cloud models are remembered.

## Project Structure

- `src/AnalyseTool/`: C# Revit plugin (host).
- `src/AnalyseTool.Sdk/`: public SDK that extension authors compile against.
- `src/AnalyseTool.Launcher/`: thin Revit add-in shim that loads the host in isolation.
- `src/AnalyseTool.Mcp/`: out-of-process MCP server (AI integration).
- `src/clientapp/`: Vue 3 + Vite + PrimeVue frontend.
- `src/Installer/`: packaging and installation assets.
- `samples/`: example extensions.

## Extend AnalyseTool (for developers)

AnalyseTool is extensible — you can add your own **commands** (C#) and **UI pages** (any web
framework) **without rebuilding the plugin**. They drop into your extensions folder, load live on
**Reload**, and are automatically reachable from JavaScript (`AT.invoke`) and from AI clients over
MCP.

📖 **The full extension-authoring guide lives on the [Wiki](https://github.com/Nikola1Davydov/AnalyzeTool/wiki):**

- The extension model — C# command DLLs, JS/UI pages, and no-build script extensions (`.cs` compiled by Roslyn).
- The SDK contract — `IRevitTask`, `IRevitContext`, the `[RevitCommand]` attribute.
- The `plugin.json` manifest, deploy & live reload, and discovering commands from the frontend.
- AI / MCP integration.

The SDK is published on NuGet:

```
dotnet add package AnalyseTool.Sdk
```

## Troubleshooting

- **Blank AnalyseTool window** → the WebView2 Runtime is missing; install it (https://developer.microsoft.com/de-de/microsoft-edge/webview2?form=MA13LH) and restart Revit.
- **A new extension's ribbon button doesn't appear** → a brand-new button needs a Revit restart the first time; changing an existing extension only needs **Reload** (AnalyseTool → Settings → Reload).
- **Duplicate AnalyseTool tab / buttons** → both the SingleUser and MultiUser builds are installed; uninstall one.
- **AI tools don't update after toggling MCP / code execution** → the AI client caches the tool list; restart the client.
- **Logs** for diagnosing anything: `%LOCALAPPDATA%\AnalyseTool\logs\analysetool-<date>.log`.

## Feedback

Found a bug or have an idea? Use the [issue tracker](https://github.com/Nikola1Davydov/AnalyzeTool/issues) (or the **Report a bug** ribbon button) and attach the latest log from `%LOCALAPPDATA%\AnalyseTool\logs`. PRs welcome.
