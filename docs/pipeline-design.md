# Pipelines — design (format + runner; the editor is deferred behind a gate)

Status: agreed direction, pre-implementation. Covers **Step 1** of #70. Step 2 (cloud /
session layer) stays as written in #70.

This document is the result of checking #70 against the code. Two things changed shape in
the process, and both are load-bearing:

- **Two of the five phases are not pipelines at all.** Write-safety (#88) and declared
  command outputs (#89) are platform debt that pays off with zero pipelines in existence.
  They are tracked outside this umbrella.
- **The node editor moved behind a gate.** It is the only part that adds a new consumer
  surface, and by this project's own reasoning it may never be needed. See "The gate".

## A pipeline engine is a composer, not a transport

#70 describes the engine as "just another caller, like WebView2 and the MCP bridge". That
undersells a category difference worth naming, because it is where the "third crutch on the
same funnel" risk actually lives.

WebView2 and the MCP bridge are **transports**: they translate an external actor's request
into a call. The engine is a **composer**: it decides what to call, in what order, based on
what came back. Same funnel, different kind of client.

The test for whether that is a crutch is whether the third client deforms the contract
under itself. It did not. Every contract extension #70 anticipated for pipelines —
`Headless`, `SupportsProgress`, `[NodeParam]`, an execution-mode signal, a
`RequiresUser` flag — turned out on inspection to be already solved, to belong in Core, or
to be premature (see below). Exactly one property survives, and MCP wants it anyway.

## What the platform already gives us

| Need | Already in the codebase |
| --- | --- |
| One entry point | `CommandQueue` — `CommandRequest` already carries `CancellationToken`, `IProgress<ProgressInfo>` and a pre-execution `Gate`. The engine enqueues; nothing in dispatch changes. |
| Node catalogue | `CommandDispatcher.RegisteredCommands` / `GetCommands`. The palette is a projection — the same catalogue #43 needs. |
| Parameter forms | `InputType` → JSON Schema via `AIJsonUtilities`; **46 of 72** commands declare it, and field descriptions come from plain `[System.ComponentModel.Description]` (already used in `PurgeFamilies.Request`, `IsolationInRevit.Request`). |
| Off-Revit-thread execution | Automatic. `CommandDispatcher.DispatchAsync` just awaits `ExecuteAsync` on the caller's thread; `RunInRevitAsync` is the only path to the Revit thread. A command that never calls it never touches it. |
| Cancellation | Reaches `IRevitTask.ExecuteAsync`. Stop is nearly free. |
| Risk flags | `ReadOnly` (39) and `Destructive` (7) already declared, already gating MCP exposure. |
| Busy visibility | `CommandQueue._running` + `GetQueueStatus`, which deliberately never touches the Revit thread and so answers even while Revit is blocked. |

## The central decision: a node IS a command

#70 splits nodes into *command* nodes and *orchestrator* nodes (Filter, ExportExcel,
ExportPdf). The engine does not get two node kinds. Orchestrator nodes are ordinary
`[RevitCommand]` implementations that simply never call `RunInRevitAsync`.

Note what this does **not** need: a `Headless` flag. Routing is already implicit in whether
a command calls `RunInRevitAsync`, so there is nothing for a dispatcher to decide and no
flag for an author to get wrong. One node kind, no capability plumbing.

Consequences:

- Filter / ExportExcel / ExportPdf live in `AnalyseTool.Tools` as normal vertical-slice
  features, not inside the engine. Core stays the platform, Tools stays the features.
- **Extensions ship nodes for free.** Any third-party `[RevitCommand]` is already a node.
  The "custom nodes ecosystem" from #70's ComfyUI notes exists by construction.

Placement follows the dependency contract: engine + `.atpipe` schema in `AnalyseTool.Core`,
nodes in `AnalyseTool.Tools` and in extensions, `RunPipeline` / `ValidatePipeline` as
ordinary commands.

## The SDK delta is one property

```csharp
[RevitCommand(Description = "…", InputType = typeof(Request), OutputType = typeof(Result))]
```

`OutputType` completes a pair that is currently half-built. Nothing else in the public
contract moves: `IRevitTask`, `IRevitContext`, `RevitPayload` are untouched. SemVer minor,
1.1.2 → 1.2.0; `AssemblyVersion` 1.2.0.0, major unchanged, so extensions built against
1.1.x keep loading.

What was considered and rejected, with the reason — recorded so it is not re-proposed:

| Proposed in #70 | Why not |
| --- | --- |
| `Headless` | Routing is already implicit (see above). The flag decides nothing. |
| `SupportsProgress` | `IProgressAware` is already implemented or not; visible at registration via `typeof`. Duplicating an interface with a flag. |
| `[NodeParam]` | `InputType` + `[Description]` + generated JSON Schema already serves node forms, MCP tool definitions and validation. Defaults come from property initializers, choices from the enum type. Nothing left to add. |
| Execution-mode signal (`Interactive` / `Unattended`) | Introduced to let a command decide whether to swallow or collect warnings. Collecting is strictly better in both modes (interactive UI can show "12 warnings" instead of discarding them), so the mode has no consumer. |
| `RequiresUser` | Exactly three interactive commands exist (`PickFolder`, `BrowseForFolder`, `BrowseForFile`, all in App, all already `HiddenFromMcp`). For now a list in the palette builder, not public contract. Note `HiddenFromMcp` is NOT a substitute — `GetCommands` carries it and is perfectly pipeline-safe. Add the flag when a real third-party case appears. |

Helper code (a shared failure preprocessor, node base classes) deliberately does **not** ship
in the SDK, even though it could — the SDK is already Revit-API-coupled (`IRevitContext`
exposes `UIApplication`; `AnalyseTool.Extension.props` brings `Nice3point.Revit.Api.*`
compile-only). The reason is versioning: authors pin EXACT versions and floating ranges are
banned, so every helper fix would become a contract bump an author must migrate to. Helpers
would need a second package — which is what #75 was rejected for.

## Outputs are the critical path (#89)

No command declares a result type today. `GetCommands` returns `new { commands }`,
`SetDataToParameters` returns `null`, and `McpBridgeServer` advertises `InputSchema` only.
Typed edges therefore mean refactoring the Tools commands slice by slice — the expensive
part of the whole feature, and the reason it ships first and separately: an MCP agent
currently infers response shapes from prose.

**Design issue to solve there, not here:** `BuildInputSchema` caps schemas at 4096 chars and
falls back to `{"type":"object","additionalProperties":true}`. Sensible for a tool listing;
fatal for typed edges, because output types (lists of elements with parameters) will exceed
the cap almost always. So the two consumers of one schema must be split — MCP keeps the
compact form, the graph validator gets the full one — or edges compare a shallow shape
(type name + top-level fields), which is both cheaper and produces a better error message.
This is Core work, not contract work.

Corollary from #70's AI-node notes: an **AI node is easier to type than a command** — its
output schema is node configuration (it doubles as the model's response format and its
validator), so it needs no refactor.

## No modal dialogs — two different problems

### Dialogs Revit raises

`IFailuresPreprocessor` is not suppression; it answers the dialog in code and turns its
content into data. Today it exists only in the Families slice (`SwallowWarningsPreprocessor`,
8 call sites). The two write sites without it — `SetDataToParameters.cs:30` and
`IsolationInRevit.cs:27` — are precisely the bulk-write path a pipeline ends with.
`ExecuteRevitCode` (`Destructive = true`) is uncovered by construction, since the script
author writes the transaction.

**Verified under load** (2026-08-04): a purge pipeline deleted unused family types and then
unused families across ~15 chunked transactions and collected **roughly 340 warnings**, with
**no modal dialog at any point** and the run reporting `Completed`. That is the claim this
whole section rests on, and until this run it had only been tested against a single warning.
An unattended destructive batch is exactly the case where one modal would have frozen the Revit
thread and, with it, every other transport.

The same run exposed a smaller thing worth fixing on sight: both purge commands share
`PurgeChunk`, whose transaction was hardcoded to `"Purge type"`. So all 340 warnings — the
family ones included — were logged under the type wording, and Revit's undo stack labelled the
family deletions the same way. The name now follows what is actually being deleted. A shared
write helper naming its own transaction is a small bug in one command and an ambiguous audit
trail in a pipeline, where two destructive nodes run minutes apart.

Whether a failed node stops the run is node data in `.atpipe`:

```
"onFailure": "stop" | "continue"      // optional; absent → "stop"
```

Decided by the engine from the node's outcome — the command neither knows nor declares it.
Three points of precision, each of which a looser wording got wrong at least once:

- **It applies only when the command THROWS.** A write that lands 488 of 500 has not failed;
  it returned a result carrying warnings, which is #89's business, not this field's.
- **The default is `stop` for every node, not just `Destructive` ones.** A default resolved
  from the command's `Destructive` flag would live in the catalogue rather than the file, so
  the same `.atpipe` could behave differently on two installations. `continue` is an explicit
  opt-in, for nodes whose failure genuinely should not invalidate the run (a trailing export,
  say).
- **Cancellation is not failure and always wins.** `ct.ThrowIfCancellationRequested()` (see
  `PurgeFamilies.cs:40`) surfaces as an exception on the same path as a real failure, so a
  naive `catch (Exception)` consulting `onFailure` would let a `continue` node swallow the
  user's Stop. `OperationCanceledException` is caught FIRST and unconditionally ends the run:

  ```csharp
  catch (OperationCanceledException) { run.Status = Cancelled; break; }  // onFailure not consulted
  catch (Exception ex) { run.Fail(node, ex); if (node.OnFailure == Continue) continue; break; }
  ```

`stopNode` — stop this node, let the rest of the graph proceed — is deliberately NOT in v1:
in a linear pipeline it is indistinguishable from `stop`, since the next node would have no
input. It becomes meaningful only with branching, i.e. not before the editor.

### Stop is not a rollback

Cancellation ends the run; it does not undo it. Stopping between nodes 3 and 4 leaves
everything nodes 1–3 wrote in the model — their transactions are committed. This is the direct
consequence of a run not being atomic, and it has two obligations: the UI says **Stop**, never
"Cancel", and the run receipt records where the run stopped so the user can see what did land.

Cancellation is also cooperative and coarse. `ct` is only observed where a command observes it
(`PurgeFamilies` checks between chunks of 40), and `RevitTaskHub.EnqueueAsync` takes no token
at all — work already handed to the Revit thread runs to completion. A run therefore stops at
the next node boundary, not instantly.

`DeleteAllWarnings()` is right for a button and wrong for a batch: 500 silently discarded
warnings leave no trace. The shared preprocessor **collects and reports**; warnings ride in
the command's declared result type (which #89 introduces anyway), not in a parallel
diagnostics channel.

Author discipline cannot cover third-party commands or Roslyn scripts, so the guarantee wants to be
host-level: a subscription active only during an unattended run
(`ControlledApplication.FailuresProcessing`, plus `UIControlledApplication.DialogBoxShowing` for
generic task dialogs) answering centrally, covering every command including ones we did not write.

Measured in a live Revit (2026-08-04, instrumentation since reverted):

- **The per-transaction preprocessor runs FIRST.** With one attached, the application-level
  `FailuresProcessing` sees an already-cleared accessor (`count=0`). This is what a backstop
  wants — it would not compete with per-transaction handlers, it would pick up only what nobody
  resolved.
- **`FailuresProcessing` is a routine event.** It fires on commits that raised nothing at all,
  on the Revit thread, every time. So anything hung there must be nearly free, and its message
  count says nothing on its own. The same is true of `PreprocessFailures`, which Revit also calls
  on every commit — hence the `messages.Count > 0` guard on the log line in `FailureHandling.cs`.
- **Still open:** the handler has only ever been observed downstream of a preprocessor. Whether it
  sees `count > 0` for a transaction with NO preprocessor — the one case a backstop exists for —
  is unanswered. Until it is, the backstop is a proposal, not a plan (#88).

### Dialogs we raise

An approval node blocks by definition — but never as a modal window. The difference is who
is blocked: a modal blocks the Revit thread and therefore the whole platform; a suspended
run blocks only itself, leaving Revit free and surviving a Revit restart because the state is
in the `.atpipe`. So approval = pause/resume + a card in the inbox (#80).

This is the same rule CLAUDE.md already states for Core and Tools ("headless — no WPF, no
dialogs"). The failure path is simply the one route that bypasses it, because Revit raises
those dialogs from inside our transaction rather than from our code.

## Revit thread: a run interleaves — decided, V1

`RevitTaskHub.Execute` drains its whole queue (`RevitTaskHub.cs:65`), so work raised by a
WebView2 window or an MCP agent runs between node N and node N+1. **Accepted for V1 and
stated in the contract.** Leasing the hub would freeze the UI for the length of a mutating
pipeline and edit the most delicate piece of dispatch in the repo, before a single pipeline
has ever run.

What that obliges instead: a mutating node **re-checks its own preconditions** and may not
assume the state an earlier node observed still holds. The existing idempotency modes are the
mechanism — `SetDataToParameters` already ships `Overwrite` / `OnlyIfEmpty` / `SkipIfEqual`.
Three commands already make multiple Revit round-trips (`PurgeFamilies`, `PurgeFamilyTypes`,
`LibraryService`), so the window exists today; a pipeline stretches it from milliseconds to
minutes, because an AI node may sit in between.

MCP makes this observable but not survivable today: `RevitBridgeClient.InvokeAsync` has **no
timeout** (list has 8s, connect 3s), so an agent that calls into a blocked Revit hangs until
its own client gives up. Bounding it belongs to #88.

## `.atpipe` v1 — the contract

```
{
  "schema": 1,
  "id": "...", "name": "...", "author": "...", "version": "1.0.0",
  "nodes": [
    { "id": "n1", "command": "GetWarningsInRevit", "contract": 1 },
    { "id": "n2", "command": "Filter",
      "params": { "where": [ { "field": "warningDescription", "op": "contains", "value": "overlap" } ] },
      "bind":   { "items": "n1" } },
    { "id": "n3", "command": "IsolationInRevit",
      "bind":   { "elementIds": "n2.items[*].failingElements" },
      "onFailure": "stop" }
  ],
  "edges": [ { "from": "n1", "to": "n2" } ],
  "state":  { ... }
}
```

Reserved from the start even though V1 is linear and synchronous, because retrofitting any of
them is a migration:

1. **`contract` per node** — an old file fails loudly against a newer command instead of
   quietly doing something else.
2. **`state`** — approval nodes suspend a run and resume it later, possibly after a Revit
   restart.
3. **`onFailure` per node** — see above.
4. **Run receipt** — every exported artifact embeds the `.atpipe` that produced it, the
   command contract versions and, for AI nodes, the model. Audit and reproducibility by
   construction (the ComfyUI lesson from #70).

### `bind` — how data reaches a node

`params` carries the literal payload; `bind` maps a payload property to `"<nodeId>"` or
`"<nodeId>.<path>"`, and wins over a literal of the same name (a leftover literal must not
survive connecting the node). Explicit rather than merging an upstream result by name: commands
take specific shapes, collisions between unrelated nodes would be silent, and the graph validator
can check an explicit binding against the two declared schemas (#89) while it cannot check a
convention. An unresolvable binding fails the node instead of binding null — a pipeline quietly
passing null into a mutating command is the failure worth being loud about.

A **wildcard** path (`items[*].failingElements`) collects every match, splicing matches that are
themselves arrays into one flat list. Without it a binding reaches only item `[0]`, so a pipeline
that filters a list could act on just its first row — which is precisely what the first real run
of a filter-then-isolate pipeline hit.

**Nothing collected is two different answers, and conflating them was wrong in both directions.**
A wildcard that matched nothing used to fail, on the reasoning that an empty list would let a
mutating command report a cheerful "0 written" over a shape that does not exist. That reasoning
holds only for a wrong shape. Run "purge unused families" a second time and the filter keeps no
rows — the path is right, the model simply has nothing left to purge, and *finding nothing to do
is the answer*, not a broken pipeline. Both purge nodes reported `Failed` on a model that was
already clean.

So the path is walked one step at a time when the collection comes back empty:

* a wildcard over a list that **exists and is empty** → an empty list, and the run continues;
* a step naming something that **is not in what precedes it** → a failure that names the step and
  lists the keys that *are* there (`found no 'id' … what is there: typeId, name`), because
  "matched nothing" never said which of the path's three parts was the wrong one.

Passing an empty list into a purge is safe by construction: `PlanPurge*` intersects the requested
ids with what the document actually holds, so an empty request plans nothing. That property is
what makes the relaxation defensible — it is not a general licence to bind empty lists into
mutating commands.

### A schema that lied, and the silence that hid it

The first pipeline an agent wrote unprompted read 187 family types, ran them through a `Filter`,
and purged the result. Its conditions never took effect: they were written as a key **on the
node** rather than inside `params`, the deserializer dropped them, and the Filter passed all 187
types through to `PurgeFamilyTypes`. Only an unrelated typo in a binding stopped it.

Four separate defects had to line up, and each one is worth naming because each is a rule:

0. **A save writes the author's bytes, not our model of them.** `SavePipeline` wrote
   `SerializeObject(doc)`, so every key `PipelineDocument` does not declare was erased on the way to
   disk. The conditions did not merely fail to take effect — they stopped existing, and the file
   left for review was a lossy reconstruction that no longer contained the mistake. This is what
   made the other three undiagnosable: the evidence was destroyed by the act of saving it. Parse to
   *check*; write the original text, re-indented.


1. **Free-form properties must not be declared as Newtonsoft types.** `JToken`, `JObject` and
   `JArray` all enumerate as `JToken`, so a reflection-based schema generator reads them as "an
   array of JToken" — of "an array of JToken" — and publishes a `$ref` pointing at itself with
   `type: ["array", "null"]`. `Filter` therefore advertised a `value` that *cannot be a scalar*:
   `{ "op": "equals", "value": 0 }` was rejected by the wire, and `[0]` passed but matched
   nothing. The agent could not express a condition against the contract we had published. Declare
   `object` instead — it publishes the permissive schema we actually mean, and Newtonsoft still
   hands back `JObject`/`JValue` at run time. This hit five commands, input and output alike.
2. **Unknown keys must survive deserialization.** A dropped key cannot be reported by anything
   downstream, because by the time the validator or the run looks, the author's mistake no longer
   exists. `PipelineNode` and `PipelineDocument` now collect them with `[JsonExtensionData]`, and
   validation warns — naming the key, and saying "move it into `params`" when the command declares
   it as a payload property. Collected rather than refused, so a file written against a later
   build still loads; only `schema` refuses outright.
3. **The graph validator is where knowledge about danger lives.** A `Filter` with no conditions
   passing everything through is correct behaviour for a half-built pipeline and catastrophic in
   front of a purge; the node cannot see what it feeds, and the validator can. That check is an
   error, not a warning, because warnings do not stop a run.

4. **A read command must not answer for something that is not there.** `GetFamilyTypes` returned a
   well-formed `{ familyId, name: "", category: "", types: [] }` when the id resolved to no family
   — indistinguishable, downstream, from a family that genuinely has no types. In a pipeline that
   difference is the whole story: a purge reading those types deleted nothing and reported success,
   while the real cause was that a purge one step earlier had deleted the families being asked
   about. It throws now, and says so in that many words. An empty result is a claim about the
   model; make it only when it is true.

The pattern behind all four: a pipeline is increasingly authored by something that reads only the
schema and the description. Every place where the published contract disagrees with the real one
becomes a file that looks right and does something else.

### No result cache, and why

The plan called for caching `ReadOnly` node results by an (inputs + params) hash. It was written
and dropped. In a linear V1 each node runs once, so repeating a read with an identical payload
inside one run essentially never happens; the value lives entirely in the cross-run case — edit
the last node, re-run, skip the expensive read — which cannot be done safely without a change
signal to invalidate on. A remembered `GetElements` replayed into a mutating pipeline an hour
later writes against element ids that may be gone, and it looks like a Revit bug rather than a
stale cache. Revisit with the change journal (#80); it will want to be persistent and keyed on
model state anyway, not a dictionary in the dispatcher.

Files live in `%LOCALAPPDATA%\AnalyseTool\pipelines\` via `PathProvider`, plus explicit
export/import. Sharing is a file over Teams or email — no server, same stance as #48.

## Engine (#90)

- `IPipelineEngine`, `INodeDispatcher`, status events
  (Queued → Executing → Completed / Failed / Cancelled).
- `LocalDispatcher : INodeDispatcher` over `CoreServices.Queue` — busy bar, `GetQueueStatus`
  and the confirmation gate come along for free.
- Linear execution; `CancellationToken` through the run, caught ahead of `onFailure` so Stop
  can never be disabled by a node's policy (see "No modal dialogs" above).
- **Revit-free by construction and tested that way** — `AnalyseTool.Core.Tests`, which references
  Core and nothing of the host. (`AnalyseTool.Test` could not host these: it references
  `AnalyseTool.App` and so drags in Revit, and it currently fails at discovery because its
  in-Revit harness cannot inject.)
- Commands: `RunPipeline`, `ValidatePipeline`, `SavePipeline`, `ListPipelines`. `Filter` is the
  first orchestrator node — an ordinary `[RevitCommand]` in Tools that never calls
  `RunInRevitAsync`, which is the whole reason no capability flag was needed.

**Verified live** (2026-08-04): read warnings → filter → isolate, saved by `SavePipeline` and run
by name, completed with four elements isolated. No editor involved, which was the claim.

### Who may run, and who may only propose

`SavePipeline` is exposed to MCP; `RunPipeline` is **not**. The asymmetry is the design: saving
executes nothing and leaves a file a human can read, so an agent may propose a pipeline while only
a person starts one — the same division the approval gate makes inside a run.

`RunPipeline` stays hidden for a concrete reason, not caution: the bridge decides per COMMAND what
an agent may invoke, and nodes dispatch under `RunPipeline`'s own identity, so an agent able to
call it with an inline document would reach commands it is refused directly. Exposing it means
carrying the caller's policy into every node (`CommandRequest.Gate` is the hook) — work, not a
flag flip.

## The gate: before the editor, not before the cloud

#70 puts its only gate before Step 2 (cloud), which is too late — the node editor, the most
expensive and least certain part, is built before anything has to be proven.

**The gate moves to before #91.** Everything up to and including the runner is justified on
its own: #88 and #89 fix today's platform, and #90 is a JSON format plus a linear executor
that gives MCP something it lacks — a way to freeze a successful chain and replay it without
an LLM.

The editor is the only piece that adds a new consumer surface (canvas, palette, ports, graph
validation, a frontend dependency). Two observations from #70's own comment argue it may
never be needed:

- the spaghetti threshold — mass users stayed on simple forms, so pre-baked pipelines are
  surfaced as ordinary panels: "button on top, graph underneath";
- "the agent did it in five steps in chat → *save as pipeline*".

If authoring belongs to the agent and consumption is a button, **neither side needs a node
editor**: a pipeline is a format plus a runner. Build #91 only if `.atpipe` files authored
that way actually start circulating between people and their authors ask to edit them by
hand. Until then, nothing is spent on a canvas.

The gate is now **reachable**, which it was not while nothing could write a `.atpipe`: with
`SavePipeline`, an agent can author one, a person can run it, and the file can travel. A gate
whose condition nobody can meet is not a gate, it is a shelf — and until that command existed,
the question the editor decision hangs on could not have been answered either way.

## Two surfaces, and why they are not one

Running and building want opposite shapes, so they get different homes.

**The runner is docked** (`#/pipelines-dock`, ribbon "Pipelines"). It sits beside the model while
a run reports node by node, which is precisely what a pane docked next to the Project Browser is
for. It is a list, a Run button, live progress and the run receipt — every line of it an
`AT.invoke` of a command that already existed, with no pipeline logic of its own.

It also closes a hole the platform had left open. `RunPipeline` is `HiddenFromMcp` so that an
agent may propose a pipeline and only a person starts one — but the pre-execution `Gate` on
`CommandRequest` is set **only** by the MCP bridge, so nothing asked the person anything. A Run
button without a confirmation would have quietly undone that decision. So `ValidatePipeline` now
also returns `destructiveNodes`, and the panel confirms **once per run**, naming them. Once, not
per node: a prompt on every write turns into something people click through, and the run is the
unit the user actually chose.

**The editor is a separate window**, like Family Manager. A canvas is unusable in a pane, and the
two surfaces share nothing but the command catalogue.

## Editor (#91) — the gate opened

The gate's condition was that authors ask to edit `.atpipe` files by hand. The repo's author
asked, so it was built; the condition is not re-litigated here.

Vue Flow (MIT) as a new dependency — `src/view/InfiniteCanvas` is a pan/zoom canvas of cards
(`useCanvas`, `useDrag`, `useCanvasPersistence`, `CanvasCard`) with no edges, ports or connection
model, so reusing it would mean writing a graph library inside a dashboard. It lands in the
editor's own lazy chunk, which is why the dock pane's bundle is unaffected.

Palette from the live command catalogue (`GetCommands`), so an installed extension's commands are
in it without the editor knowing anything about extensions. Parameter forms from the declared
`inputSchema` — a command that declares its input gets a form for free, which is #89 paying for
itself a second time. Validation is inline (`ValidatePipeline` with the draft document), so a
pipeline is checked before it is ever written to disk.

Three decisions worth keeping:

- **Run order is visible and editable.** V1 executes nodes in FILE ORDER; `edges` is lineage the
  validator checks, not a scheduler. So every card carries its run number and the inspector can
  move a node earlier or later. A canvas that hid this would let someone arrange a convincing
  picture whose run bore no relation to it — worse than no canvas.
- **The document is the single source of truth.** Vue Flow's nodes and edges are computed from
  it, never kept beside it. Two models of one graph is how an editor starts saving something
  other than what it shows.
- **A field is a literal or a wire, never both.** A binding wins over a literal of the same name,
  so offering both at once would misrepresent what actually runs.

### Node ids are read far more often than they are written

A node id is not decoration: it is what a binding names (`filter2.items[*].id`) and what the run
receipt reports. Generating it from the command covers most nodes — `purgeFamilies` says what it
is — and fails exactly where it matters most. A `Filter` has no shape of its own, so `filter` and
`filter2` describe nothing, and the purge pipeline has two of them: one over families, one over
family types. Picking the wrong one deletes 286 families instead of two.

So a node that hands its input through unchanged is named after **where its data came from**, with
the source's leading verb dropped: `filterFamilies`, `filterFamilyTypeRows`. "Hands its input
through" is not a hard-coded list of commands — it is a declared output array whose items are
undeclared, the same signal `effectiveOutput` already uses to resolve pass-through rows.

The id stays **generated and read-only**. It is a reference key, not a caption: everything that
reads it — a binding, the validator, the run receipt — reads it as an identity, and an editable
one buys a naming preference at the cost of a class of silent breakage. If a generated name is
unclear, the fix belongs in the rule above, where it fixes every pipeline rather than one.

Node positions live in the node's own `ui` key, declared on `PipelineNode` so it does not read as
an unrecognised key. A sidecar layout file would be separated from the pipeline the first time
someone mails one; the runner ignores `ui`, and a pipeline written by hand or by an agent simply
has none.

Renaming a node rewrites the bindings and edges that referenced it — otherwise a rename silently
unwires the graph. Deleting one does **not** rewrite the bindings that pointed at it: that would
hide the breakage, and validation names it in a sentence instead.

The editor opens from the runner ("Edit" / "New"), not from its own ribbon button — that is where
a person already is when they want to change a pipeline. A WebView cannot open a WPF window, so
the panel asks the host through `OpenPipelineEditor`, the same reason `PickFolder` exists.

## AI nodes (#92)

Three kinds — transformation (output constrained by a schema set in the node config), router
(semantic branching), bounded agent node (own tool allowlist, step budget, goal). Model per
node from `AiProviderRegistry`; item batching is node infrastructure; provenance ("AI decided,
confidence, why") rides with each item into the approval card and into project memory (#80).

The invariant is a property of the graph, checked when it is built: **an edge from an AI node
may not reach a `Destructive` command directly — an approval node must sit between them.**
"AI never writes to the model without a human" becomes topology, not user discipline.

### Approval is a filter, not a barrier

Stated as a barrier — the run halts until someone clicks — the invariant would kill the point
of a pipeline: press and forget. So the approval node does not stop the run. Items that pass a
machine-checkable acceptance predicate flow through and are written; the rest are parked as a
card in the inbox (#80) and **the run completes**. You come back to 400 renamed families and 12
questions, not to a pipeline standing still on node 3.

The predicate lives on the node, in the `.atpipe` — versioned, reviewable, travelling with the
pipeline — and NOT as a "trust the AI" setting. A global toggle is a global answer to a
per-case question: ticked once in a confident moment, it silently covers every future run.

```json
{ "id": "n3", "command": "Approval", "params": {
    "autoAccept": {
      "valueMatches": "^[A-Z]{2}_[A-Za-z0-9]+_\\d{3}$",
      "maxItems": 50,
      "minConfidence": 0.9
    } } }
```

The three conditions are NOT of equal strength, and treating them as interchangeable is the
trap:

| Condition | Checks | Strength |
| --- | --- | --- |
| `valueMatches` / allowed value set | the value itself, against a rule | **strong** — verified against reality |
| `maxItems` | blast radius | medium — bounds damage, not correctness |
| `minConfidence` | what the model said about itself | **weak** |

An LLM's self-reported confidence is not calibrated, and it is least reliable exactly on the
atypical cases. It is a tiebreaker, never a sole criterion. A naming standard, by contrast, is
already a regex — so "the AI proposed a name, it matches the standard, fewer than 50 items" is
a fully mechanical check that needs no human at all.

Three levels decide whether `autoAccept` is honoured:

1. **`Destructive = true` → auto-accept off by default.** The attribute sets the safe default.
2. **A node may declare `autoAccept` explicitly** — the opt-in, visible in the file.
3. **A short list where `autoAccept` is refused even when declared**: the irreversible ones —
   `DeleteFamilyElements`, `PurgeFamilies`, `PurgeFamilyTypes` — plus `ExecuteRevitCode`, about
   which nothing can be reasoned. Here the gate is a barrier at any setting, because the cost of
   a mistake does not scale down with `maxItems`.

Level 3 is an **inverted allowlist**: loosening is permitted only for commands the host knows
are reversible, so every unknown command — including every third-party one — stays a barrier.
That covers extensions without adding an SDK property, on the same reasoning that deferred
`RequiresUser`.

Note the flag this hangs on had to be repaired first: `Destructive` was applied to three
extension-management commands and to parameter writes, but **not** to `DeleteFamilyElements`,
`PurgeFamilies`, `PurgeFamilyTypes`, `RenameFamily`, `RenameFamilyType`, `SetInstancesWorkset`
or `LoadLibraryFamilies` — so the rule would have gated the harmless case and waved the
irreversible one through. Fixed in #88.

### Earning trust instead of declaring it

A run-level `dryRun` flag: AI nodes execute for real, `Destructive` nodes report what they
would have done and write nothing. The first run of a new pipeline is dry, you read the diff,
then you drop the flag. More expensive than a checkbox — every mutating command needs a no-op
mode — but `SetDataToParameters` already carries `Overwrite`/`OnlyIfEmpty`/`SkipIfEqual`, so a
fourth mode is a natural fit rather than a new concept.

### Where the model comes from — and why not over MCP

An AI node calls `AiClientFactory.Create(providerId, model)` and gets an `IChatClient`. That
already covers the cloud case: `AiProviderRegistry` holds the built-in local Ollama **plus any
number of user-added OpenAI-compatible endpoints** (OpenAI, OpenRouter, Groq, Mistral, LM
Studio, vLLM), with the API key DPAPI-encrypted per Windows user and never handed to the
frontend. So "AI nodes require a strong local machine" is not true today — the user brings a
key, picks a cheap model for classification and a strong one for an agent node, per node.

Reusing the existing MCP connection to a chat client instead looks tempting and does not work,
for a reason of direction rather than effort. **MCP is inverted relative to what a node needs.**
Over MCP the AI client is the caller and AnalyseTool is the tool provider; an AI node needs to
BE the caller. The protocol does have a feature for this (sampling — the server asks the client
for a completion), but our transport cannot express it: `RevitBridgeClient` is connect-per-
request TCP into the bridge, one request and one response per connection, with no persistent
socket and no id-correlation, so the bridge has no way to initiate anything toward the client.
Supporting sampling would mean a bidirectional rewrite of the wire protocol.

Even granted that rewrite, it would be the wrong dependency: the pipeline would only run while
the user's chat app is open and connected, which defeats the point of a repeatable run started
from the ribbon, and 25–50 batched calls over a 500-element model would go through that app's
quota and consent prompts instead of the user's own predictable API billing.

The agent's real role here is the other end: **authoring** pipelines ("five steps in chat →
save as pipeline") and **invoking** `RunPipeline`. Agent → pipeline → deterministic nodes with
AI islands is the composition that works; agent-as-the-model-inside-a-node is the one that
does not.

## Microsoft Agent Framework: not in V1

An external dependency inside `AnalyseTool.Core`, which loads into an ALC in the Revit process
and multi-targets net8/net10; dependency conflicts there are not hypothetical. `.atpipe` stays
our contract either way — MAF would only ever be an interpreter of it — so adopting it later
costs nothing that adopting it now would save. Re-evaluate at #92, where checkpointing and
human-in-the-loop stop being anticipated and become required.

## Phases

**Platform debt — do regardless of pipelines:**

| | Scope |
| --- | --- |
| #88 | Write-safety: collecting failure policy for all slices, host-level backstop for third-party/script transactions, MCP invoke timeout |
| #89 | `OutputType` + declared result types per slice; split the schema's two consumers |

**Pipelines proper:**

| | Scope |
| --- | --- |
| #90 | `.atpipe` v1 + engine + `RunPipeline`, headless, Revit-free tests |
| — | **GATE**: are agent-authored `.atpipe` files circulating, and is hand-editing asked for? |
| #91 | Node editor (Vue Flow), palette shared with #43 |
| #92 | AI nodes, approval invariant, pause/resume |

## Out of scope (deliberately)

- Branching in V1.
- Step 2 (sessions, SignalR, distributed execution) — unchanged in #70, still gated on a pilot.
- Scheduled triggers. Change Journal triggers (#80) are the more valuable kind and belong to
  that feature's timeline.
- Licensing/gating of nodes (#72).
