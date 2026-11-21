# AnalyseTool plugin for Claude Code and Codex

Connects the AI client to AnalyseTool in Revit — every open Revit version on one address,
`http://localhost:48075/mcp` — and brings three skills: `revit` (how to work with the model and with buttons),
`new-button` (a button from a description) and `model-tests` (checks of the model as C#: write, run, repair, accept). It ships with AnalyseTool, next to the add-in, and updates with it.

**Claude Code**

```
claude plugin marketplace add "<add-in folder>\Plugin"
claude plugin install analysetool@analysetool
```

**Codex**

```
codex plugin marketplace add "<add-in folder>\Plugin"
codex plugin add analysetool@analysetool
```

The token is never copied: the MCP server entry (`.mcp.json` for Claude Code, `codex.mcp.json` for Codex) runs a
headers helper that reads it from `%LOCALAPPDATA%\AnalyseTool\mcp.json`. The helper is `scripts/headers.ps1`, passed
to PowerShell as `-EncodedCommand` so it runs the same in any shell and from any folder; a test keeps both files in
step with the script.

AnalyseTool starts its MCP server the first time it is opened in a Revit session (the **Buttons** button), with AI
clients turned on in its profile. Privacy: everything stays on this computer — the server listens on `localhost`
only and refuses requests from web pages.
