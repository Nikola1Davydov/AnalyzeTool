# EXP88 — runbook (temporary; revert once the answers are in #88)

Two questions in #88 are still unmeasured. Everything else this instrumentation established already
moved into `docs/pipeline-design.md` §"No modal dialogs" — don't re-measure it.

The probe logs at Warning level under an `EXP88` prefix:

```powershell
Select-String EXP88 "$env:LOCALAPPDATA\AnalyseTool\logs\analysetool-*.log"
```

## Setup

```powershell
dotnet build src/AnalyseTool.Launcher/AnalyseTool.Launcher.csproj -c "Debug R25"   # Revit closed
```

In Revit: Settings → enable C# code execution (off by default). Open a project with a couple of
walls. The plugin's devtools console opens by itself in Debug (`MainWindow.xaml.cs`), and that is
where `AT.invoke` lives.

## The provocation — a transaction WE DO NOT CONTROL

That is the whole point: every transaction in Tools now attaches our preprocessor, so the only way
to reach an unguarded one is code we did not write. A Roslyn snippet stands in for a third-party
command.

```js
await AT.invoke("ExecuteRevitCode", { code: `
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
` })
```

Duplicate `Mark` is confirmed to raise a warning through the API (measured 2026-08-04 via
`SetDataToParameters`: one resolved message). Undo afterwards. If a run reports zero messages the
write did not land — check the return value before blaming the provocation.

## Q1 — does the host see a transaction with no preprocessor?

Run with `ANALYSETOOL_EXP88_RESOLVE` unset. Record:

- [ ] `EXP88 FailuresProcessing: transaction='EXP88 warn-test' count=?` — is count > 0?
- [ ] Did the modal appear? (The probe only watches by default, so it should.)
- [ ] Did `EXP88 DialogBoxShowing` fire, and with which `argsType`?

Then restart Revit with `ANALYSETOOL_EXP88_RESOLVE=1` and run it again:

- [ ] Does `deleted the warnings from the APPLICATION-level handler` appear?
- [ ] Was the modal suppressed this time?

**What it decides.** `count > 0` plus working suppression ⇒ the host-level backstop exists, and
unattended safety stops depending on third-party authors remembering a preprocessor. Otherwise it is
impossible in this form and #88 needs a different answer — equally worth knowing, since the design
doc currently calls the backstop a proposal precisely because this run has not happened.

## Q2 — can `RevitTaskHub.Execute` be re-entered?

Needs the modal to stay up, so run with `ANALYSETOOL_EXP88_RESOLVE` unset.

1. Fire the provocation.
2. **While the dialog is on screen**, trigger work from another transport — click a button in the
   AnalyseTool panel, or have an MCP agent call `GetElements`.
3. Record:
   - [ ] `EXP88: RevitTaskHub.Execute RE-ENTERED` — did it appear?
   - [ ] Does `GetQueueStatus` report `waitingForUser: true` while the dialog is up?
   - [ ] Does `pendingRevitWork` go negative, or stay stuck after the dialog is dismissed?

**What it decides.** A `depth > 1` line means a re-entrancy guard is needed, and where. No such line,
in a session that definitely sat on a modal, means it can be dropped from #88 with evidence rather
than left as a "probably fine".

## Afterwards

`git revert` the commit that restored this, and put the answers into #88 and into
`docs/pipeline-design.md` — the findings outlive the scaffolding, the scaffolding does not.
