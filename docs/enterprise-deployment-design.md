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
6. **IT is needed once, not for every change.** Day-to-day configuration (settings, locks,
   catalog, required extensions, AI endpoint) is owned by the BIM coordinator and changes without
   an IT ticket. IT's part is the MSI and, optionally, a one-time pointer.
7. A user who installed the plugin themselves (no admin rights, SingleUser MSI, a contractor's
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

Resolution per setting follows the Group Policy model of **mandatory vs. default**: a policy value
listed under `locked` is mandatory — it wins over the user's own choice and the UI shows it
read-only; a policy value that is *not* locked is a default — it applies until the user makes
their own choice, which then wins. Between the two policy layers the machine layer wins. In
order: locked machine → locked organization → user → machine default → organization default →
built-in default. (Phase 1 implements this in `PolicyState.Resolve`.)

| Layer | Put in place by | Needs admin | User can leave |
| --- | --- | --- | --- |
| Machine | IT, via GPO / Intune / SCCM | yes | no |
| Organization | the user, via **Join organization** | no | yes (**Leave organization**) |
| User | the plugin | no | these ARE the user's settings |

The machine and organization layers carry the **same file format**, and the machine file has two
forms:

- **Pointer (recommended).** `%ProgramData%\AnalyseTool\policy.json` contains only
  `{ "version": 1, "policyUrl": "https://…/policy.json", "enforced": true }`. The plugin loads the
  real policy from the URL through the same loader as Join organization (§8), and `enforced`
  means the user cannot leave. IT writes this file once; **the content at the URL is owned by the
  BIM coordinator** and changes with a commit or a file replace, never an IT ticket.
- **Inline.** The full policy in the machine file, for shops where IT wants to own every value.
  Every change then travels via GPO.

Self-installed seats, contractors and freelancers join the same URL by hand (§8). All layers can
coexist: the machine layer wins.

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
  "minimumVersion": "2.3.0",
  "update": { "downloadUrl": "https://git.company.local/bim/analysetool/AnalyseTool-2.3.0-SingleUser.msi",
              "sha256": "…" },
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

  "sharepoint": { "syncUrl": "odopen://sync/?siteId=…&webId=…&listId=…&webUrl=…&listTitle=BIM%20Tools" },

  "ai": {
    "providers": [
      { "id": "company-gateway", "name": "Company AI Gateway", "baseUrl": "https://ai.company.local/v1",
        "apiKeyEnv": "ANALYSETOOL_AI_KEY" }
    ],
    "allowUserProviders": false
  },

  "logging": { "sink": "\\\\fileserver\\logs\\analysetool\\{user}\\{date}.log", "level": "Information" },

  "telemetry": { "sink": "https://otel.company.local/v1/logs", "identity": "hashed",
                 "events": ["inventory", "command", "ai"] }
}
```

Every section is optional. Unknown keys are ignored with a warning so a newer policy file works
on an older plugin.

Key semantics:

- `organization` — shown in Settings ("Managed by Company BIM") and in the join preview (§8), so
  a user always knows whose configuration they are running and whom to ask.
- `minimumVersion` / `update` — a seat below the version shows a non-blocking banner with the
  download link. On a **per-user** install the plugin may offer to run the MSI itself after Revit
  closes (same host as the policy, SHA-256 verified); on a per-machine install it only links.
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
- `telemetry` — fleet inventory and command usage to the company's own sink; absent = nothing is
  sent (§10).

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

### 7. Rollout story — who does what

The split that matters: **IT touches a seat once** (the MSI and a pointer); everything that
changes afterwards lives at a URL the **BIM coordinator** controls. Nothing here is
AnalyseTool-specific machinery: the plugin reads one small file from `%ProgramData%\AnalyseTool\`
at startup and fetches the rest; Windows does the delivery of that one file.

**IT (once per seat, and per plugin version)**

1. **Plugin.** Put `AnalyseTool-<ver>-MultiUser.msi` on a share readable by domain computers.
   In the GPO linked to the BIM workstations OU: *Computer Configuration → Policies → Software
   Settings → Software Installation → New Package*, deployment **Assigned**. The MSI installs as
   SYSTEM at the next boot. New versions go in via the package's *Upgrades* tab (the MSI has
   `MajorUpgrade` configured). This is the one step that legitimately needs admin rights: code
   loaded into Revit must arrive through a trusted path.
2. **Pointer.** Same GPO: *Computer Configuration → Preferences → Windows Settings → Files → New
   File*, action **Replace**, destination `%ProgramData%\AnalyseTool\policy.json`, content = the
   pointer form (§1). Written once; it only changes if the URL moves.
   *Alternative without any file:* a DNS TXT record `_analysetool.<domain>` pointing at the URL
   (§8 discovery). Then IT's part is the MSI and one DNS record, and the plugin finds the policy
   itself. Add `"enforced"` via the pointer file if leaving must be impossible.
3. Optional: *Preferences → Environment* for `ANALYSETOOL_AI_KEY` when the key is per machine.

**Intune / Azure AD only:** same shape — MSI as a Win32 app (`msiexec /i … /qn`), the pointer via
an Intune PowerShell script.

**BIM coordinator (any time, no IT involved)**

- Owns `policy.json` at the URL: settings, locks, catalog URL, required extensions, AI endpoint,
  log sink. Edits reach every seat on its next Revit start (ETag refresh, §8). Keep it in a Git
  repository: history, review, and `AnalyseTool.Cli policy validate` in CI before it goes live.
- Owns `catalog.json` and the extension feeds / zips on any internal static hosting (GitLab raw,
  Nexus, an IIS folder, a file share, **a synced SharePoint library** — §9). Publishing a new
  extension version = replacing files.
  Extensions install into the user's managed zone with user rights — no admin needed, today
  already.
- Sees who is behind: the policy may carry `"minimumVersion"` + `"update"`; a seat below it shows
  a banner with the internal download link (per-user seats can self-update, below), and
  `GetOrganizationStatus` / the CLI report the version.

**Fully per-user path (no IT at all)**

When seats install the **SingleUser MSI** themselves (`%AppData%`, no admin rights), there is no
machine layer and the organization layer (§8) carries everything. The BIM coordinator publishes
three things on internal static hosting — the MSI, `policy.json`, the catalog + feeds — and hands
out one link. Getting a seat joined without typing:

- **Discovery** by domain (DNS TXT / well-known URL, §8) when IT is willing to add one DNS record.
- **Installer property.** `msiexec /i AnalyseTool.msi POLICYURL=https://…` writes `org.json` at
  install time; the coordinator distributes the MSI with a ready command line or a `.bat`. The
  seat is joined before Revit starts for the first time.
