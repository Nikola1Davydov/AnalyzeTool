# AnalyseTool for Revit

[![github release version](https://img.shields.io/github/v/release/Nikola1Davydov/AnalyzeTool.svg?include_prereleases)](https://github.com/Nikola1Davydov/AnalyzeTool/releases/latest)
[![license](https://img.shields.io/github/license/nhn/tui.editor.svg)](https://github.com/Nikola1Davydov/AnalyzeTool/blob/master/LICENSE)
![Static Badge](https://img.shields.io/badge/revitVersion-2025--2026-blue)
[![LINKEDIN](https://img.shields.io/badge/LINKEDIN-_NikolaiDavydov-ff1414)](https://linkedin.com/in/nikolai-davydov-4359bba1)

AnalyseTool is a free Revit plugin for parameter analysis, filtering, bulk editing, and AI-assisted workflows.

The project focuses on free AI usage through local Ollama models, so you can run AI workflows without paid subscriptions.

![AnalyseTool Screenshot](img/Overview.png)

## Compatibility

- Revit 2025-2026

## Key Features

- Free plugin with built-in AI workflows.
- Free local AI support with Ollama models.
- Category-based parameter exploration with filters (Instance/Type, BuiltIn/Shared/Project).
- Parameter Filled/Empty analytics with chart-driven selection.
- Parameter Value Check workflow.
- Infinite Canvas workflow with AI-assisted edits.
- Select/Isolate actions directly in Revit.
- Update check against GitHub Releases.

## Free Plugin + Free AI

- AnalyseTool is free to use.
- Core AI scenarios are designed to work with free local models via Ollama.
- No mandatory paid AI subscription is required for local AI workflows.
- Cloud model mode is optional and depends on your provider setup.

## Installation

1. Download the latest package from [Releases](https://github.com/Nikola1Davydov/AnalyzeTool/releases/latest).
2. Close Revit.
3. Install the plugin package.
4. Start Revit and open `Add-Ins -> AnalyseTool`.

## AI Requirement

To use AI features, you must install Ollama first:

- Download Ollama: https://ollama.com/download
- Keep Ollama running, so the plugin can access models.

## AI Model Settings

In Canvas AI settings:

- `Local models` (recommended): load local Ollama models for free AI usage.
- `Cloud models` (optional): input model name manually if you want to use an external provider.

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
dotnet add package AnalyseTool.Sdk --version 1.0.0
```

## Feedback

Issues and PRs are welcome — please use the [issue tracker](https://github.com/Nikola1Davydov/AnalyzeTool/issues).
