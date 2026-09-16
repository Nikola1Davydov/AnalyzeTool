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
6. A user who installed the plugin themselves (no admin rights, SingleUser MSI, a contractor's
   laptop) can **join the company's configuration in one action** from Settings — and leave it
   again.

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

### 1. Three configuration layers

```
%ProgramData%\AnalyseTool\policy.json      ← machine layer, admin-owned, read-only for the plugin
%LOCALAPPDATA%\AnalyseTool\org.json         ← organization layer: a policy the USER joined by URL (§8)
%LOCALAPPDATA%\AnalyseTool\*.json           ← user layer, plugin-owned, exactly today's files
```

Resolution per setting: **machine value if present, else organization value, else user value,
else default.** A value that is also listed under `locked` (in whichever layer supplies it)
cannot be overridden by the layers below it and cannot be changed through the UI or any command.

| Layer | Put in place by | Needs admin | User can leave |
| --- | --- | --- | --- |
| Machine | IT, via GPO / Intune / SCCM | yes | no |
| Organization | the user, via **Join organization** | no | yes (**Leave organization**) |
| User | the plugin | no | these ARE the user's settings |

The machine and organization layers carry the **same file format**. IT publishes one
`policy.json`; strict shops push it to `%ProgramData%`, everyone else (self-installed seats,
contractors, freelancers on a project) joins it by URL. Both can coexist: the machine layer wins.

`PathProvider` gets `MachineProfilePath` (`%ProgramData%\AnalyseTool`) and `PolicyPath`.
A new `Core/Common/Policy/PolicyStore` loads both policy sources once, tolerates a missing or
broken file (logged + reported through `ExtensionDiagnostics`, never fatal), merges them and
exposes typed accessors plus `Origin(settingName)` for the UI.

The plugin **never writes** to the machine layer. If neither policy source exists, the tool is
exactly the single-seat product it is today.

### 2. `policy.json` (v1)

```json
{
  "version": 1,
  "organization": { "name": "Company BIM", "contact": "bim-support@company.local" },
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

- `organization` — shown in Settings ("Managed by Company BIM") and in the join preview (§8), so
  a user always knows whose configuration they are running and whom to ask.
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
| `ExtensionSources` | `AllRoots()` appends policy roots and the machine-level `extensions-dist`; `RemoveRoot` refuses for them; `AddRoot` refuses when `extensions.roots` is locked |
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

Nothing here is AnalyseTool-specific machinery: the plugin reads one file from
`%ProgramData%\AnalyseTool\` at startup, and Windows does the delivery.

**Active Directory / GPO**

1. **Plugin.** Put `AnalyseTool-<ver>-MultiUser.msi` on a share readable by domain computers.
   In the GPO linked to the BIM workstations OU: *Computer Configuration → Policies → Software
   Settings → Software Installation → New Package*, deployment **Assigned**. The MSI installs as
   SYSTEM at the next boot, before anyone logs in. New versions go in via the package's
   *Upgrades* tab (the MSI has `MajorUpgrade` configured).
2. **Policy.** Same GPO: *Computer Configuration → Preferences → Windows Settings → Files → New
   File*. Action **Replace**, source `\\fileserver\deploy\AnalyseTool\policy.json`, destination
   `%ProgramData%\AnalyseTool\policy.json`. GPO re-applies at boot and every ~90 minutes, so an
   edited file on the share reaches every seat within that window; the plugin picks it up on the
   next Revit start.
3. **Pre-installed extensions (no feed needed).** *Preferences → Folders/Files* can also drop
   ready extension folders into `%ProgramData%\AnalyseTool\extensions-dist\<id>\`. The plugin
   scans that machine-level managed root in addition to the user one (read-only for the Extension
   Manager: no install/remove/update there, listed with a **Machine** badge).
4. **AI key.** *Preferences → Windows Settings → Environment* sets `ANALYSETOOL_AI_KEY` — per
   user (User Configuration) or per machine when the key belongs to a gateway.
5. **Verify** a seat with `AnalyseTool.Cli policy show` and `ext list`.

A startup PowerShell script that copies the file is an equivalent alternative to Preferences.

**Intune / Azure AD only**

Same shape, different tooling: the MSI wrapped as a Win32 app (`msiexec /i … /qn`), `policy.json`
delivered by an Intune PowerShell script or a second Win32 app. Everything the plugin does is
identical.

**Hosting for catalog and feeds.** Any internal static hosting: GitLab raw, Nexus, an IIS folder,
a file share. Publish a new extension version by replacing files. No service to run.

**Later, on request:** an ADMX template reading `HKLM\Software\Policies\AnalyseTool` as a second
source of the machine layer, for administrators who want checkboxes in the Group Policy Editor
instead of a JSON file.

### 8. Join organization — one action to adopt the company configuration

The scenario: a user installs the plugin themselves, opens Settings and connects to the company's
configuration without IT touching their machine.

**User flow**

1. Settings → **Organization** → **Join organization…**
2. The user enters a policy URL **or** the company domain **or** nothing when the plugin already
   discovered a policy (below).
3. The plugin downloads `policy.json` and shows a **preview** before applying anything:
   organization name and contact, which settings change, which become locked, which extensions
   will be installed and from where, where AI requests will go, where logs will go.
4. The user confirms. The policy is applied: required extensions install, the catalog is
   fetched, the locks take effect. The panel now reads "Managed by <name> — updated <time>" with
   a **Leave organization** button.

**Discovery (so the user types as little as possible)**

| Input | Resolution |
| --- | --- |
| full URL | fetched as is |
| domain (`company.local`) | 1. DNS TXT `_analysetool.company.local` → URL; 2. `https://company.local/.well-known/analysetool/policy.json` |
| nothing | same two lookups against `USERDNSDOMAIN` (domain-joined machines) |