- **Invite link.** The installer registers the `analysetool://join?url=…` protocol; a link in
  mail or Teams opens the Join dialog with the URL filled in. Handled by the CLI exe (§6), which
  hands the request to a running Revit or stores it for the next start.

What per-user gains: **self-update of the plugin becomes possible.** A per-user MSI runs without
UAC, so `minimumVersion` + `organization.downloadUrl` can turn into "Install version X when Revit
closes": download from the policy's host only, verify the SHA-256 the policy carries, run
`msiexec /i … /qn` after Revit exits. Never for the per-machine install.

What per-user loses: no `enforced` (the user can Leave; the coordinator sees it in status), and
locks hold only while joined. Acceptable for most shops; strict ones bring IT in for the pointer.
Note for docs: SingleUser and MultiUser MSIs must not coexist on one machine — Revit would load
both `.addin` files (true today already).

**Pre-installed extensions via GPO (optional, IT-owned).** *Preferences → Folders/Files* can drop
ready extension folders into `%ProgramData%\AnalyseTool\extensions-dist\<id>\`. The plugin scans
that machine-level managed root read-only (**Machine** badge; no install/remove/update there). Use
this only when extensions must not be user-writable; otherwise `extensions.required` + a feed keeps
them in the coordinator's hands.

**Verify** a seat with `AnalyseTool.Cli policy show`, `org status` and `ext list`.

**Later, on request:** an ADMX template reading `HKLM\Software\Policies\AnalyseTool` as a second
source of the machine layer, for administrators who want checkboxes in the Group Policy Editor.

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
| path | `%ENV%` expanded, `policy.json` read from the folder (UNC share, synced SharePoint library — §9) |
| nothing | same two lookups against `USERDNSDOMAIN` (domain-joined machines), then synced-folder scan (§9) |

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

- HTTPS, or a file-system path (UNC share, synced SharePoint library — §9); plain `http://` is
  refused. A policy can install code and redirect AI traffic, so the transport must not be
  tamperable in transit; a share or synced library is protected by its own ACLs.
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

