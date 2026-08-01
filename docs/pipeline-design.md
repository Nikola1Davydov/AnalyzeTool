# Pipelines — design (local engine)

Status: agreed direction, pre-implementation. Covers **Step 1** of #70 (local pipeline
engine, editor, `.atpipe` sharing). Step 2 (cloud / session layer) stays as written in
#70 and is deliberately not detailed here — it starts only after the pilot gate.

## What the platform already gives us

The engine is a new CALLER of the existing platform, not a new execution path. What it
inherits for free:

| Need | Already in the codebase |
| --- | --- |
| One entry point | `CommandQueue` (`src/AnalyseTool.Core/Common/Dispatch/CommandQueue.cs`) — `CommandRequest` already carries `CancellationToken`, `IProgress<ProgressInfo>` and a pre-execution `Gate`. The `LocalDispatcher` is genuinely a thin wrapper: it enqueues, like WebView2 and the MCP bridge do. |
| Node catalogue | `CommandDispatcher.RegisteredCommands` / the `GetCommands` command. The palette is a projection of this — the same catalogue #43 (command palette) needs. |
| Parameter forms | `RevitCommandAttribute.InputType` → JSON Schema via `AIJsonUtilities`. **46 of 72** registered commands already declare it; the node's parameter form is generated from it. |
| Cancellation | Flows to `IRevitTask.ExecuteAsync`. The Stop button is nearly free. |
| Risk flags | `ReadOnly` (39 commands) and `Destructive` (7) are already declared and already gate MCP exposure. |
| Visibility of a running node | `CommandQueue._running` + `GetQueueStatus` — the busy bar shows the executing node without a line of new UI plumbing. |

## The central decision: a node IS a command

#70 splits nodes into *command* nodes and *orchestrator* nodes (Filter, ExportExcel,
ExportPdf). We do not give the engine two kinds of node. Orchestrator nodes are ordinary
`[RevitCommand]` implementations that never call `RunInRevitAsync`; they declare
`Headless = true`, and the dispatcher routes them to a background thread instead of the
Revit thread.

What this buys:

- The engine has ONE node type and one dispatch decision. No second code path to keep
  in step with the first.
- Filter / ExportExcel / ExportPdf live in `AnalyseTool.Tools` as normal vertical-slice
  features, not inside the engine. Core stays the platform, Tools stays the features —
  the dependency contract in CLAUDE.md is untouched.
- **Extensions ship nodes for free.** A third-party `[RevitCommand]` is already a node,
  including a headless one. The "custom nodes ecosystem" that #70's ComfyUI notes call a
  trump card exists by construction, with no extra SDK surface.

Placement follows from the contract: engine + `.atpipe` schema in `AnalyseTool.Core`
(headless, no WPF), nodes in `AnalyseTool.Tools` and in extensions, editor in the Vue
client, `RunPipeline`/`ValidatePipeline` as ordinary commands.

## The expensive part is outputs, not the engine

Today **no command declares its result type**. `InputType` exists (46 of 72 use it);
there is no `OutputType`, and commands return anonymous objects — `GetCommands` returns
`new { commands }`, `SetDataToParameters` returns `null`. `McpBridgeServer` advertises
`InputSchema` and nothing about the shape coming back.

So "typed connections between nodes" is not an attribute to add — it is a refactor of the
Tools commands to declared result types, slice by slice. That is the critical path of the
whole of Step 1, and it is why it ships FIRST and SEPARATELY (S1 below): declared outputs
improve the MCP layer immediately — an agent currently infers the response shape from a
prose description — with no pipeline in sight.

Corollary from #70's AI-node notes, worth restating because it inverts the usual
intuition: an **AI node is easier to type than a command**. Its output schema is part of
the node's configuration (it doubles as the model's response format and its validator),
so it needs no refactor at all.

## Revit thread: a pipeline run interleaves — decided, V1

`RevitTaskHub.Execute` drains its whole queue in one go (`RevitTaskHub.cs:65`). There is
no notion of "this pipeline owns the Revit thread until it finishes": between node N and
node N+1, work raised by a WebView2 window or an MCP agent will run.

**Decision for V1: accept the interleaving and state it in the contract.** A pipeline run
is not a transaction and is not atomic. The alternative — leasing the hub for the length
of a run — freezes the UI for the whole of a mutating pipeline and edits the busiest,
most delicate piece of dispatch code in the repo; that price is not worth paying before a
single pipeline has ever run.

What that obliges us to do instead:

- A mutating node **re-checks its own preconditions**; it may not assume the state an
  earlier node observed still holds. The existing idempotency modes are the mechanism —
  `SetDataToParameters` already ships `Overwrite` / `OnlyIfEmpty` / `SkipIfEqual`.
- The `.atpipe` contract says this out loud, so nobody designs a chain that depends on
  atomicity and discovers the truth from a corrupted model.
- Revisit when a real mutating pipeline exists and we can measure how often it actually
  bites — not before.

## Write safety is a hard blocker (S0)

#70 names this in its header, and the code confirms every part of it:

- `IFailuresPreprocessor` exists **only in the Families slice**
  (`SwallowWarningsPreprocessor`, 8 call sites). A pipeline chains mutating commands with
  no human between the nodes: an unhandled Revit failure dialog stalls the run in a place
  where nobody is watching.
- No re-entrancy guard on the mutating path.
- MCP timeouts / logging.

None of this is pipeline-specific, all of it becomes unavoidable here. S0 ships before
any node may mutate.

## `.atpipe` v1 — the contract

JSON, versioned, ours. Shape:

