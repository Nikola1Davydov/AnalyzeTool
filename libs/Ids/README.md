# IdsSharp

A small, MIT-licensed .NET reader and evaluator for **buildingSMART IDS** (Information Delivery
Specification) files.

No IFC toolkit. No CAD dependency. You describe your model through one small interface and the
specifications are checked against it — so the same rules can answer *"does the authoring model
satisfy this?"* today and *"does the exported IFC satisfy it?"* later, without the checking logic
knowing the difference.

```csharp
IdsDocument ids = IdsParser.ParseFile("project.ids");

IReadOnlyList<SpecificationResult> results = IdsEvaluator.Evaluate(ids, myEntities);

foreach (SpecificationResult specification in results)
foreach (RequirementResult result in specification.Results.Where(r => r.Kind == ResultKind.Failed))
    Console.WriteLine($"{result.Entity.Label}: {result.Reason}");
```

## The interface you implement

```csharp
public interface IModelEntity
{
    string  Id { get; }                 // your own handle, carried into results untouched
    string  Label { get; }              // for reports
    string  IfcClass { get; }           // "IFCWALL"
    string? PredefinedType { get; }

    bool TryGetAttribute(string name, out string? value);
    bool TryGetProperty(string propertySet, string name, out string? value);

    IEnumerable<KeyValuePair<string, string>> EnumerateProperties();
    IEnumerable<string> EnumerateAttributeNames();
}
```

Enumeration exists because a facet may ask for *any property matching `^Fire.*` in any Pset*, which
no lookup by exact name can serve. If enumeration is expensive for your model, return nothing —
those facets are then reported as **not checked**, never as passed.

## What it does not pretend

**A facet this library does not evaluate is never reported as satisfied.** `classification`,
`material` and `partOf` are parsed, kept, and returned as `ResultKind.NotChecked` with the reason.
Same for a supported facet your model cannot answer.

That third outcome is the main design decision here. A checker that quietly treats *"I did not
look"* as *"it is fine"* produces a clean report for a model nobody checked — and a clean report is
believed. `NotChecked` is spelled differently from `Passed` so it cannot be mistaken for one.

The same reasoning runs through the rest:

- a **pattern is anchored** to the whole value, as XSD means it — otherwise `K-[0-9]{3}` would accept
  `XK-001` and a naming standard would pass on names that violate it;
- a **broken pattern is refused at load**, because left alone it matches nothing, which looks exactly
  like a strict rule working while being a rule that never ran;
- a property that is **present but empty** does not satisfy an existence requirement;
- an **empty applicability selects nothing**, not everything — a half-written rule must not condemn
  the whole project;
- **bounds compare numerically and invariantly**, so `1.5` is not read as `15` on a machine whose
  regional settings differ from the author's.

## Supported

| Part | State |
| --- | --- |
| `entity` (name, predefinedType) | supported |
| `attribute` (name, value) | supported |
| `property` (propertySet, baseName, value) | supported |
| `simpleValue`, `enumeration`, `pattern`, bounds, lengths | supported |
| `cardinality` required / optional / prohibited | supported |
| `minOccurs` / `maxOccurs` on applicability | supported |
| `classification`, `material`, `partOf` | **parsed, reported as not checked** |
| XSD validation against the official `ids.xsd` | not yet |
| Official buildingSMART test suite in CI | not yet |

Until those last two are done, treat this as *"reads IDS and checks the facets above"* rather than
*"IDS conformant"*. Claiming more than a tool does is the failure it is built to catch.

## Licence

MIT.
