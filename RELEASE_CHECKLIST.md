# Release checklist

A reusable checklist to run before publishing a new AnalyseTool version.
**Part 1** can be verified in the repo/build (an agent can run it). **Part 2** needs a human
(live Revit, publishing, tagging).

---

## Part 1 — Repo & build (automatable)

### Versions & docs

- [ ] **Version bumped** — `PLUGIN_VERSION` in `src/SharedData/ToolData.cs` is **higher** than the last
      published/tagged version (`git tag --sort=-creatordate`). Never re-publish an equal or lower version.
- [ ] **Changelog complete** — `CHANGELOG.md` has a `## [<version>] / <YYYY-MM-DD>` section that lists
      **all** notable changes of this release. Bullets start with `- ` (the release pipeline only keeps
      lines starting with `- ` under the matching `## [<version>]` header).
- [ ] **Extension-authoring changes documented** — if anything changed that matters to extension authors
      (SDK contract, manifest fields, bridge, build configs, new capabilities), it is reflected in **both**
      `src/ONBOARDING.md` **and** `src/LLM.md`.
- [ ] **SDK version bumped if the SDK changed** — if `src/AnalyseTool.Sdk/**` changed, bump the SDK
      version in `AnalyseTool.Sdk.csproj`: - additive/backward-compatible → bump **minor** (`Version` + `FileVersion`); keep `AssemblyVersion`
      stable within the major (it's the binding identity — moving it breaks older extensions). - breaking contract change → bump **major** (and `AssemblyVersion` major); old extensions are then
      rejected at load with a clear message.
- [ ] **Docs consistent** — supported Revit versions and the SDK `Description` agree everywhere
      (README badge + Compatibility, `AnalyseTool.Sdk.csproj` Description, `AnalyseTool.Sdk.props`,
      `ONBOARDING.md`, `LLM.md`). Note the TFM split: net8.0-windows for R25/R26, net10.0-windows for R27.
- [ ] **No dead code / debug junk** — no unused exports/commands left behind by a refactor, no stray
      `console.log` / `OpenDevToolsWindow` / commented-out blocks / temporary files.

### Builds

- [ ] **Host builds** — Release **R25 / R26 / R27**:
      `dotnet build src/AnalyseTool/AnalyseTool.csproj -c "Release R25"` (and `R26`, `R27`) → 0 errors.
- [ ] **clientapp builds** — `cd src/clientapp && npm run build` → `✓ built`.
- [ ] **SDK packs** — `dotnet pack src/AnalyseTool.Sdk/AnalyseTool.Sdk.csproj -c "Release R25"` → nupkg
      with the intended version, no warnings.
- [ ] **Nuke build project compiles** — `dotnet build src/build/_build.csproj` → 0 errors (guards the
      release pipeline itself).

---

## Part 2 — Publish & verify (human only)

### Live in Revit (can't be verified from the repo)

- [ ] Plugin installs and the **AnalyseTool** ribbon tab appears (all three buttons).
- [ ] Smoke-test the features that changed this release (open windows, run the new/changed commands on a
      **copy** of a real project — especially any model-write action: delete / purge / load / rename).
- [ ] Long operations show progress and don't freeze Revit; closing a window mid-operation doesn't crash.

### Artifacts & distribution

- [ ] Release pipeline is green; the GitHub release was created with the changelog body.
- [ ] Both installers (**SingleUser** + **MultiUser**) install on a clean machine; WebView2-missing prompt
      works when the runtime is absent.
- [ ] SDK published to nuget.org at the new version (needs the `NUGET_API_KEY` repo secret) — otherwise it
      is only attached to the GitHub release.
- [ ] If releasing from `main`, `dev` is merged into `main` first.