```
{
  "schema": 1,
  "id": "...", "name": "...", "author": "...", "version": "1.0.0",
  "nodes": [ { "id": "n1", "command": "GetElements", "contract": 2, "params": { ... } } ],
  "edges": [ { "from": "n1", "to": "n2" } ],
  "state":  { ... }        // run state — see below
}
```

Three properties designed in from the start, even though V1 is linear and synchronous:

1. **`contract` per node** — a command's payload/result shape is versioned
   (`CheckNaming@2`), so an old file fails loudly against a newer command instead of
   silently doing something else.
2. **`state` for pause/resume** — approval nodes (S4) suspend a run and resume it later,
   possibly after a Revit restart. Retrofitting suspension into a format that assumed one
   synchronous pass is a migration; reserving the field now is free.
3. **Run receipt** — every artifact a pipeline exports (Excel, PDF, report) embeds the
   `.atpipe` that produced it plus the command contract versions and, for AI nodes, the
   model. Audit and reproducibility by construction (the ComfyUI lesson from #70).

Node result caching, keyed by (inputs + params) hash, applies to **`ReadOnly` nodes
only** — this is exactly what the existing flag is for. Mutating nodes are never
auto-replayed; ComfyUI's nodes are pure functions, ours change a live model.

Files: `%LOCALAPPDATA%\AnalyseTool\pipelines\` via `PathProvider`, plus explicit
export/import. Sharing is a file over Teams or email — no server, same stance as
extension distribution (#48).

## Engine (S2)

- `IPipelineEngine`, `INodeDispatcher`, status events
  (Queued → Executing → Completed / Failed / Cancelled).
- `LocalDispatcher : INodeDispatcher` over `CoreServices.Queue` — the busy bar,
  `GetQueueStatus` introspection and the confirmation gate come along for free.
- Linear execution in V1, `CancellationToken` threaded through the whole run.
- **Revit-free by construction, and tested that way.** Today `AnalyseTool.Test` holds a
  single `UnitTest1.cs` and references `AnalyseTool.App`, so it drags in Revit — engine
  unit tests against a fake dispatcher cannot live there. A Revit-free test project is
  part of S2, not an afterthought.
- **Headless before the editor:** the milestone of S2 is a `RunPipeline` command that
  executes a hand-written `.atpipe` from MCP or the console. The engine gets dogfooded
  while the canvas is still a sketch.

## Editor (S3)

- **Vue Flow, as a new dependency.** The existing `src/view/InfiniteCanvas` is a pan/zoom
  canvas of cards (`useCanvas`, `useDrag`, `useCanvasPersistence`, `CanvasCard`) with no
  edges, ports or connection model — reusing it would mean writing a graph library inside
  a dashboard. Its persistence and viewport composables are still worth reading before we
  write ours.
- Palette from the command catalogue, filtered by capability flags — the same catalogue
  as #43; build it once, both features consume it.
- Parameter forms generated from `InputSchema` (string → input, bool → toggle, enum →
  dropdown). This part works today because `InputType` already exists.
- Validation at BUILD time, not run time: incompatible schemas, missing parameters,
  commands absent from this installation.
- Live node status + Stop.

## AI nodes and the approval invariant (S4)

From the #70 comment, unchanged in substance: AI transformation / AI router / bounded
agent node; model chosen per node from `AiProviderRegistry`; batching of items is node
infrastructure, not the pipeline author's problem; provenance ("AI decided, confidence,
why") rides with each item into the approval card.

The invariant is a property of the graph, machine-checked when the graph is built: **an
edge from an AI node may not reach a `Destructive` command directly — an approval node
must sit between them**, and the editor inserts one automatically. "AI never writes to the
model without a human" is then topology, not user discipline.

## Microsoft Agent Framework: not in V1

The spike proposed in #70 stays open, but not on the critical path. A linear executor is
small; MAF is an external dependency inside `AnalyseTool.Core`, which is loaded into an
ALC inside the Revit process and multi-targets net8/net10. Dependency conflicts there are
not hypothetical. Since `.atpipe` is our contract either way — MAF would only ever be an
interpreter of it — adopting it later costs nothing that adopting it now would save. The
honest moment to re-evaluate is S4, where checkpointing and human-in-the-loop become real
requirements rather than anticipated ones.

## Phases

| | Scope | Ships value alone? |
| --- | --- | --- |
| **S0** | Write-safety hardening: global `IFailuresPreprocessor`, re-entrancy guard, MCP timeouts/logging | Yes — fixes today's mutating commands |
| **S1** | `OutputType` + `[NodeParam]` in the SDK; declared result types per Tools slice | Yes — MCP agents stop guessing response shapes |
| **S2** | `.atpipe` v1, `PipelineEngine`, `LocalDispatcher`, Revit-free test project, `RunPipeline` | Yes — scriptable pipelines with no UI |
| **S3** | Vue Flow editor, palette (shared with #43), generated forms, build-time validation | The user-facing feature |
| **S4** | AI nodes, approval gate, pause/resume | Depends on S2's reserved `state` |

S1 is an additive change to the public SDK contract → minor bump of `AnalyseTool.Sdk`
(currently 1.1.2) and a manual push per `RELEASE_CHECKLIST.md`.

## Out of scope (deliberately)

- Branching in V1. Linear is right for reasons beyond effort: #70's ComfyUI notes call the
  spaghetti threshold the reason mass users stayed on simple forms. Pre-baked pipelines
  are surfaced as ordinary panels — button on top, graph underneath for whoever wants it.
- Step 2 (sessions, SignalR, distributed execution) — gated on a pilot, per #70.
- Scheduled triggers. Change Journal triggers (#80) are the more valuable kind and belong
  to that feature's timeline, not this one.
- Licensing/gating of nodes (#72).
