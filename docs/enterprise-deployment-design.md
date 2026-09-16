# Enterprise Deployment — design (central configuration, policy, CLI)

Status: proposed direction, pre-implementation. Answers the question "how does a company
with hundreds of Revit seats roll AnalyseTool out and keep it configured?" Today the tool is
a single-seat product: every setting is written by the plugin itself into the user's profile,
and nothing outside that profile can shape it.

## Problem

Everything configurable lives in `%LOCALAPPDATA%\AnalyseTool\` and is written by the plugin:

| File | Owner | Holds |
| --- | --- | --- |
| `extensions.json` | `ExtensionSources` (Core) | user extension roots, authoring root |
| `extensions-state.json` | `ExtensionStateStore` (Core) | disabled extension ids |
| `codeexec.json` | `CodeExecutionSettings` (Core) | opt-in for ad-hoc C# execution |
| `catalog.json` | `ExtensionSourceCatalog` (Core) | company / user catalog entries |
| `mcp.json` | `McpServerController` (Mcp.Bridge) | MCP bridge on/off, port, token |
| `ai-providers.json` | `AiProviderRegistry` (Tools) | AI providers, DPAPI (CurrentUser) keys |

Consequences for an IT department:

- No place to put a setting **once for every seat**. Each user configures their own profile.
- No **lock**: a user can enable `ExecuteRevitCode` (full-trust code in the Revit process)
  with one click, or install any package via *Install from repository…*.
- `catalog.json` is a local file only; keeping it current on N machines is a file-copy job
  outside the tool.
- AI keys are DPAPI-per-user and cannot be provisioned centrally; there is no notion of a
  corporate gateway.
- Logs are local (`AppLog.cs`, daily rolling file). No central sink.
- Nothing can be installed, validated or diagnosed **without opening Revit**.

## What already exists (keep, build on)

- **Per-machine MSI.** `src/Installer/Program.cs` produces a `MultiUser` MSI
  (`InstallScope.perMachine`, `%ProgramData%\Autodesk\Revit\Addins\<year>`). It is silent-install
  ready (`msiexec /i … /qn`) for SCCM / Intune / GPO software distribution.
- **Managed zone** `extensions-dist` with `ExtensionInstaller.InstallPackage` and
  `ExtensionUpdateFeed` (`github:owner/repo` **or any https JSON feed** returning
  `{ version, downloadUrl }`). An internal feed needs static hosting only, not a service.
- **Company catalog** `catalog.json` — already documented in ONBOARDING.md as "the one to use
  for a company's internal extensions"; an entry with a shipped `id` replaces it.
- **Additional extension roots** (`ExtensionSources.AddRoot`) — a network share works today.
- **Headless Core.** All of the stores above are plain file-backed classes with no UI, which is
  what makes both the policy layer and a CLI cheap.

## Goals

1. An administrator configures the tool **once**, by distributing a file with the tools they
   already have (GPO Preferences, Intune, SCCM, a login script). No new server component is
   required for this.
2. Selected settings can be **locked**: the user sees them read-only and the commands that
   would change them refuse with a clear message.
3. The company's extension catalog and updates come from **the company's own hosting**.
4. Everything the policy layer does is **observable**: the effective configuration and its
   origin (machine / user / default) can be displayed in Settings and printed by a CLI.
5. Nothing changes for a single user with no policy file — the user layer alone behaves exactly
   as today.

## Non-goals (this iteration)

- A hosted AnalyseTool server, shared state between users, remote command execution. The
  architecture reserves the slot (a SignalR transport next to `Mcp.Bridge`), but no company
  requirement on the table needs it yet.
- ADMX / registry-based policy. Possible later as a second source of the machine layer, only if a
  customer requires it. The JSON file is the primary contract.
- Writing an AI gateway. Companies use an existing one (Azure OpenAI, LiteLLM, API Management);
  we only need to point at it.
- License gating (#72).

## Design

### 1. Two configuration layers

```
%ProgramData%\AnalyseTool\policy.json      ← machine layer, admin-owned, read-only for the plugin
%LOCALAPPDATA%\AnalyseTool\*.json          ← user layer, plugin-owned, exactly today's files
```

Resolution per setting: **machine value if present, else user value, else default.**
A machine value that is also listed under `locked` cannot be overridden by the user layer and
cannot be changed through the UI or any command.

`PathProvider` gets `MachineProfilePath` (`%ProgramData%\AnalyseTool`) and `PolicyPath`.
A new `Core/Common/Policy/PolicyStore` loads the file once, tolerates a missing or broken file
(logged + reported through `ExtensionDiagnostics`, never fatal) and exposes typed accessors plus
`Origin(settingName)` for the UI.

The plugin **never writes** to the machine layer. If the file is missing, the tool is exactly the
single-seat product it is today.

### 2. `policy.json` (v1)

```json
{
  "version": 1,
  "locked": ["codeExecution.enabled", "extensions.roots", "mcp.enabled"],

  "codeExecution": { "enabled": false },

  "extensions": {
    "roots": ["\\\\fileserver\\revit\\analysetool\\extensions"],
    "catalogUrl": "https://git.company.local/bim/analysetool-catalog/raw/main/catalog.json",
    "allowedFeeds": ["https://git.company.local/", "github:company-org/"],
    "required": [
      { "id": "company.standards", "source": "https://git.company.local/bim/standards/feed.json" }
    ],
    "allowInstallFromRepository": false
  },

  "mcp": { "enabled": false },

  "ai": {
    "providers": [
      { "id": "company-gateway", "name": "Company AI Gateway", "baseUrl": "https://ai.company.local/v1",
        "apiKeyEnv": "ANALYSETOOL_AI_KEY" }
    ],
    "allowUserProviders": false
  },

  "logging": { "sink": "\\\\fileserver\\logs\\analysetool\\{user}\\{date}.log", "level": "Information" }
}
```

Every section is optional. Unknown keys are ignored with a warning so a newer policy file works
on an older plugin.

Key semantics:

- `locked` — dotted setting paths. A locked path is read-only in the UI; the corresponding
  `Set…` command returns an error naming the policy file.
- `extensions.roots` — appended to the scan roots as **Dev zone, IsDefault=true** (not removable).
- `extensions.catalogUrl` — fetched at startup (cached with ETag under the user profile so an
  offline start still has the last copy) and merged **after** the shipped catalog and **before**
  the user's `catalog.json`; the user file can add but not remove company entries.
- `extensions.allowedFeeds` — prefix whitelist for `updateFeed` / catalog `source` / *Install from
  repository…*. Absent = everything allowed (today's behavior).
- `extensions.required` — installed on startup if missing, updated on *Check for updates*, cannot
  be disabled or removed by the user. Uses `ExtensionInstaller` + `ExtensionUpdateFeed` unchanged.
- `ai.providers[].apiKeyEnv` — the key is read from an environment variable (set by GPO / login
  script) instead of DPAPI storage. `baseUrl` may point at a gateway that holds the real key, in
  which case `apiKeyEnv` is omitted.
- `logging.sink` — an additional Serilog sink; the local rolling file stays.

### 3. Stores read through the policy

Each existing store gets the same small change: consult `PolicyStore` first, keep its own
user file as the fallback, and refuse writes for locked paths.

| Store | Change |
| --- | --- |
| `CodeExecutionSettings` | `Enabled` → policy value wins; `SetEnabled` refuses when locked |
| `ExtensionSources` | `AllRoots()` appends policy roots; `RemoveRoot` refuses for them; `AddRoot` refuses when `extensions.roots` is locked |
| `ExtensionStateStore` | `SetEnabled(false)` refuses for `required` ids |
| `ExtensionSourceCatalog` | third source: the remote catalog; entries carry `Origin = Policy` |
| `ExtensionUpdateFeed` / `InstallExtensionFromFile` / install-from-repository | enforce `allowedFeeds`, `allowInstallFromRepository` |
| `McpServerController` | `mcp.enabled` from policy when present |
| `AiProviderRegistry` (**Tools**) | see §4 |
| `AppLog` (App) | add the policy sink |

### 4. The Tools boundary

`AiProviderRegistry` lives in `AnalyseTool.Tools`, which references **only the Sdk** and must not
see `PolicyStore` in Core. Two options:

- **(a) Sdk contract** — `IRevitContext.Services` (or a new `IPolicy` on the context) exposes a
  read-only `GetPolicySection<T>(string name)`. Extensions get the same benefit: a third-party
  extension can read its own section from the company's `policy.json`. This is the right call
  and a deliberate Sdk minor version bump.
- (b) Tools re-reads `policy.json` itself. Rejected: two parsers, two sets of failure modes.

Go with (a). The generic accessor keeps the Sdk surface to one method.

### 5. Settings UI

- Locked settings render disabled with a lock icon and a tooltip "Managed by your organization
  (`%ProgramData%\AnalyseTool\policy.json`)".
- A new **Organization** panel: policy file found / not found, load errors, effective values
  with their origin, catalog URL and last fetch time. Read-only.
- Required extensions show a **Required** badge; the disable/remove actions are hidden.

### 6. CLI (`AnalyseTool.Cli`) — second entry point into Core

A console exe next to `AnalyseTool.Mcp.exe`, shipped by the same MSI (`PluginAssets.targets`
already copies the MCP exe; add the CLI the same way). References **Core + Sdk only**, in the
dependency table as another *caller* of Core alongside `Mcp.Bridge`. Core is headless and the
Revit API is compile-only, so everything below runs without Revit.

Commands (v1):

| Command | Does |
| --- | --- |
| `policy show [--json]` | effective configuration with origin per setting |
| `policy validate <file>` | schema + semantic checks (paths exist, feeds parse, ids well-formed); exit code for CI |
| `ext list` | installed extensions per zone, enabled state, incompatible-year notes |
| `ext install <zip> [--year 2025]` | `ExtensionInstaller.InstallPackage` from a deployment script |
| `ext validate <zip>` | `ExtensionPackage.Validate` — for a company's CI before publishing to its feed |
| `ext update [--id]` | resolve feeds, download, install |
| `diag collect [--out]` | zip of logs, versions, policy, extension list for support |

Explicitly **not** in the CLI: anything that touches a `Document`. That needs Revit and belongs
to the RevitTests tier or a future remote transport.

Boundary work: `Check-Boundaries.ps1` learns the new project; CLAUDE.md / AGENTS.md table gets a
row; `InternalsVisibleTo("AnalyseTool.Cli")` in Core, same pattern as `Mcp.Bridge`.

### 7. Rollout story for an administrator (what ONBOARDING.md § "For IT" will say)

1. Deploy `AnalyseTool-<ver>-MultiUser.msi` silently via SCCM / Intune.
2. Deploy `policy.json` to `%ProgramData%\AnalyseTool\` via GPO Preferences (Files) or the
   same package.
3. Host `catalog.json` and extension zips + feed JSON on any internal static hosting
   (GitLab raw, Nexus, IIS folder, file share). Publish new versions by replacing files.
4. Optional: set `ANALYSETOOL_AI_KEY` per user or point `baseUrl` at the company gateway.
5. Verify a seat with `AnalyseTool.Cli policy show` and `ext list`.

## Security notes

- Policy is trusted **because of where it is**: `%ProgramData%` is admin-writable only on a
  correctly configured machine. No signature in v1; document the assumption. Signing is a
  later addition if a customer asks.
- `allowedFeeds` and `allowInstallFromRepository=false` are the two settings that turn the tool
  from "installs what the user pastes" into "installs what IT approved". They are the reason
  the policy layer exists; ship them in phase 1, not later.
- `codeExecution.enabled=false` + locked should be the recommended enterprise default in the docs.

## Testing

Tier 1 (`AnalyseTool.Tests`), Revit-free:

- Policy parsing: missing file, broken JSON, unknown keys, each section alone.
- Layer resolution: machine wins, locked refuses writes, user-only behaves as today.
- Catalog merge order shipped → policy → user with id overrides.
- `allowedFeeds` matching (prefix, case, `github:` form).
- CLI: `policy validate` exit codes on good/bad files; `ext validate` on the Acme.Sample zip.

## Implementation phases / TODO

Phase 1 — policy layer (no UI, no CLI): the smallest change that makes the tool manageable.

- [ ] `PathProvider.MachineProfilePath`, `PolicyPath`
- [ ] `Core/Common/Policy/PolicyStore` + `PolicyDocument` model, load-once, diagnostics on error
- [ ] `CodeExecutionSettings` reads policy, refuses when locked
- [ ] `ExtensionSources` appends policy roots, honors lock
- [ ] `allowedFeeds` + `allowInstallFromRepository` enforced in feed resolution and install commands
- [ ] `McpServerController` honors `mcp.enabled`
- [ ] Tier-1 tests for parsing, resolution, feed whitelist
- [ ] `docs/policy.schema.json` (JSON Schema for editor completion and `policy validate`)

Phase 2 — catalog and required extensions.

- [ ] Remote `catalogUrl` with ETag cache, merge order shipped → policy → user
- [ ] `extensions.required`: install on startup, block disable/remove, update with the rest
- [ ] Tier-1 tests for merge order and required-id protection

Phase 3 — Sdk contract and Tools.

- [ ] Sdk: read-only policy section accessor on the context (minor version bump, CHANGELOG, ONBOARDING §Sdk)
- [ ] `AiProviderRegistry`: policy providers, `apiKeyEnv`, `allowUserProviders`
- [ ] `AppLog`: policy logging sink

Phase 4 — UI.

- [ ] Locked state rendering in Settings (disabled + lock icon + tooltip)
- [ ] **Organization** panel: policy status, effective values with origin
- [ ] **Required** badge; hide disable/remove for required extensions

Phase 5 — CLI.

- [ ] `AnalyseTool.Cli` project (Core + Sdk), `InternalsVisibleTo`, `Check-Boundaries.ps1`, CLAUDE.md / AGENTS.md table row
- [ ] `policy show`, `policy validate`
- [ ] `ext list`, `ext install`, `ext validate`, `ext update`
- [ ] `diag collect`
- [ ] Ship via `PluginAssets.targets` + MSI; tier-1 tests drive the exe like `McpExeTests`

Phase 6 — docs.

- [ ] ONBOARDING.md § "For IT administrators": MSI silent install, policy.json reference, hosting a catalog/feed, CLI
- [ ] LLM.md: one paragraph on reading a policy section from an extension
- [ ] CHANGELOG.md entry
