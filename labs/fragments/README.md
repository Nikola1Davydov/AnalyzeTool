# labs/fragments — a Revit → `.frag` converter

A spike: write That Open's [Fragments](https://github.com/ThatOpen/engine_fragment) format directly
from C#, so a Revit model can be opened in an `@thatopen/components` viewer without an IFC round
trip. No such converter exists publicly — their only writer is the TypeScript `IfcImporter` — so the
first job was working out what the format actually looks like on disk. That is written up in
[SPEC.md](SPEC.md), and it is the part worth reading first.

**Why not go through IFC:** an IFC export is slow, lossy, and replaces Revit's `ElementId` with an
IfcGUID. Writing `.frag` ourselves keeps `ElementId` as the item identity, which is what lets the
viewer's selection address the very same elements the `CommandQueue` already speaks about.

## Status

| | |
| --- | --- |
| Format spec | derived from a reference file and documented |
| C# writer | complete for shells, materials, instancing, attributes — 35 tests |
| Validated | a file written by our C# **loads and renders** in `@thatopen/fragments@3.4.7` (headless Chromium, `viewer/`) |
| IFC guid | implemented and cross-checked against an independent implementation |
| Revit mapper | written; compiles against Revit 2025 and 2026 — **never executed**, needs Revit |
| Not yet written | `spatial_structure` (the viewer's model tree), `relations`, `indexes`, circle extrusions (rebar) |

### Why two identities per element

The goal this feeds is comparing a received IFC file against the live Revit model. Both sides can
become `.frag` — the IFC by That Open's own `IfcImporter`, the Revit model by this writer — and then
they are the same kind of thing in the same viewer. That only works if the items carry a shared key:

- `localId` = **ElementId**, so a click in the viewer turns back into a command against the model.
- `guid` = the **IFC guid** Revit's own IFC exporter would assign (`ExportUtils.GetExportId` put
  through `IfcGuid`), so the element matches its counterpart on the IFC side.

The library targets plain `net8.0` and has no Revit dependency, so it builds and tests anywhere.
When the format work settles it moves to `src/AnalyseTool.Tools/Geometry/Infrastructure/` and picks
up Tools' target frameworks; only the Revit-facing mapper is new code at that point.

Nothing here is wired into the plugin, the solution under `src/`, or CI.

## Layout

```
schema/index.fbs                     vendored FlatBuffers schema (ThatOpen/engine_fragment@main)
SPEC.md                              how the format really behaves — read this before changing the writer
src/AnalyseTool.Fragments/           the writer (Revit-free)
  Schema/                            flatc-generated bindings, checked in
  FragmentModel.cs                   what callers build: items, shells, materials, samples
  FragmentWriter.cs                  serializer → zlib-compressed FlatBuffers
  Axes.cs                            Revit's Z-up → the format's Y-up
  IfcGuid.cs                         the 22-character IFC identifier — the IFC↔Revit join key
extension/                           the AnalyseTool extension: Revit Document -> .frag
src/AnalyseTool.Fragments.Cli/       writes a demo .frag with no Revit involved
viewer/                              loads a written .frag in That Open's real runtime, headless
tests/                               35 tests locking in the on-disk conventions
tools/generate-bindings.sh           regenerates the bindings (needs flatc 25.2.10)
tools/inspect_frag.py                decodes any .frag and reports how its arrays are used
tools/frag_to_obj.py                 rebuilds the geometry as OBJ — checks the transform chain
```

## Try it

```bash
cd labs/fragments

dotnet test                                          # 35 tests, no Revit needed
dotnet run --project src/AnalyseTool.Fragments.Cli -- out/demo.frag

bash tools/generate-bindings.sh                      # once, for the Python tools (needs flatc)
python3 -m pip install flatbuffers
python3 tools/inspect_frag.py out/demo.frag          # what got written
python3 tools/frag_to_obj.py out/demo.frag out/demo.obj
```

`inspect_frag.py` works on any `.frag`, including theirs — that is how SPEC.md was written:

```bash
curl -sSLO https://raw.githubusercontent.com/ThatOpen/engine_components/main/resources/frags/school_arq.frag
python3 tools/inspect_frag.py school_arq.frag
```

## Units

**Geometry is already project-independent.** Revit's internal length unit is decimal feet in every
project, whatever the display units are set to, so the exporter's single feet→metres factor is
correct whether the project is in millimetres, metres or feet and inches. A project in mm does not
produce a model at the wrong scale.

**Parameter values were not.** `Parameter.AsValueString()` formats in the project's display units
*and* in the Revit UI's language — the same wall thickness reads as `200`, `0.2`, `7 7/8"` or `0,2`
depending on the machine, and the last one cannot even be parsed as a number. Nothing diffable
against an IFC file. So values are exported as invariant-culture numbers in SI, with the unit named
in the attribute type:

```
["Width","0.2","Length[m]"]        not  ["Width","200 mm","Double"]
["Area","15.4","Area[m2]"]
["Rotation","1.5707963267948966","Angle[rad]"]
["Structural","true","Boolean"]
["Mass","78.5","mass[internal]"]   a spec with no pinned SI unit — labelled, never silently converted
```

Type names are written out rather than taken from `LabelUtils`, whose labels are localized: a German
Revit would otherwise emit `Länge[m]` and the type itself would stop being comparable.

What the project *displays* in is recorded once in the model metadata, for a UI to format with:

```json
{"storedUnits":"SI (m, m2, m3, rad)","displayUnits":{"length":"millimeters","area":"squareMeters"}}
```

Still open, and adjacent: `Meshes.coordinates` is left at the origin, so a model placed on shared
coordinates carries no survey offset yet.

## The three things that will bite you

All three are documented with evidence in SPEC.md; they are repeated here because none of them is
guessable from the schema.

1. **`guids_items` and `relations_items` hold raw localIds, `meshes_items` holds indices.** The schema
   describes all three with the same sentence. They are not the same.
2. **Up is +Y.** Revit and IFC are both Z-up; the format is not, and their importer converts while
   writing. `Axes.ZUpToYUp` handles it — and rotates *placements only*, never local geometry, or the
   rotation gets applied twice.
3. **Profiles are polygons and must be planar.** The viewer triangulates them with earcut. Feeding
   Revit's tessellated triangles in as 3-point profiles is always safe; merging coplanar triangles
   into bigger polygons is a later size optimisation.

## Checking the viewer end

```bash
cd viewer
npm install
node build-serve.mjs ../out/demo.frag ../out/serve
CHROMIUM_PATH=<chromium> node serve-check.mjs ../out/serve ../out/viewer.png
```

It loads the file with `FragmentsModels`, then prints what the LIBRARY read back — item count,
categories, localIds, bounding box — and exits non-zero unless the page reaches PASS. `build.mjs`
produces the same page as one self-contained HTML instead; that form needs a real origin, because a
module worker created from a blob URL will not start on a `file://` page.

## Running it in Revit

The exporter is an **AnalyseTool extension**, not a Revit add-in of its own — it rides the same
rails third-party extensions do, so it appears as an ordinary command and is callable from the
WebView UI (`AT.invoke("analysetool.fragments.Export")`) and from an AI agent over MCP. Read-only:
no transaction, nothing changes.

It is deliberately NOT a command in `Tools/`: that project may ProjectReference nothing but the Sdk,
so putting the writer there today would either break `Check-Boundaries.ps1` or make the shipping
plugin build depend on this prototype.

```bash
# once: build the WebView page (node is deliberately not a build dependency of the plugin)
cd labs/fragments/extension/ui-src && npm install && node build.mjs
```

```powershell
# R25 / R26 / R27 — Debug deploys straight to the live extensions folder
dotnet build labs/fragments/extension -c "Debug R25"
```

That lands `plugin.json` and `ui/` in `%LOCALAPPDATA%\AnalyseTool\extensions\AnalyseTool.Fragments\` with the
assemblies under `<year>\`. A brand-new extension needs one Revit restart; after that a rebuild plus
**Settings → Reload** is the whole loop.

Then open **AnalyseTool → Fragments** on the ribbon. The page has two buttons — *Active view* and
*Whole model*; start with the active view, it is far quicker and a wrong result shows up just as
well. It exports, loads the result and renders it in place, next to the numbers that decide what to
fix:

| field | what it tells you |
| --- | --- |
| `items` / `shells` / `samples` | elements, unique geometries, placements |
| **`reuse`** | samples ÷ shells. **Above 1.0 means family geometry is shared across instances** — the whole reason for exporting from Revit rather than through IFC. At exactly 1.0 the instancing is not working. |
| `triangles` | tessellation cost; if it is huge, merging coplanar triangles into polygons is the next optimisation |
| `tessellateMs` / `writeMs` / `bytes` | where the time and the size actually go |

The page verifies without Revit too — it serves the built extension folder, stubs `window.AT` the
way the host injects it, and drives the real button:

```bash
cd labs/fragments/extension/ui-src && node check.mjs <extensionDir> shot.png
```

`viewer/` is the same thing for a `.frag` on disk, independent of the extension:

```bash
cd labs/fragments/viewer && npm install
node build-serve.mjs /path/to/YourModel.frag ../out/serve
node serve-check.mjs ../out/serve ../out/check.png
```

Both serve over HTTP on purpose: on a `file://` page the module worker cannot start and the load
hangs silently. Inside Revit this is a non-issue — WebView2 serves the extension folder from a real
`https://` virtual host, which is also why the page can fetch `../model.frag` relatively.

## Next

1. Run the mapper inside Revit. It compiles but has never executed — every claim about it is a
   claim about code, not about output.
2. `spatial_structure` from Revit's Level hierarchy, so the viewer gets a model tree.
3. The IFC side: `IfcImporter` on a received IFC, then match the two models by IFC guid.
