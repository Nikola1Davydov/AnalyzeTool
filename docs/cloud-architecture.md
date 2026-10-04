# Cloud edition — design (vision)

Status: vision, pre-implementation. Reference product: Orkestra Online / OkPy (a firm-wide
library of automation tools, an AI agent that writes tools, analytics on every run).

Relation to [`extension-platform-design.md`](extension-platform-design.md): the public product
keeps its rule — AnalyseTool never hosts third-party binaries (#48). The cloud edition does not
break it: every firm runs its **own** tenant (its own Azure subscription, its own Blob Storage),
and the internal marketplace holds that firm's own tools. AnalyseTool ships the software, the
firm owns the data and the binaries.

## What can and cannot move to the cloud

The Revit API exists only inside a running `Revit.exe` with an open model. So the cloud holds
the **control plane** — the library of buttons, roles, analytics, the AI loop — and the model
work stays on the user's machine. (APS Design Automation is batch processing of `.rvt` files
without UI; it is not a live model and is out of scope.)

```
┌──────────────────────────── Azure (firm's tenant) ─────────────────────────┐
│ Entra ID — sign-in, users, teams                                           │
│                                                                            │
│ ASP.NET Core API (App Service / Container Apps)                            │
│  ├─ /marketplace   listings, installs, ratings, review requests            │
│  ├─ /buttons       personal buttons, versions, shares                      │
│  ├─ /me/ribbon     what THIS user gets on the ribbon (rules applied)       │
│  ├─ /analytics     run events in, dashboards out                           │
│  ├─ /ai/chat       agent loop; model keys live here only                   │
│  └─ SignalR hub    persistent link to every running Revit                  │
│                                                                            │
│ Azure SQL — users, teams, buttons, versions, shares, listings, runs, bugs  │
│ Blob Storage — immutable packages (one blob prefix per version)            │
│ Azure OpenAI / Claude on Microsoft Foundry — models                        │
└──────────────────────▲─────────────────────────────▲───────────────────────┘
                       │ HTTPS: list + download      │ WebSocket: "run command X"
┌──────────────────────┴─────────────────────────────┴───────────────────────┐
│ Revit + AnalyseTool                                                        │
│  package cache %LOCALAPPDATA%\AnalyseTool\cloud\  → collectible ALC (as today) │
│  SignalR transport → CoreServices.Queue → command                          │
└────────────────────────────────────────────────────────────────────────────┘
```

Cloud is an **opt-in mode**, not a replacement. The local product (Ollama, local MCP, local
extension folders, "your model data stays on your machine") keeps working unchanged; a firm
switches the cloud mode on.

## Roles and teams

Roles are per team: `Membership(UserId, TeamId, RoleId)`. A user may be in several teams. Each
role has a company-defined access level (see *Access levels per button*).

| Role | Own buttons | Sees | Can |
| --- | --- | --- | --- |
| **Member** | any number, private | own buttons + what was shared with them + listings of their teams/company | create, share, submit to the team marketplace, install from the marketplace |
| **Lead** (per team) | yes | everything of **their team's** members, incl. analytics | try a member's button, approve team listings, create team/project buttons and toolsets — own or assembled from members' buttons |
| **Admin / QA** (company) | yes | everything | company-wide analytics, quality gates, bugs, certification, manage teams and roles |

**Visibility rule** — user U sees button B if any holds:
1. U is the author of B;
2. U is Lead of a team the author of B belongs to (Admin: always);
3. B (or a toolset containing it) is shared with U, directly or via a group;
4. B is listed in a marketplace scope U belongs to (team or company).

"Sees" (catalog, code, analytics) is not "has on the ribbon": nothing lands on anyone's ribbon
without an explicit install, except toolsets a Lead pins for the team or project.

## Access levels per button

Seeing a button and being allowed to download it are separate rights. Roles carry an ordered
**access level** that the company defines (e.g. 1 Member, 2 Power user, 3 BIM coordinator /
Lead, 4 Admin) — the names are the firm's, the platform only compares numbers.

| Right | Meaning | Set on the listing / button as |
| --- | --- | --- |
| **View** | appears in the catalog with description and stats | `ViewLevel` (default: everyone in scope) |
| **Install** | download the package, put it on the ribbon, run it | `InstallLevel` (+ optional allowed teams / groups) |
| **Inspect** | see the source, download the package for review | `InspectLevel` (default: Lead and above) |
| **Edit / publish versions** | change it | owner + reviewers of its scope |

