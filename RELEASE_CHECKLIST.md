# Release checklist

Run it before publishing a version. **Part 1** an agent can do in the repository; **Part 2** needs a person, Revit and
the release itself.

---

## Part 1 — Repository and build

### Version and docs

- [ ] **The version is in the code**: `"Build"` › `"Version"` of `build/appsettings.json` (local builds, CI and the
      installers all carry it). Raise it above the last release (`git tag --sort=-creatordate | head`): MSI keeps only
      X.Y.Z, and a version that is tagged already is not released again. Left empty, GitVersion takes it from the tags.
- [ ] **CHANGELOG.md** has `## [X.Y.Z] / <YYYY-MM-DD>` (not «unreleased») with the changes a user notices, one or two
      sentences each, and what is not there yet. The About page shows it.
- [ ] **The guide for authors** — `source/AnalyseTool.Extensions/Authoring/LLM.md` (ships next to the add-in, served by
      `GetAuthoringGuide`) and `ONBOARDING.md` (Wiki home, NuGet readme of the Sdk) — says what changed for button
      authors: the folder layout, the Sdk, MCP tools, transactions.
- [ ] **The plugin for Claude Code and Codex** (`source/AnalyseTool/Plugin/`): `version` in both manifests
      (`.claude-plugin/plugin.json`, `.codex-plugin/plugin.json`) is the release version; `claude plugin validate
      source/AnalyseTool/Plugin` and `claude plugin validate source/AnalyseTool/Plugin/analysetool` pass;
      `AiPluginTests` keep the encoded headers helper in step with `scripts/headers.ps1`.
- [ ] **The Sdk**: if `source/AnalyseTool.Sdk/**` changed, its `<Version>`/`FileVersion` moved (minor for additions,
      major — and `AssemblyVersion` — for a break). The version `LLM.md` §4.3 and `ONBOARDING.md` §4.1 pin must exist on
      NuGet.
- [ ] **README**: badges, supported Revit versions, the quick start, the plugin commands are current.

### Build

- [ ] `dotnet run -c Release --project build` is green: project boundaries, ClientApp, R25/R26/R27, the Revit-free tests
      (`AnalyseTool.Tests`, `AnalyseTool.Hosting.Tests`) and the revit-bridge tests.
- [ ] `dotnet run -c Release --project build -- pack` writes `output/AnalyseTool-<version>-SingleUser.msi`,
      `-MultiUser.msi` and `AnalyseTool.bundle.zip`.
- [ ] The MSI (read with Python `msilib`, or Orca):
  - `UpgradeCode` is `{E74A2DBC-8131-4240-ABDE-E2A776451BBA}` — the one of 1.x, so installing removes an installed 1.x
    of the same kind;
  - `ProductVersion` is the release version;
  - files: `.addin` for 2025/2026/2027, `Plugin\` (both manifests, `.mcp.json`, `codex.mcp.json`, skills,
    `scripts\headers.ps1`), `Ribbon\analysetool\`, `LLM.md`, `CHANGELOG.md`; libgit2 for Windows only; no
    `Ribbon\analysetool.dev\` (Debug only).

---

## Part 2 — In Revit and publishing (a person)

### In Revit

- [ ] `dotnet test --project tests/AnalyseTool.RevitTests/AnalyseTool.RevitTests.csproj -c Release.R25` (and R26, R27)
      from your own terminal — the tests start Revit; from an agent's shell Revit does not come up.
- [ ] **2025** (then 2026, 2027 briefly): Buttons opens the pane; New folder, New button — it runs; edit its C# and save —
      the new code runs; a compile error shows «!» with file and line; save, restore and pin a version.
- [ ] Edit mode: drag between sections, into another folder, to the trash (a button and a folder); −/+, ☆, ✎, 📁.
- [ ] A page with `"dockable": true` opens inside the pane, another in a window; a command with an input type gets a form.
- [ ] A DLL project shows as a box with its years; Older buttons › ✎ › Move to… moves a button into a tab (on a copy).
- [ ] Parameters: the table, binding, the report.
- [ ] MCP: the plugin installs (`claude plugin …`, `codex plugin …`) and connects; `ListRevitVersions`, a read-only tool,
      `RunCommand`; with two Revit versions open, `"revit": "2026"` reaches the other one.
- [ ] Anything that writes to the model is tried on a **copy** of a real project.

### Installers and publishing

- [ ] SingleUser and MultiUser install on a clean user/machine; the WebView2 prompt works without the runtime.
- [ ] Installed **over 1.x** of the same kind: 1.x is gone (its folder and `AnalyseTool.addin`), 2.0 loads, the
      extensions in `%LOCALAPPDATA%\AnalyseTool\extensions\` are still there.
- [ ] CHANGELOG dated (`## [X.Y.Z] / <date>`), then `rewrite` (or `dev`) merged into `main`. No tag by hand:
      run `.github/workflows/Release.yml` by hand (Actions › Release › Run workflow, on `main`); when the version of
      `build/appsettings.json` has no tag yet, it builds, tests, packs, makes the tag `X.Y.Z` (no «v», as in 1.x) and the release with both
      MSIs, the bundle and the changelog of the version. An undated or missing changelog section stops it. A suffix
      (`2.1.0-beta.1`) makes a pre-release. Check the run in Actions and the files on the release.
- [ ] **The Sdk package, only if its contract changed**: the release does not push it. Bump its version, take the
      `.nupkg` of a green build, `dotnet nuget push <pkg>.nupkg -s https://api.nuget.org/v3/index.json -k <key>`, then
      move the pinned version in `LLM.md` and `ONBOARDING.md`.
- [ ] The Wiki follows `ONBOARDING.md` (`wiki-sync.yml`).
