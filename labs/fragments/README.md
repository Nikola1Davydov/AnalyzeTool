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
| C# writer | complete for shells, materials, instancing, attributes — 25 tests |
| Validated | our output parses with `@thatopen/fragments@3.4.7`'s own bindings; all cross-indices resolve |
| Revit mapper | **not started** — this library is deliberately Revit-free |
| Not yet written | `spatial_structure` (the viewer's model tree), `relations`, `indexes`, circle extrusions (rebar) |

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
src/AnalyseTool.Fragments.Cli/       writes a demo .frag with no Revit involved
tests/                               25 tests locking in the on-disk conventions
tools/generate-bindings.sh           regenerates the bindings (needs flatc 25.2.10)
tools/inspect_frag.py                decodes any .frag and reports how its arrays are used
tools/frag_to_obj.py                 rebuilds the geometry as OBJ — checks the transform chain
```

## Try it

```bash
cd labs/fragments

dotnet test                                          # 25 tests, no Revit needed
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

## Next

1. The Revit mapper: walk elements, tessellate per family symbol so instances share one shell, map
   materials, feed `ElementId` in as the localId. `Tools/Families/Infrastructure/FamilyMeshService.cs`
   already does the tessellation and nested-`GeometryInstance` composition this needs.
2. `spatial_structure` from Revit's Level hierarchy, so the viewer gets a model tree.
3. Load a written file in a real `@thatopen/components` viewer — the one check that cannot be done
   headless here, and the one that confirms the Y-up call.
