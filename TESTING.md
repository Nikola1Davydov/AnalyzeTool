# AnalyseTool — Beta Testing Guide (1.4.0-beta.1)

Thanks for testing! This beta turns AnalyseTool into an **extensible platform**: you can add your
own commands and UI, and connect it to an AI client over MCP. This guide covers install, what to
try, the optional/experimental features, known rough edges, and how to report problems.

## Requirements

- **Windows** + **Autodesk Revit 2025 or 2026**.
- (Optional) An AI client for MCP, e.g. **Claude Desktop**.
- (Optional) **Ollama** for local AI parameter analysis.

## Install

1. Download the latest **prerelease** MSI from the
   [Releases page](https://github.com/Nikola1Davydov/AnalyzeTool/releases) (look for `1.4.0-beta.1`).
2. **Close Revit**, then run the MSI. If you have an older AnalyseTool installed, uninstall it first
   (Windows → Apps) or let the installer replace it.
3. Start Revit → open a project → you'll see the **AnalyseTool** ribbon tab.

> The plugin installs per Revit version. If you run both 2025 and 2026, the installer covers both.

## What to try (and tell us what breaks)

**Core**
- [ ] Open the main AnalyseTool window — browse categories, parameters, the Parameter Value Check
      and Infinite Kanban pages.
- [ ] Edit parameters; try the AI analysis/edit (needs Ollama running locally).

**Settings** (AnalyseTool tab → **Settings**)
- [ ] **Environment** shows your Revit / SDK / plugin versions.
- [ ] **Extension paths** — add a folder, create the `extensions\<year>` structure, see status update.
- [ ] **Commands** — search the catalog; this is the list of everything callable.

**Extensions** — the headline feature
- [ ] **Script extension (easiest):** copy `samples/script-hello/` into
      `%LOCALAPPDATA%\AnalyseTool\extensions\<RevitYear>\sample.script.hello\`
      (`<RevitYear>` = `2025` or `2026`), then **Settings → Reload**. No build needed.
- [ ] **New template** — scaffold a UI / C# / Combo extension from Settings and Reload.
- [ ] Edit a script's `.cs` in your editor, **Reload**, confirm the change takes effect.

**MCP (AI integration)** — optional
- [ ] Settings → **MCP server** → pick a port → **Start**.
- [ ] Copy the generated **Claude Desktop config** snippet into your client's MCP config, restart the
      client. Your commands should appear as tools (e.g. `GetCategoriesInRevit`).

**C# code execution** — 🧪 experimental, **off by default**
- [ ] Settings → **C# code execution** → enable "Allow the AI to run C# code".
- [ ] With it on, the AI gets `ExecuteRevitCode` (run a snippet) and `SaveAsCommand` (turn a working
      snippet into a permanent ribbon button + command).
- ⚠️ **Safety:** this runs arbitrary C# in-process with full Revit API access. Only enable it if you
      trust your AI client. Leave it off if unsure — everything else works without it.

## Known limitations (beta)

- **New ribbon buttons need a Revit restart** the first time. Changing existing extensions/code only
  needs **Reload**.
- The AI's tool list is cached by the client — after toggling MCP or code execution, **restart the
  AI client** to refresh.
- C# code execution / Save-as-command are **new and lightly tested** — expect rough edges; report them.
- The loopback MCP bridge is **unauthenticated** — only meaningful on a trusted local machine.

## Reporting issues

Open an issue at **https://github.com/Nikola1Davydov/AnalyzeTool/issues** with:

- AnalyseTool version (`1.4.0-beta.1`) and **Revit version** (2025 / 2026).
- What you did, what you expected, what happened.
- Steps to reproduce (and a sample model if relevant).
- For extension/MCP problems: the extension's `plugin.json`, and any compile error shown in Settings.
- Screenshots of any error dialog.

Thank you! 🙏
