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

- `AnalyseTool/`: C# Revit plugin logic.
- `clientapp/`: Vue 3 + Vite + PrimeVue frontend.
- `Installer/`: packaging and installation assets.

## Frontend Development

## Revit Bridge API (Frontend)

Bridge file: `clientapp/src/RevitBridge.ts`

Current command names:

- `SelectionInRevit`
- `IsolationInRevit`
- `GetCategoriesInRevit`
- `GetDataByCategoryName`
- `GetDocumentHealthStatus`
- `CheckUpdate`
- `GetDocumentData`
- `SetDataToParameters`
- `AiAnalyse`
- `AiEditParameters`
- `GetOllamaModels`

Basic usage:

```ts
import { Commands, sendRequest } from "@/RevitBridge";

await sendRequest(Commands.GetCategoriesInRevit, null);
await sendRequest(Commands.GetDataByCategoryName, { categoryName: "Walls" });
await sendRequest(Commands.SelectionInRevit, { elementIds: [123, 456] });
```

## Feedback

Issues and PRs are welcome.