- A button visible but above the user's `InstallLevel` shows as **locked** with "Request
  access" — the request goes to the Lead of the user's team. A button above `ViewLevel` is not
  listed at all.
- Typical use: destructive or model-wide tools (purge, mass rename, workset moves) listed for
  everyone to see, installable from level 3 only.
- Defaults by origin: a personal button — Install for the owner, Inspect for the team's Lead;
  a share grants Install to its targets; a listing takes the levels the reviewer sets.
- Enforced **server-side**, never only in the UI: `/me/ribbon` omits what the user may not
  install, and a SAS download URL is issued only after the check. Lowering a user's level or
  raising a listing's `InstallLevel` removes the package from that user's ribbon and cache on
  the next sync.
- Install is not secrecy: a downloaded DLL can be decompiled. Anything that must stay
  confidential (credentials, proprietary rules) belongs on the server, not in the package.

## Button lifecycle

```
Personal ──share──► Shared with people (live reference)
   │
   ├─submit──► Team listing ──(Lead approves)──► team marketplace
   │                │
   │                └─submit──► Company listing ──(QA approves, checks pass)──► company marketplace, "Certified"
   │
   └─Lead copies──► Team / Project button (snapshot)
```

- **Share** is a reference: the author edits, the recipient gets the new version. Free exchange
  between peers; the author can revoke at any time; the recipient decides whether to install.
- **Listing and promotion are snapshots** of one version (`SourceVersionId` keeps provenance).
  An author's later edit never silently changes what a team runs — the author "proposes an
  update", the reviewer sees a diff and accepts, like a pull request.
- **Fork**: anyone may fork a listing into a personal button (provenance kept).

## Internal marketplace

The firm's catalog of reviewed tools, in two scopes: **team** (visible to that team) and
**company** (visible to everyone).

- Listing page: description, screenshots, author, versions + changelog, Revit years supported,
  `Destructive` / `ReadOnly` flags of its commands, install count, run count, failure rate,
  ratings and comments.
- Install is opt-in per user; Leads may additionally **pin** toolsets that every team or
  project member gets.
