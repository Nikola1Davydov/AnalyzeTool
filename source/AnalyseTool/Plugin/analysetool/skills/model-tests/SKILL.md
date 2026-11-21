---
name: model-tests
description: Write, run and act on AnalyseTool model tests — checks of the open Revit model written as C# (rules on parameters, code per element, tests of the whole model), like unit tests for a BIM model. Use when the user wants to check, validate or audit a Revit model against rules (a BEP, naming, required parameters, clashes), asks what fails in the model, or mentions model tests, the Tests tab, a test suite, a baseline or accepting failures.
---

# AnalyseTool model tests

A **suite** is a C# class implementing `IModelTestSuite` (`AnalyseTool.Sdk.Testing`) in a folder of model tests: its
name ends in `.tests` (`bep-project-x.tests\src\*.cs`). Suites in any other folder do not load.
A **folder** is a project: one extension with one or more suites, shown as a folder in the **Tests** tab of the
AnalyseTool pane. AnalyseTool compiles the folder, calls `Define` on load, and runs the tests in a transaction group
that is always rolled back — **tests never change the model**; only a repair (`Fix`) the user asks for writes.

Read section **11. Model tests** of the guide (`GetAuthoringGuide`) before writing a suite: it is the contract.

## Write

1. **Learn the model first**: `GetModelOverview` (language, units, categories by `builtInCategory`, counts), then
   `GetCategoryParameters { builtInCategory }` for the parameters a rule will read. Test what exists — a rule on an
   empty category proves nothing.
2. **New project**: `CreateModelTestSuite { name }` → a folder `<id>.tests` with an example suite. **One more suite in a
   project**: `CreateModelTestSuite { name, extension: "<folder id>" }`. Existing ones: `GetModelTests`.
3. Write the suite with `WriteExtensionFile { id, path: "src/<Class>.cs", content }`; the result carries compiler
   errors with file and line — fix until there are none.

```csharp
using AnalyseTool.Sdk.Testing;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

public sealed class BepProjectX : IModelTestSuite
{
    public void Define(ISuite s)
    {
        s.Name("BEP Project X").Trait("Discipline", "ARC");

        s.Elements(BuiltInCategory.OST_Walls)                       // a rule: one value per element
         .Parameter(BuiltInParameter.ALL_MODEL_MARK).NotEmpty()
         .Name("Walls have a mark");

        s.Elements(BuiltInCategory.OST_Doors)
         .Parameter("Fire Rating").OneOf("EI30", "EI60").FixValue("EI30");

        s.Elements<Room>().Where(room => room.Area > 0)
         .Parameter(BuiltInParameter.ROOM_NUMBER).NotEmpty().Unique();

        s.Elements(BuiltInCategory.OST_Doors).Check("Door has a host wall", (door, m) =>   // code per element
            m.Assert.True(((FamilyInstance)door).Host is Wall, "The door has no host wall"));

        s.Test("No unplaced rooms", m =>                              // code for the whole model
            m.Assert.Empty(m.Elements<Room>().Where(room => room.Location == null), "The room is not placed"));
    }
}
```

Rules of thumb:

- **`BuiltInCategory` / `BuiltInParameter` over names**: names depend on Revit's language («Mark» is «Kennzeichen»).
  A shared or project parameter has no built-in id — use its name exactly as `GetCategoryParameters` lists it.
- Conditions: `NotEmpty`, `Equal`, `Matches(regex)`, `OneOf`, `InRange(min, max)` (in Revit's internal units — lengths in feet: `InRange(100.Mm(), 20000.Mm())`, areas `1.M2()`, volumes `0.5.M3()`, angles `45.Deg()` — never bare numbers),
  `Unique`. Options last: `.Name`, `.Id`, `.Severity`, `.Trait`, `.Slow()`, `.Skip(reason)`.
- Give every test a readable `.Name(...)`; give a stable `.Id(...)` to a test whose rule may change — accepted
  failures are kept by id.
- `Define` only declares: no model access there. Collect inside the lambdas (`m.Document`, `m.Elements(...)`).
- Geometry is plain Revit API in a `Check`; pass `new Evidence { Related = [...], Box = outline }` so the page can
  draw it, `m.Assert.Inconclusive(...)` when Revit cannot tell, and mark it `.Slow()`.
- Lengths in the API are feet: `900.Mm()`, `0.9.M()`.

## Run and report

- `RunModelTests { suite: "<folder or suite id>" }` — or `tests: [...]`, `trait: "Discipline=ARC"`,
  `elementIds: [...]`, `includeSlow: true`. Without filters every non-slow test runs and the counts join the trend.
- Status: `passed` = nothing **new** failed (accepted failures do not count); `failed`; `error` = **the test threw —
  a bug in the test, fix the code**, not the model; `inconclusive`; `skipped`.
- Report to the user per test: what failed, how many elements, two or three examples with their `label` and `value`
  (`valueState`: `empty`, `missing` — no such parameter, `set`). Not a dump of every case.
- Show them in Revit when it helps: `ShowElementsIn3D { elementIds }` (isolated in the active view; the view does not change) or
  `{ elementIds, sectionBox: true }` (a section box around them); `ShowGeometry` for evidence boxes, `ClearGeometry`
  after. A view in the **Realistic** display style does not show what `ShowGeometry` (and Highlight) draws: when the
  user sees nothing, ask them to switch the view to Shaded or Consistent Colors.

## Act — ask first

- **Repair**: `FixModelTestCases { testId, keys? }` runs the test's `Fix` / `FixValue` in one transaction (one
  Ctrl+Z). It changes the model: say what it will write on how many elements and wait for a yes. Run again after.
- **Accept** (baseline): `AcceptModelTestFailures { testId, keys? }` hides those failures from now on — only new
  ones fail. It is the way to start on an existing model, but it hides problems: **always ask**. `remove: true`
  takes them back.
- When a test is wrong (fails on what is fine, `error`), fix the test, not the model. After a change that works,
  `SaveExtensionVersion { id, message }`.