### 9. Where the URL comes from — hosting, and SharePoint / OneDrive

A policy source is anything the plugin can read **without an interactive login**. Two kinds:

| Source | Form | Notes |
| --- | --- | --- |
| Git hosting (GitHub / GitLab raw, internal GitLab) | `https://…` | best: history, review, `policy validate` in CI |
| Azure Blob Storage with a SAS token | `https://…?sv=…` | cheap static hosting, no Azure AD on the client |
| Internal web server (IIS, nginx) | `https://…` | classic |
| File share | `\\server\bim\analysetool\` | a path, not a URL — same loader |
| **Synced SharePoint / OneDrive library** | `%USERPROFILE%\Contoso\BIM Tools - Documents\AnalyseTool\` | **the SharePoint answer** (below) |
| SharePoint HTTPS link | — | **not supported**: the link needs an Azure AD sign-in; `HttpClient` gets a 401 or a login page. "Anyone" links with `?download=1` are tenant-disabled more often than not and unstable. |

So the loader accepts **`https://` URLs and file-system paths**, with `%ENV%` expansion in paths.
Everything that is "a policy source" (machine pointer, Join input, `catalogUrl`, feed `source`,
`updateFeed`) takes either form. Plain `http://` is refused everywhere.

**SharePoint / OneDrive — the synced-folder pattern**

Users already run the OneDrive client and sync the BIM library to disk. The tenant and library
names are the same for everyone; only `%USERPROFILE%` differs, and the plugin expands it.

```
%USERPROFILE%\Contoso\BIM Tools - Documents\AnalyseTool\
    policy.json
    catalog.json
    packages\
        company.standards-1.4.0.zip
        company.standards.feed.json      ← { "version": "1.4.0", "downloadUrl": "company.standards-1.4.0.zip" }
```

Two rules that make it work:

1. **A synced folder is a distribution source, never a load root.** Loading DLLs in place would
   have Revit lock files OneDrive is trying to sync — conflicts and half-written copies. The
   plugin *installs from* the folder into its own managed zone (`ExtensionInstaller`, exactly
   like install-from-zip) and *updates* by comparing the feed version with the installed one.
   `ExtensionUpdateFeed` therefore resolves relative `downloadUrl`s against the feed's own
   location, for files as for https.
2. **Files On-Demand.** Files may be placeholders; a read triggers a download. The coordinator
   marks the AnalyseTool folder "Always keep on this device", and the plugin reads with a
   timeout and reports "downloading from OneDrive…" instead of hanging the Revit UI thread.

**Giving people access.** Permissions are SharePoint's job (group membership); the policy cannot
grant them and must not try. What it can do is remove the manual steps after access exists:

- `sharepoint.syncUrl` — the `odopen://` link SharePoint's **Sync** button produces (or the
  Intune / GPO "Configure team site libraries to sync automatically" setting). If the synced
  folder is missing on a seat, the Organization panel shows **Connect the BIM Tools library**,
  which opens that link; OneDrive does the rest.
- **Discovery through synced folders.** Besides DNS and well-known URLs (§8), discovery scans
  `%USERPROFILE%\*\* - Documents\AnalyseTool\policy.json` (and `%OneDriveCommercial%`). A new
  hire's path: get SharePoint access → click Sync → open Revit → accept the Join banner.

