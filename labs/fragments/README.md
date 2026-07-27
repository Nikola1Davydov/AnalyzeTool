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
src/AnalyseTool.Fragments.Revit/     Revit Document -> FragmentModel (needs Revit to run)
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

The mapper ships with a standalone command so it can be tried on a real model without wiring
anything into the plugin. It opens no transaction and changes nothing.

```powershell
git fetch && git checkout claude/open-company-toolum-integration-e1auqs

# 2025 or 2026 — pick the Revit you will open
dotnet build labs/fragments/src/AnalyseTool.Fragments.Revit -p:RevitVersion=2025
```

The output folder (`bin/Debug/net8.0-windows/`) must contain exactly three DLLs —
`AnalyseTool.Fragments.Revit.dll`, `AnalyseTool.Fragments.dll` and `Google.FlatBuffers.dll`. No
Revit API DLLs: those come from Revit itself, and shipping copies would clash.

1. Copy `AnalyseTool.Fragments.Revit.addin` from that folder to
   `%AppData%\Autodesk\Revit\Addins\2025\`.
2. Open it and set `<Assembly>` to the full path of `AnalyseTool.Fragments.Revit.dll`.
3. Start Revit, open a project, then **Add-Ins → External Tools → Export .frag**.
4. Choose **Active view only** first — it is far quicker, and a wrong result shows up just as well.

The file lands on the desktop and a dialog reports items, unique geometries, placements, triangles,
tessellation time, serialisation time and file size. **Samples ÷ shells is the number to look at:**
above 1.0 means geometry is being shared across instances, which is the whole reason for exporting
from Revit rather than through IFC.

To look at the result, point the viewer at it — it makes no assumptions about which model it loads:

```bash
cd labs/fragments/viewer
npm install
node build-serve.mjs /path/to/YourModel.frag ../out/serve
node serve-check.mjs ../out/serve ../out/check.png     # headless, prints what the library read back
```

Or open `../out/serve/index.html` through any local web server and orbit it by hand. It must be
served over HTTP, not opened as a file — see the note above about module workers.

## Next

1. Run the mapper inside Revit. It compiles but has never executed — every claim about it is a
   claim about code, not about output.
2. `spatial_structure` from Revit's Level hierarchy, so the viewer gets a model tree.
3. The IFC side: `IfcImporter` on a received IFC, then match the two models by IFC guid.