- The existing manager UI grows naturally: the planned **Available** tab of the public
  registry (#76) shows the internal marketplace when the cloud mode is on.
- Review request = the PR analogue: submission, automated checks, reviewer, decision, history.

## Toolsets and composition

- **Toolset**: a named set of buttons from different authors, each pinned to a version — e.g.
  the panel of project X. Start here.
- **Composite button** (later): a command that calls other commands through the queue by name
  ("number → check → export"). Possible because every command is a schema'd `CommandRequest`;
  needs a dependency contract (the composite declares the versions it calls).

## Project buttons

A project toolset appears when a model of that project is open. Binding by ACC project id,
document `CreationGUID`, or a Project Information parameter. Closed project → its panel is gone.

## Ribbon layout

Panels by origin: **My** (personal), **Shared with me**, **Team**, **Project**, **Company**,
and **Review** — what a Lead or QA is currently trying. Review shows a banner: unreviewed code
by <author>. A collectible ALC is a loading boundary, not a security boundary: unreviewed code
runs with the user's rights, so it never arrives anywhere without an explicit action.

## Storage and package integrity

Package format = the existing one (`plugin.json`, `ui/`, `<year>/*.dll`). Blob layout:

```
packages/{buttonId}/{version}/package.zip      immutable, never overwritten
packages/{buttonId}/{version}/package.sig      server signature over SHA-256
```

- Upload → server validates (manifest, id, years — the same rules as `PackExtension`), runs the
  schema checks of `Check-Schemas.ps1` server-side, stores the zip, records SHA-256, signs it.
- Client downloads with a short-lived SAS URL, verifies hash and signature against a public key
  compiled into the host, unpacks into the cache, loads through the existing `ExtensionLoader`.
- UI is served from the local cache via WebView2 virtual host mapping, not from the cloud:
  page and DLL always match, works offline, no CORS.
- The update path reuses `ExtensionUpdateFeed`'s HTTPS form (`{version, downloadUrl}`).

## Analytics, quality, bugs

Every run already passes through `CommandQueue`, so one event per run is emitted in one place:

```
RunEvent { buttonVersionId, userId, teamId, source (button | mcp | cloud-agent),
           durationMs, ok, errorType, revitVersion, documentHash, at }
```

- `documentHash`, never the model name — client and project names do not belong in analytics.
- Failures carry the root exception (as the bridge already reports since #97) and are grouped
  by fingerprint into **bugs** linked to button + author. An AI pass may propose a fix.
- Company level: usage across teams, duplicates (five people wrote the same numbering tool),
  failure rates, certification queue.

**Privacy (DSGVO, §87 BetrVG).** Per-employee analytics visible to a supervisor is
performance monitoring and needs a works-council agreement in Germany. Default: Leads see
analytics **per button**; per-person breakdown is a company-level setting, off by default.

## AI through Azure

- Models: Azure OpenAI (GPT) or Claude on Microsoft Foundry (C# SDK: `Anthropic.Foundry`,
  `AnthropicFoundryClient`). Keys live on the server only; the client authenticates with its
  Entra token. Per-user limits, cost and logs come for free.
- The agent loop runs on the server. Tools = the command catalog (schemas already exist and are
  CI-checked). A tool call travels over SignalR to the user's Revit → `CoreServices.Queue`
  → result back to the model.
- The OkPy-Agent loop maps onto what exists: write → `ExecuteRevitCode` on the live model →
  `SaveAsCommand` — with "save" becoming "publish as a personal button".
- Stepping stone: a server endpoint speaking OpenAI-compatible `/v1/chat/completions` is just
  another provider in the existing AI settings.

## Client changes

| Change | Where | Notes |
| --- | --- | --- |
| Cloud extension source (list + download + verify + cache) | Core | sits beside the local roots in `ExtensionCatalog`; headless |
| `GET /me/ribbon` replaces local scanning in cloud mode | Core / App | ribbon groups by origin |
| SignalR transport | new project `AnalyseTool.Remote.SignalR` (Core + Sdk) | the `Mcp.Bridge` pattern: one `InternalsVisibleTo`, zero Core changes |
| Consent for remote destructive calls | App | via `CommandRequest.Gate` — the hook already exists for this |
| Caller identity on requests | Core | the `CallerIdentity` init-property already reserved in `CommandRequest` |
| Run events | Core | emitted by the queue, shipped in batches |
| Sign-in (Entra ID, MSAL) | App | UI; Core gets a token provider |

## Data model (minimum)

```
User          (Id, EntraObjectId, Name)
Team          (Id, Name)
Role          (Id, Name, Level)                             -- company-defined, ordered by Level
Membership    (UserId, TeamId, RoleId)                      -- Admin is a company flag on User
Group         (Id, Name) + GroupMember(GroupId, UserId)      -- optional, for sharing to many
Button        (Id, OwnerUserId, Kind: Personal | Team | Project, TeamId?, ProjectId?)
ButtonVersion (Id, ButtonId, Version, BlobPath, Sha256, Signature, CreatedAt,
               SourceVersionId?)                             -- provenance of copies and forks
Share         (ButtonId | ToolsetId, TargetUserId | TargetGroupId, GrantedBy, At)
Listing       (Id, ButtonVersionId, Scope: Team | Company, TeamId?, Status, Certified,
               ViewLevel, InstallLevel, InspectLevel)
ListingAudience(ListingId, TeamId | GroupId)               -- optional narrowing of Install
AccessRequest (Id, UserId, ListingId, State, DecidedBy?)
ReviewRequest (Id, ButtonVersionId, TargetScope, ReviewerId?, State, Comments)
Install       (UserId, ListingId | ButtonId, PinnedVersionId?, At)
Toolset       (Id, OwnerUserId, Scope, TeamId?, ProjectId?, Pinned)
ToolsetItem   (ToolsetId, ButtonVersionId, Order, Panel)
RunEvent      (ButtonVersionId, UserId, TeamId, Source, DurationMs, Ok, ErrorType, RevitVersion, At)
Bug           (Id, ButtonId, Fingerprint, FirstSeen, LastSeen, Count, State)
```

## Phases

1. **Library**: Blob + registry API + signed packages; the client installs from it through the
   existing update-feed path. Personal buttons and sharing. No AI, no review.
2. **Marketplace**: teams, roles, listings, review requests, toolsets, ribbon groups.
3. **Analytics and bugs**: run events, dashboards, bug grouping, privacy setting.
4. **Cloud agent**: AI proxy, SignalR transport, server-side agent loop, publish from the agent.

## Open questions

- Self-hosted per firm (deploy template into the firm's Azure) vs one multi-tenant SaaS.
  Self-hosted keeps #48 literally true and suits firms that keep data in-house.
- Is a Lead's view limited to direct team members, or also to sub-teams?
- Offline behaviour: how long does a cached package stay runnable without the server
  (licence and revocation vs a site with no internet)?
- Revocation: a withdrawn or vulnerable version — removed from caches on next sync, or blocked
  from running immediately?