**Later, not v1:** a SharePoint connector reading the library through Microsoft Graph with silent
Windows SSO (MSAL, an app registration, admin consent). It gives a true URL without syncing;
worth it only if the synced-folder pattern proves insufficient.

### 10. Telemetry — the company's, never the vendor's

There is no telemetry today, and for the single-seat product that stays so. The enterprise
scenario needs it in one specific shape: **data goes to the company's own sink, chosen by the
policy, and nowhere else.** AnalyseTool the project has no endpoint and collects nothing.

Three things that are usually lumped together:

| Kind | For whom | Today |
| --- | --- | --- |
| Diagnostics: exceptions, stack traces | support during an incident | yes — Serilog rolling file (`AppLog.cs`), `ExtensionDiagnostics`; `logging.sink` (§2) ships it centrally |
| Fleet inventory: plugin / Revit version, installed extensions, join state, policy version | the BIM coordinator ("who is behind?", "who still has extension X?") | no |
| Usage: which command ran, from which transport, how long, success or error class | the coordinator ("is X used at all?", "why does Y fail for half the office?") and extension authors | no |

The second and third are the telemetry this section adds. They are the fleet-wide view of what
`GetOrganizationStatus` already shows on one seat.

**Rules**

- **Off unless the policy turns it on.** A seat without a policy (or with a policy that omits
  `telemetry`) sends nothing. Written in the docs in exactly those words.
- **The recipient is the company.** `telemetry.sink` names an OTLP endpoint, Seq, Application
  Insights, or a folder (a share, or the synced SharePoint library — §9) receiving JSON-lines
  files. No default, no fallback host.
- **Visible to the user.** The Join preview lists "Sends telemetry to: …" (§8); the Organization
  panel has **Show recent events** that prints exactly what left the seat.
- **No model data, ever.** Events carry command name, extension id, transport (`WebView2`,
  `Mcp`), duration, outcome (`ok` / error type name), versions. Never a payload, a file name, an
  element name, a parameter value. User and machine identity only with `identity: "user"`;
  the default `"hashed"` sends a stable per-seat hash so the coordinator can count seats without
  naming them.
- **Never blocks a command.** Buffered, sent in the background, dropped when the sink is down.

**Policy**

```json
"telemetry": {
  "sink": "https://otel.company.local/v1/logs",      // or a folder path (JSON lines, one file per seat per day)
  "identity": "hashed",                               // "hashed" | "user"
  "events": ["inventory", "command", "ai"]            // subsets are fine
}
```

**Where it lives in the code**

- One hook in `CommandQueue` (Core) — the single entry point for every transport — emits a
  `command` event per request with start/end, source, outcome. Third-party extensions are covered
  without changing them.
- An `inventory` event at bootstrap (versions, extensions, join state) and after every
  Join / Leave / update.
- `ai` events from `AiClientFactory` (Tools): provider id, model, token counts — the coordinator's
  cost view. Reaches Core via the same Sdk policy accessor as §4, so Tools stays on the Sdk only.
- Events are structured Serilog events on a dedicated logger; `telemetry.sink` picks the Serilog
  sink (OTLP / Seq / file). No new dependency for the file form.
- A tier-1 test asserts that no property of a `command` event equals or contains the request
  payload — the class of leak this design promises not to have.

**Not doing:** anonymous usage statistics for the author "to know what is used". That is a
separate consent, privacy document and infrastructure, and it undercuts the one-line promise that
sells the enterprise variant: data does not leave the company.

## Security notes

- Policy is trusted **because of where it is**: `%ProgramData%` is admin-writable only on a
  correctly configured machine. No signature in v1; document the assumption. Signing is a
  later addition if a customer asks.
- `allowedFeeds` and `allowInstallFromRepository=false` are the two settings that turn the tool
  from "installs what the user pastes" into "installs what IT approved". They are the reason
  the policy layer exists; ship them in phase 1, not later.
- `codeExecution.enabled=false` + locked should be the recommended enterprise default in the docs.
- Telemetry is off without a policy, goes only to the sink the policy names, and never carries
  model data (§10). The vendor has no endpoint.
