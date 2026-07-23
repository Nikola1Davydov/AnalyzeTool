# Extension Platform — design (foundation for the Extension Manager)

Status: agreed direction, pre-implementation. Covers issues #64 (Extension Manager),
#48 (third-party distribution) and the packaging/publishing pipeline. Licensing
gating (#72) is explicitly out of scope for this iteration.

## Goals

1. Ship the **contracts** (folder layout, package format, manifest v2) with the big
   restructuring release, before third-party extensions exist in the wild.
2. Keep the hobbyist/AI-authoring path ("write a tool for myself with an AI") as
   frictionless as today — the packaging world must be opt-in.
3. Publishing must be one command locally (`dotnet build -t:PackExtension`) or one
   git tag in CI. No marketplace, no hosted binaries — AnalyseTool is runtime +
   delivery mechanics only (#48).

## Two zones with different semantics

| | Installed (managed) | Dev / Local (unmanaged) |
| --- | --- | --- |
| Location | `%LOCALAPPDATA%\AnalyseTool\extensions-dist\` (new folder — clean manager invariants) | `extensions\` (the historical folder, behavior unchanged) + user-added roots (existing `ExtensionSources` feature) |
| Layout | Package format only (below) | Loose folder: `plugin.json` + `.cs` / DLL / `ui` next to it, no year folders |
| Owned by | Extension Manager (install / remove / update / consent) | The author; live Reload as today |
| Manager UI | Installed tab: enable/disable, remove, update badge, diagnostics | Same list with a **Dev** badge: enable/disable, diagnostics, open folder — no install/update semantics |

The legacy layout `extensions\<year>\<id>\` stays readable (deprecated) so existing
users lose nothing. `CreateExtensionTemplate` and LLM.md target the dev zone only.

## Package format (the distribution contract)

One zip = one extension for all Revit versions. Contents = the extension folder:

```
MyExt/
  plugin.json        # one manifest for all versions
  icon.png
  ui/                # web part, version-independent
  2025/MyExt.dll     # per-year binaries (R25/26 = net8, R27 = net10)
  2026/MyExt.dll
  2027/MyExt.dll
```

Resolution rules (host running year Y):
1. `entryAssembly` is looked up in `<Y>/` first, then in the folder root.
2. Scripts (`*.cs`) and `ui/` always come from the root.
3. Neither found for Y → the extension is listed as **incompatible with Revit Y**
   in the manager, and is not loaded (no silent invisibility).

Script-only and UI-only extensions have no year folders at all.

## Manifest v2 (additive — old manifests keep working)

New optional fields: `publisher`, `description`, `website`, `supportUrl`, `icon`,
`updateFeed`. No `targetRevit` (the year folders are the declaration), no
license fields (#72 later).

## Per-extension state

`enabled/disabled` persists host-side in `%LOCALAPPDATA%\AnalyseTool\extensions-state.json`
(never in the vendor's manifest). Consulted by `ExtensionLoader.LoadAll` and the
ribbon builder. Applies to both zones. Commands: `EnableExtension`, `DisableExtension`.

## Install / remove / update

- `InstallExtensionFromFile` (zip → validate manifest → third-party consent dialog,
  consent logged (#48) → unpack into the managed zone → Reload).
- `RemoveExtension` (delete folder + script cache → Reload).
- `CheckExtensionUpdates`: polls each installed extension's `updateFeed`.
  Two feed forms:
  - any HTTPS JSON returning `{version, downloadUrl}`;
  - shortcut `"updateFeed": "github:owner/repo"` → GitHub releases/latest, zip
    asset as download (same pattern as the host's own `CheckUpdate`).
  Update = download from the vendor's URL → same validation as install → swap
  folder → Reload. We never host third-party binaries.

## Publishing pipeline (vendor & novice CI/CD)

1. **`PackExtension` MSBuild target shipped inside the AnalyseTool.Sdk NuGet**
   (`build/AnalyseTool.Sdk.targets`, auto-imported like the existing props).
   `dotnet build -t:PackExtension` builds all supported `Release R*` configurations,
   lays out the bundle (per-year DLLs + shared root files), validates it (manifest
   parses, id matches folder, entryAssembly present per year — clear textual errors
   an AI assistant can self-correct from) and zips to `artifacts/<id>-<version>.zip`.
2. **Workflow template**: `CreateExtensionTemplate` + docs emit
   `.github/workflows/release.yml` — on tag `v*`: setup .NET 8+10, run
   `PackExtension`, attach the zip to a GitHub Release. Novice publishing path:
   `git tag v1.0.0 && git push --tags`.
3. `updateFeed: github:owner/repo` then gives that author updates for free.
4. CI dogfooding: extend the existing Acme.Sample external-author job with
   `PackExtension` so the pipeline is tested on every commit.

## Docs to update in the same release

`ONBOARDING.md` (zones, package format, publishing), `src/LLM.md` + the embedded
copy in `CreateExtensionTemplate` (dev-zone authoring stays step one; §publish added).

## Template generator: two modes

`CreateExtensionTemplate` grows a second mode so the dev zone doesn't become a
trap on the way to publishing:

1. **For myself** (today's behavior): scaffold into the dev zone, no git.
2. **Project**: the user picks ANY folder (Documents, D:\projects, …). The
   generator scaffolds the full project there — `plugin.json`, csproj referencing
   the Sdk, `.gitignore`, the `release.yml` publishing workflow, LLM.md/README
   with the three commands to connect an empty GitHub repo — runs `git init` +
   initial commit when git is available, and **auto-registers the folder as a dev
   root** via the existing `ExtensionSources.AddRoot`, so the extension is live in
   Revit immediately while living in a proper project location. No clone step:
   the GitHub repo is created empty and receives the local history via push.

   The drawer additionally takes an optional **Repository URL**: when set, the
   generator also runs `git remote add origin <url>`, so the only remaining step
   is `git push -u origin main` (pre-written in the generated README/LLM.md).
   Guardrails:
   - auto-push is a separate opt-in checkbox, not the default — credential
     prompts (GCM browser flow) from inside the Revit process are fragile, and a
     failed push must produce the exact command to run manually;
   - the drawer hints "create the GitHub repo EMPTY (no README/.gitignore)" —
     otherwise the first push is rejected as non-fast-forward;
   - missing git / unset user.name degrades gracefully: full scaffold is still
     written, git steps are skipped and reported, README carries the full
     command sequence.

3. **Promote to project**: a manager action on any dev-zone extension, for the
   "started for myself, now I want to publish" lifecycle. Pick a target folder
   (+ optional repository URL) → the host MOVES the extension there, scaffolds
   whatever is missing (csproj, `.gitignore`, `release.yml`, README), runs the
   same git steps as project mode, registers the new folder as a dev root and
   removes the old dev-zone copy. The extension keeps working throughout.
   Lifecycle: script for myself → (one click) → git project → (push + tag) →
   published package; every step is a valid place to stop.

## Registry (the curation point, later iteration)

A separate repo (`AnalyseTool-registry`) holding one JSON list of entries
`{id, name, publisher, description, repo, updateFeed}` — metadata and links only,
binaries stay with vendors (#48). The manager fetches it (raw.githubusercontent)
and renders the **Available** tab; Install downloads from the vendor's release.

Submission flow:
1. Vendor publishes a release, then submits via an issue form in the registry
   repo (one field: repo URL). The manager can open that prefilled URL — a
   "Submit to catalog" action.
2. A GitHub Action in the registry repo validates the submission (release
   exists, zip valid, manifest parses, id free) and opens a PR with the entry.
3. **Merging the PR is the curation act** — listing appears for all users;
   delisting = removing the entry. Git history = moderation history.
4. Listing is explicitly NOT endorsement ("third-party, not reviewed") — the
   install consent dialog stays.

Updates never go through the registry (manager polls the vendor's `updateFeed`
directly), so a stale registry breaks nothing.

### Popularity signals (no-server model)

- **Downloads**: GitHub already counts release-asset downloads (`download_count`
  per asset) — free for every `github:`-hosted extension. Custom HTTPS feeds may
  report an optional `downloads` number; absent otherwise.
- **Votes**: the vendor repo's GitHub stars. Real ratings/reviews would require
  accounts, moderation and anti-abuse — the marketplace infrastructure #48
  rejects. "Vote" in the manager = open the repo page (also drives vendor
  visibility). Known limit: stars are per-repo, not per-extension.
- **Aggregation**: clients must NOT poll the GitHub API themselves (60 req/h
  anonymous limit). A scheduled Action in the registry repo refreshes
  stars/downloads for all entries (its token has ample limits) and commits them
  into `registry.json` — clients still fetch one static file; the git history
  doubles as a popularity timeline. Available tab sorts by either number.

## Out of scope (deliberately)

- License gating / `ILicenseProvider` (#72) — separate SDK-contract release.
- The registry itself ships AFTER the foundation (needs install-from-url +
  manifest v2 first); the design above is the agreed target.
- Multi-button manifest (`ui.buttons[]`), package signatures.
- No payment processing, review/approval of code, or hosted binaries — ever (#48).

## Implementation phases

1. New folder layout + resolution in `ExtensionCatalog`/`ExtensionLoader`
   (+ legacy layout kept working) + dev/managed zone split in `ExtensionSources`.
2. Manifest v2 fields + `extensions-state.json` + enable/disable commands.
3. Package validation module shared by pack & install; `InstallExtensionFromFile`,
   `RemoveExtension`.
4. `PackExtension` target in the Sdk package + workflow template + Acme.Sample CI.
5. `CheckExtensionUpdates` + feeds.
6. Manager UI in `ExtensionsSettingsView` (Installed/Dev, actions, badges).
7. ONBOARDING.md + LLM.md sync.
