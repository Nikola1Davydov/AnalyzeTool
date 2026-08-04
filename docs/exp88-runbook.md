# EXP88 — runbook (temporary, delete with the instrumentation)

Two questions in #88 cannot be answered by reading code; they need a live Revit. This file and the
instrumentation it drives are one commit — `git revert` it once the answers are written into #88.

Everything the probe logs goes to `%LOCALAPPDATA%\AnalyseTool\logs\analysetool-<date>.log` at
Warning level, prefixed `EXP88`. The whole readout is:

```powershell
Select-String EXP88 "$env:LOCALAPPDATA\AnalyseTool\logs\analysetool-*.log"
```

## Setup

```powershell
powershell -File src/build/Check-Boundaries.ps1
dotnet build src/AnalyseTool.Launcher/AnalyseTool.Launcher.csproj -c "Debug R25"   # Revit closed
```

Then in Revit: Settings → enable C# code execution (off by default; `ExecuteRevitCode` refuses to
run otherwise). Open any project with at least two walls.

## The provocation

A transaction **we do not control**, raising a warning Revit would normally show as a modal. Run it
through `ExecuteRevitCode` — that is the point: a Roslyn snippet is the case no helper of ours can
cover, exactly like a third-party command.

**Duplicate `Mark` on two elements.** Revit answers with "Elements have duplicate 'Mark' values",
severity Warning, so the transaction still commits. Confirmed through the API on 2026-08-04:
`SetDataToParameters` reached its preprocessor with 1 message.

Take the ids from a category the model actually has, and read the `Mark` parameter id from
`GetCategoryParameters` rather than hardcoding a `BuiltInParameter` number. If a run produces zero
messages, the write did not land — check `written`/`skipped` before blaming the provocation. (That
happened once here and was briefly mis-recorded as "the API cannot raise this warning". It can.)

```csharp
var elems = new FilteredElementCollector(doc)
    .OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType()
    .Take(2).ToList();

using (var t = new Transaction(doc, "EXP88 warn-test"))
{
    t.Start();                                  // deliberately NO failure preprocessor
    foreach (var e in elems)
        e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("EXP88-DUP");
    t.Commit();
}
return elems.Count;
```

Undo afterwards so the marks are not left behind.

If for some reason that raises nothing in your model, the fallback is two identical walls in the
same place (`Wall.Create` twice with the same line, type and level) — "There are identical instances
in the same place". That one leaves junk geometry, so undo it too.

Invoke it from the WebView2 devtools console of the plugin window:

```js
await AT.invoke("ExecuteRevitCode", { code: "<the snippet above>" })
```

## Measured so far (2026-08-04)

- **The preprocessor from `b8f2156` works.** A duplicate-Mark write through `SetDataToParameters`
  logged `Resolved failures in transaction 'Set data to parameters': 1 message(s)` and **no modal
  appeared**.
- **Ordering: the per-transaction preprocessor runs FIRST.** The application-level
  `FailuresProcessing` then saw `count=0` — an accessor already cleared. Good news for the backstop:
  it would not compete with per-transaction handlers, it picks up only what nobody resolved, which
  is exactly the role it is wanted for.
- **`FailuresProcessing` is a ROUTINE event** — it fires on commits that raised nothing at all, with
  `count=0`. So its count means nothing on its own, and anything hung on it runs on the Revit thread
  (`thread=1`) on every single transaction and must be nearly free.
- `PreprocessFailures` is likewise called on every commit, warnings or not.

Still open: the application-level handler has only ever been observed DOWNSTREAM of a preprocessor.
Whether it sees `count > 0` for a transaction with no preprocessor at all — the case the backstop
exists for — is what Q1 below still has to answer.

## Q1 — what does an application-level handler see?

Run the provocation with `ANALYSETOOL_EXP88_RESOLVE` **unset**. Record:

- [ ] Did `EXP88 FailuresProcessing` appear at all? With which transaction name and severities?
- [ ] Did `EXP88 DialogBoxShowing` appear? Which `argsType` — `TaskDialogShowingEventArgs`,
      `MessageBoxShowingEventArgs`, or something else? (This decides how a dialog could be answered.)
- [ ] Did the modal appear on screen anyway?

Then restart Revit with `ANALYSETOOL_EXP88_RESOLVE=1` and run it again:

- [ ] Did `deleted the warnings from the APPLICATION-level handler` appear?
- [ ] Was the modal suppressed this time?
- [ ] Did the transaction still commit (the two walls carry `EXP88-DUP`)?

Finally, ordering. Run a variant of the snippet that DOES attach its own preprocessor
(`SwallowWarningsPreprocessor.Apply(t)` cannot be referenced from a snippet — inline a tiny
`IFailuresPreprocessor` that logs and returns `Continue`):

- [ ] Do both handlers fire, or only one? In which order?

**What the answer decides:** if the application-level handler sees and can resolve failures from a
foreign transaction, the unattended backstop in #88 is a host guarantee. If it only observes, or
does not fire at all, that design does not exist and #88 needs a different answer.

## Q2 — can `RevitTaskHub.Execute` be re-entered?

Needs the modal to actually stay on screen, so run with `ANALYSETOOL_EXP88_RESOLVE` **unset**.

1. Fire the provocation.
2. While the warning dialog is up, trigger other work from a **different** transport — click a
   button in the AnalyseTool panel, or have an MCP agent call `GetElements`. That enqueues into the
   hub and raises the ExternalEvent.
3. Record:
   - [ ] Did `EXP88: RevitTaskHub.Execute RE-ENTERED` appear?
   - [ ] Does `GetQueueStatus` report `waitingForUser: true` while the dialog is up?
   - [ ] Does `pendingRevitWork` ever go negative or stay stuck after the dialog is dismissed?

**What the answer decides:** a `depth > 1` line means a re-entrancy guard is needed and where. No
such line, in a session that definitely sat on a modal, means the guard can be dropped from #88 with
evidence instead of left as a "probably fine".

## Also worth checking while Revit is open (regression on `b8f2156`)

| Check | Call | Expect |
| --- | --- | --- |
| Warnings collected, no modal | `SetDataToParameters` writing the same Mark to two walls | no dialog; `{ ok, written: 2, skipped: 0, warnings: [...] }` |
| Skips counted | one valid item + one with `elementId: 999999999` | `written: 1, skipped: 1` |
| Modes intact | `mode: "OnlyIfEmpty"` onto a filled parameter | `written: 0, skipped: 1` |
| Isolate result shape | `IsolationInRevit` with ids, then with `[]` | `{ ok: true, isolated: N }` / `{ ok: true, isolated: 0 }` |
| Flag repair | `GetCommands`, or Settings → Commands | `destructive: true` on the seven Families commands + `PlaceFamilyInstance` |
| UI unbroken | apply in DataTable and Connect Parameters; isolate from the chart and the family palette | unchanged |

Get the `Mark` parameter id from `GetCategoryParameters` rather than hardcoding a
`BuiltInParameter` number.

## MCP timeout, without waiting ten minutes

Temporarily set `InvokeTimeout` in `RevitBridgeClient` to 30 seconds, open any modal in Revit (the
provocation, or File → Options), and have an agent call `GetElements`.

- [ ] A clear error naming a blocked Revit and pointing at `GetQueueStatus`, not silence.
- [ ] `GetQueueStatus` answers throughout with `waitingForUser: true` (it never touches the Revit
      thread — that is the whole reason it exists).

Restore the 10 minutes afterwards.