- The organization layer is trusted **because the user consented** to a specific origin (an HTTPS
  host or a path) after a preview — not by location. A path under the user's own profile grants
  nothing the user did not already have: they own their settings anyway. A policy fetched from a URL is never applied silently, and a URL
  change (redirect to another host) invalidates the join and asks again.

## Testing

Tier 1 (`AnalyseTool.Tests`), Revit-free:

- Policy parsing: missing file, broken JSON, unknown keys, each section alone.
- Layer resolution: machine wins, locked refuses writes, user-only behaves as today.
- Catalog merge order shipped → policy → user with id overrides.
- `allowedFeeds` matching (prefix, case, `github:` form).
- Three-layer resolution: machine over organization over user; locks honored per origin.
- Discovery input parsing: URL vs domain vs path vs empty; `http://` refused; `%ENV%` expansion.
- Feed with relative `downloadUrl` resolved against a file-system feed location.
- `org.json` refresh: ETag unchanged, changed, fetch failure keeps the cache; Leave removes locks.
- Telemetry: no event without `telemetry` in the policy; `command` events never contain the payload;
  file sink writes valid JSON lines; a dead sink does not fail or delay the command.
- CLI: `policy validate` exit codes on good/bad files; `ext validate` on the Acme.Sample zip.

## Implementation phases / TODO

Phase 1 — policy layer (no UI, no CLI): the smallest change that makes the tool manageable.

- [x] `PathProvider.MachineProfilePath`, `PolicyPath`
- [x] `Core/Common/Policy/PolicyStore` + `PolicyDocument` model, load-once, diagnostics on error
- [x] Machine file in two forms: inline policy, or pointer `{ policyUrl, enforced }` — the pointer is recognized and reported; following it lands with 3a
- [x] `CodeExecutionSettings` reads policy, refuses when locked
- [x] `ExtensionSources` appends policy roots (`%ENV%` expanded), honors lock
- [x] Machine-level managed root `%ProgramData%\AnalyseTool\extensions-dist` scanned read-only (Machine badge; no install/remove/update there)
- [x] `allowedFeeds` + `allowInstallFromRepository` enforced in feed resolution and install commands
- [x] `McpServerController` honors `mcp.enabled`
- [x] `GetPolicyStatus` / `ReloadPolicy` commands (the data the Organization panel and the CLI read)
- [x] Tier-1 tests for parsing, resolution, feed whitelist
- [x] `docs/policy.schema.json` (JSON Schema for editor completion and `policy validate`)

Phase 2 — catalog and required extensions.

- [ ] Remote `catalogUrl` with ETag cache, merge order shipped → policy → user
- [ ] `extensions.required`: install on startup, block disable/remove, update with the rest
- [ ] Tier-1 tests for merge order and required-id protection

Phase 3a — policy sources and discovery (no join yet; everything the machine pointer and Join
will share).

- [ ] Policy source abstraction: `https://` **or** file-system path with `%ENV%` expansion; `http://` refused; shared by pointer, Join, `catalogUrl`, feeds
- [ ] Fetch with `If-None-Match` / file timestamp; cache under the user profile; offline keeps cache
- [ ] Machine pointer form `{ policyUrl, enforced }` resolved through the source abstraction (completes the phase-1 placeholder)
- [ ] Discovery: URL / domain / path / `USERDNSDOMAIN`; DNS TXT `_analysetool.<domain>`, `/.well-known/analysetool/policy.json`, synced-folder scan (`%USERPROFILE%\*\* - Documents\AnalyseTool`, `%OneDriveCommercial%`)
- [ ] `ExtensionUpdateFeed`: file-system feeds, relative `downloadUrl`; install-from-folder copies into `extensions-dist`, never loads in place
- [ ] Files On-Demand: reads with timeout off the UI thread, "downloading from OneDrive…" status
- [ ] `organization.name` / `contact` required for a joinable policy
- [ ] Tier-1 tests: input parsing, `%ENV%` expansion, `http://` refusal, refresh cases (unchanged / changed / failure), relative feed paths

Phase 3b — Join organization (the organization layer, on top of 3a).