On first start, if discovery against `USERDNSDOMAIN` finds a policy, Settings shows a
non-modal banner "Your organization publishes AnalyseTool settings. Join?". Nothing is applied
without the user's confirmation — discovery only offers.

**Persistence and refresh**

`%LOCALAPPDATA%\AnalyseTool\org.json`:

```json
{ "policyUrl": "https://…/policy.json", "etag": "\"…\"", "fetchedAt": "2026-09-16T08:00:00Z",
  "organizationName": "Company BIM", "cachedPolicy": { … } }
```

On every start the plugin re-fetches with `If-None-Match`; unchanged → nothing to do, changed →
re-apply and note it in the panel, offline → the cached policy stays in force. A fetch failure is a
diagnostic, never a reason to drop the configuration.

**Leave organization** deletes `org.json`, releases the locks and offers (not forces) to remove
the extensions that were installed because of `extensions.required`. User-layer files are untouched,
so the user gets their own pre-join settings back.

**Trust**

- HTTPS only. A policy can install code and redirect AI traffic; a plain-http or
  file-share URL is refused for the organization layer (the machine layer is trusted by location,
  §Security notes).
- The preview is the consent step and must name the consequential items explicitly:
  "Installs extensions: …", "Sends AI requests to: …", "Locks: …".
- `organization.name` and `contact` are required for a joinable policy so the UI never says
  "managed by unknown".
- Later, optional: a publisher signature over the file plus a fingerprint IT hands to staff,
  shown in the preview. v1 relies on HTTPS + preview.

**Implementation shape**

- `PolicyStore` gains a second loader (`OrgPolicySource`) beside the file loader; merge order is
  the only place that knows there are two. No store class changes for this feature.
- Core commands: `DiscoverOrganizationPolicy(input?)` → preview model, `JoinOrganization(url)`,
  `LeaveOrganization()`, `GetOrganizationStatus()`.
- App: the Organization panel from §5 grows the join/leave controls and the first-start banner.
- CLI: `org join <url|domain>`, `org leave`, `org status` — the same commands for scripted seats.

## Security notes

- Policy is trusted **because of where it is**: `%ProgramData%` is admin-writable only on a
  correctly configured machine. No signature in v1; document the assumption. Signing is a
  later addition if a customer asks.
- `allowedFeeds` and `allowInstallFromRepository=false` are the two settings that turn the tool
  from "installs what the user pastes" into "installs what IT approved". They are the reason
  the policy layer exists; ship them in phase 1, not later.
- `codeExecution.enabled=false` + locked should be the recommended enterprise default in the docs.
- The organization layer is trusted **because the user consented** to a specific HTTPS origin after
  a preview — not by location. A policy fetched from a URL is never applied silently, and a URL
  change (redirect to another host) invalidates the join and asks again.

## Testing

Tier 1 (`AnalyseTool.Tests`), Revit-free:

- Policy parsing: missing file, broken JSON, unknown keys, each section alone.
- Layer resolution: machine wins, locked refuses writes, user-only behaves as today.
- Catalog merge order shipped → policy → user with id overrides.
- `allowedFeeds` matching (prefix, case, `github:` form).
- Three-layer resolution: machine over organization over user; locks honored per origin.
- Discovery input parsing: URL vs domain vs empty; http/file URLs refused for the org layer.
- `org.json` refresh: ETag unchanged, changed, fetch failure keeps the cache; Leave removes locks.
- CLI: `policy validate` exit codes on good/bad files; `ext validate` on the Acme.Sample zip.

## Implementation phases / TODO

Phase 1 — policy layer (no UI, no CLI): the smallest change that makes the tool manageable.

- [ ] `PathProvider.MachineProfilePath`, `PolicyPath`
- [ ] `Core/Common/Policy/PolicyStore` + `PolicyDocument` model, load-once, diagnostics on error
- [ ] `CodeExecutionSettings` reads policy, refuses when locked
- [ ] `ExtensionSources` appends policy roots, honors lock
- [ ] Machine-level managed root `%ProgramData%\AnalyseTool\extensions-dist` scanned read-only (Machine badge; no install/remove/update there)
- [ ] `allowedFeeds` + `allowInstallFromRepository` enforced in feed resolution and install commands
- [ ] `McpServerController` honors `mcp.enabled`
- [ ] Tier-1 tests for parsing, resolution, feed whitelist
- [ ] `docs/policy.schema.json` (JSON Schema for editor completion and `policy validate`)

Phase 2 — catalog and required extensions.

- [ ] Remote `catalogUrl` with ETag cache, merge order shipped → policy → user
- [ ] `extensions.required`: install on startup, block disable/remove, update with the rest
- [ ] Tier-1 tests for merge order and required-id protection

Phase 3 — Join organization (the organization layer).

- [ ] `org.json` model + `OrgPolicySource` loader in `PolicyStore`; three-layer merge with `Origin`
- [ ] Discovery: URL / domain / `USERDNSDOMAIN`; DNS TXT `_analysetool.<domain>` and `/.well-known/analysetool/policy.json`
- [ ] Commands `DiscoverOrganizationPolicy`, `JoinOrganization`, `LeaveOrganization`, `GetOrganizationStatus`
- [ ] Preview model listing changes, locks, extensions to install, AI endpoint, log sink
- [ ] Startup refresh with `If-None-Match`; offline keeps cache; host change invalidates the join
- [ ] `organization.name` / `contact` required for a joinable policy; HTTPS-only enforcement
- [ ] Tier-1 tests: resolution order, input parsing, refresh cases, leave semantics

Phase 4 — Sdk contract and Tools.

- [ ] Sdk: read-only policy section accessor on the context (minor version bump, CHANGELOG, ONBOARDING §Sdk)
- [ ] `AiProviderRegistry`: policy providers, `apiKeyEnv`, `allowUserProviders`
- [ ] `AppLog`: policy logging sink

Phase 5 — UI.

- [ ] Locked state rendering in Settings (disabled + lock icon + tooltip)
- [ ] **Organization** panel: policy status, effective values with origin
- [ ] **Required** badge; hide disable/remove for required extensions
- [ ] Organization panel: Join organization… dialog with preview, Leave organization, "Managed by <name> — updated <time>"
- [ ] First-start banner when discovery finds a policy (offer only, never auto-apply)

Phase 6 — CLI.

- [ ] `AnalyseTool.Cli` project (Core + Sdk), `InternalsVisibleTo`, `Check-Boundaries.ps1`, CLAUDE.md / AGENTS.md table row
- [ ] `policy show`, `policy validate`
- [ ] `ext list`, `ext install`, `ext validate`, `ext update`
- [ ] `org join <url|domain>`, `org leave`, `org status`
- [ ] `diag collect`
- [ ] Ship via `PluginAssets.targets` + MSI; tier-1 tests drive the exe like `McpExeTests`

Phase 7 — docs.

- [ ] ONBOARDING.md § "For IT administrators": GPO step-by-step (Software Installation + Preferences → Files), Intune variant, policy.json reference, hosting a catalog/feed, publishing for Join (DNS TXT / well-known URL), CLI
- [ ] ONBOARDING.md § "Joining your company's configuration" for end users
- [ ] LLM.md: one paragraph on reading a policy section from an extension
- [ ] CHANGELOG.md entry