- [ ] `org.json` model + `OrgPolicySource` loader in `PolicyStore`; three-layer merge with `Origin`
- [ ] Commands `DiscoverOrganizationPolicy`, `JoinOrganization`, `LeaveOrganization`, `GetOrganizationStatus`
- [ ] Preview model listing changes, locks, extensions to install, AI endpoint, log sink, telemetry sink
- [ ] Host change invalidates the join and asks again; `enforced` hides Leave
- [ ] `minimumVersion` banner + `GetOrganizationStatus` reports version
- [ ] `sharepoint.syncUrl` → **Connect the BIM Tools library** action when the source folder is missing
- [ ] Installer: `POLICYURL` property writes `org.json` at install time (SingleUser and MultiUser MSI)
- [ ] Per-user self-update: download from the policy host only, verify `update.sha256`, run `msiexec /qn` after Revit exits; disabled on per-machine installs
- [ ] Tier-1 tests: resolution order, leave semantics, enforced, preview contents

Phase 4 — Sdk contract and Tools.

- [ ] Sdk: read-only policy section accessor on the context (minor version bump, CHANGELOG, ONBOARDING §Sdk)
- [ ] `AiProviderRegistry`: policy providers, `apiKeyEnv`, `allowUserProviders`
- [ ] `AppLog`: policy logging sink

Phase 5 — telemetry (company sink only).

- [ ] `telemetry` policy section: `sink` (https OTLP / Seq / folder), `identity`, `events`; absent = off
- [ ] `CommandQueue` hook → `command` events (name, extension id, transport, duration, outcome); never the payload
- [ ] `inventory` event at bootstrap and after Join / Leave / extension update
- [ ] `ai` events from `AiClientFactory` via the Sdk policy accessor (provider, model, tokens)
- [ ] Dedicated Serilog logger + sink selection from policy; background buffer, drop on sink failure
- [ ] Organization panel: **Show recent events**; Join preview line "Sends telemetry to: …"
- [ ] Tier-1 tests: off by default, payload never leaks, JSON-lines file sink, dead sink is harmless

Phase 6 — UI.

- [ ] Locked state rendering in Settings (disabled + lock icon + tooltip)
- [ ] **Organization** panel: policy status, effective values with origin
- [ ] **Required** badge; hide disable/remove for required extensions
- [ ] Organization panel: Join organization… dialog with preview, Leave organization, "Managed by <name> — updated <time>"
- [ ] First-start banner when discovery finds a policy (offer only, never auto-apply)

Phase 7 — CLI.

- [ ] `AnalyseTool.Cli` project (Core + Sdk), `InternalsVisibleTo`, `Check-Boundaries.ps1`, CLAUDE.md / AGENTS.md table row
- [ ] `policy show`, `policy validate`
- [ ] `ext list`, `ext install`, `ext validate`, `ext update`
- [ ] `org join <url|domain>`, `org leave`, `org status`
- [ ] `analysetool://join?url=…` protocol handler registered by the installer, served by the CLI exe
- [ ] `diag collect`
- [ ] Ship via `PluginAssets.targets` + MSI; tier-1 tests drive the exe like `McpExeTests`

Phase 8 — docs.

- [ ] ONBOARDING.md § "For BIM coordinators": owning policy.json in Git, catalog and feeds, minimumVersion, validate in CI; hosting options table; the SharePoint synced-library layout, "Always keep on this device", `odopen://` sync link
- [ ] ONBOARDING.md § "For IT administrators": MSI + pointer (or DNS TXT) only; GPO step-by-step (Software Installation + Preferences → Files), Intune variant, policy.json reference, hosting a catalog/feed, publishing for Join (DNS TXT / well-known URL), CLI
- [ ] ONBOARDING.md § "Joining your company's configuration" for end users (invite link, installer property, SingleUser vs MultiUser warning)
- [ ] ONBOARDING.md § telemetry: what is sent, to whom, how to turn it on, the "nothing without a policy" promise
- [ ] LLM.md: one paragraph on reading a policy section from an extension
- [ ] CHANGELOG.md entry
