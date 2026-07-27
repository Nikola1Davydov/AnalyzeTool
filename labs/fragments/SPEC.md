# The `.frag` format — as it actually is on disk

Derived empirically by decoding a reference file written by That Open's own IFC importer, because
the schema comments in `schema/index.fbs` are ambiguous (and in two places misleading) about how the
index arrays are used.

**Reference file:** `resources/frags/school_arq.frag` from `ThatOpen/engine_components@main`
(3 391 365 B compressed → 10 734 096 B raw, 23 446 items, 5 512 of them with geometry).
**Schema source:** `ThatOpen/engine_fragment@main:packages/fragments/flatbuffers/index.fbs`.
**Toolchain:** `flatc` 25.2.10 — the same FlatBuffers version `@thatopen/fragments@3.4.7` pins at runtime.

Reproduce any of the claims below with:

```bash
python3 tools/inspect_frag.py <file>.frag
```

## 1. Container

| Layer | Fact |
| --- | --- |
| Compression | **zlib/deflate** (`pako` on the JS side), magic `78 9c`. Not gzip. |
| Payload | A FlatBuffers buffer, root type `Model`. |
| File identifier | The schema declares `file_identifier "0001"`, but the reference file has **zeros** at bytes 4..8 — it was finished *without* the identifier. Do not rely on it when reading; writing it is optional. |

## 2. The identity model

`local_ids: [uint]` is the item table. Everything else hangs off it.

- `local_ids` is **not sorted** and not required to be.
- `max_local_id` is the **next free id**, not the maximum in use (reference: `max_local_id = 1534642`,
  highest id actually used = 1534640).
- Ids are allocated from **one shared counter** across items *and* mesh entities — materials, samples
  and transforms carry ids from the very same space (see §5).

### Two different kinds of "items" array — this is the main trap

The schema calls several arrays "an indexation matching localIds indices with X", but they do not
all mean the same thing:

| Array | Length | Contains | Verified by |
| --- | --- | --- | --- |
| `guids_items` | = `len(guids)` = 18 577 | **raw localId values** | max = 1 497 643 ≫ 23 446 items, so it cannot be an index; all 18 577 values are present in `local_ids` |
| `relations_items` | = `len(relations)` = 19 848 | **raw localId values** | max = 1 497 643; 681 of them are *not* in `local_ids` (spatial nodes like IfcProject/IfcSite carry relations without being items) |
| `meshes_items` | 5 512 | **indices into `local_ids`** | sorted ascending, max = 5 511 < 23 446 |

So: `guids[i]` belongs to the item whose localId is `guids_items[i]`, but `meshes_items[k]` must be
resolved through `local_ids[meshes_items[k]]`.

### Arrays that are simply parallel to `local_ids`

Both have exactly 23 446 entries, one per item, positionally aligned:

- `categories: [string]` — **one category string per item, repeated verbatim**, not a deduplicated
  pool (30 distinct values across 23 446 entries). The schema comment "an array of all item
  categories found in the file" reads like a pool; it is not.
- `attributes: [Attribute]` — one per item. Each `Attribute.data` is an array of **JSON strings**:
  `["Name","M_Chair-Breuer:M_Chair-Breuer:180296","IFCLABEL"]` → `[name, value, type]`.

`relations[i].data` is the same idea: `["ContainsElements",74481,76246,...]` — a JSON string whose
first element is the relation name and the rest are localIds.

`unique_attributes`, `relation_names` and `indexes` are **empty** in the reference file — they are
newer, optional lookup aids. A minimal writer can skip them.

## 3. Geometry addressing

The chain from an item to its triangles:

```
item k  (0 .. len(meshes_items)-1)
  ├── local_ids[ meshes_items[k] ]      → the item's localId
  ├── global_transforms[k]              → where the item sits in the model  (1:1 with meshes_items)
  └── samples where sample.item == k    → the item's geometry instances
        ├── sample.material        → index into materials
        ├── sample.representation  → index into representations
        └── sample.local_transform → index into local_transforms
              representation.id    → index into the per-class array (shells / circle_extrusions),
                                     selected by representation.representation_class
```

Reference counts, all consistent with the above: 5 512 items with geometry, 8 957 samples,
1 498 representations, 1 498 shells (all `SHELL`), 40 materials, 1 593 local transforms,
5 512 global transforms.

This is where a Revit exporter wins: one `Shell` per family symbol geometry, many `Sample`s pointing
at it with different transforms — real instancing, which an IFC round-trip usually flattens away.

## 4. `Shell` is polygonal, not triangulated

A `Shell` holds `points: [FloatVector]` (float32, metres, local to the item's transform) plus
`profiles: [ShellProfile]`, where each profile is an **ordered loop of point indices** — a face, not
a triangle. The reference file is dominated by quads:

```
shell[0]: 80 points, 60 profiles, 2 holes   profile vertex counts: {4: 56, 12: 2, 16: 2}
shell[2]: 76 points, 40 profiles, 0 holes   profile vertex counts: {4: 38, 38: 2}
```

The JS side triangulates these with `earcut` at load time, which means:

- **Profiles must be planar.** Earcut works in 2D; a non-planar loop renders wrong. Emitting Revit's
  tessellated triangles as 3-vertex profiles is always safe. Merging coplanar triangles back into
  polygons is a size optimisation to do later, not a correctness requirement.
- `holes: [ShellHole]` carries `profile_id` — the index of the profile the hole belongs to.
- `profiles_face_ids: [ushort]` — one face id per profile, so several profiles can be grouped as one
  original face for picking/highlighting. Revit's `Face` index maps onto this directly.
- `type: ShellType` — `NONE` while the shell has < 65 535 points; `BIG` switches the profile/hole
  arrays to `big_profiles`/`big_holes` with `uint` indices. A writer must split or promote on that
  threshold.

## 5. `*_ids` inside `Meshes` are real ids, not indices

`material_ids`, `representation_ids`, `sample_ids`, `local_transform_ids`, `global_transform_ids`
are each the same length as the array they name, and hold **globally unique ids from the same
counter as `local_ids`** — not `0..n-1`:

```
MaterialIds        len=   40  head=[1524051, 1524052, 1524053, ...]  max=1524090
RepresentationIds  len= 1498  head=[1524050, 1524049, 1524048, ...]  max=1524050   (descending!)
SampleIds          len= 8957  head=[1524091, 1524092, 1524093, ...]  max=1533047
LocalTransformIds  len= 1593  head=[1533048, 1533049, 1533050, ...]  max=1534640
GlobalTransformIds len= 5512  head=[1522552, 1522551, 1522550, ...]  max=1522552   (descending!)
```

They are not `(required)` in the schema. Order is arbitrary, so they are addressing handles (for
editing and streaming), not positional data.

## 6. Transforms and placement

`Transform` = `position: DoubleVector` + `x_direction: FloatVector` + `y_direction: FloatVector`.
The Z axis is implied by the cross product, so the writer must emit an **orthonormal right-handed**
pair; Revit's `Transform.BasisX/BasisY` already satisfy this.

`Meshes.coordinates` is the model's global placement (doubles, for georeferencing) — reference file:
`pos (-1.369, -0.648, 51.927)`, identity axes. Geometry itself stays local and small
(shell points in the reference are ~0.2 m), which is what keeps float32 precision usable.

**Units are metres.** Revit's internal units are decimal feet, so every coordinate needs
`UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Meters)` — or a single ×0.3048 factor.

**Up is +Y, not +Z.** This one costs a 90° rotation if you get it wrong, and it is not stated
anywhere in the schema. Clustering the 5 512 item placements in the reference file per axis:

```
X: span  -14.70 ..  64.50   distinct=376
Y: span   -1.30 ..  11.80   distinct=101   most common: 0.0, 1.2, 5.0, 10.1, 6.3, 10.7
Z: span   -1.50 ..  76.70   distinct=344
```

X and Z carry the two ~78 m horizontal extents; Y spans 13 m and its values cluster on a handful of
levels (0.0 / 5.0 / 10.1 — storey elevations). The building's storeys stack along **Y**.

IFC itself is Z-up, so their importer performs the conversion while writing, landing the file in
three.js's Y-up convention. Revit is Z-up too, so a Revit exporter must apply the same mapping:

```
x_frag =  x_revit
y_frag =  z_revit
z_frag = -y_revit
```

which keeps the frame right-handed (X × Y = X × Z = −Y = Z). `Axes.ZUpToYUp` in the library does
exactly this, for both points and transforms.

*Caveat:* this is inferred from the only reference writer that exists. If the viewer turns out to
rotate on load instead, the correction is a one-line change in `Axes` — but matching the file their
own importer produces is the safe default.

## 7. Materials

`Material` is a packed struct: `r,g,b,a: ubyte` + `rendered_faces: RenderedFaces` (ONE/TWO) +
`stroke: Stroke`. Reference values are plain opaque colours, `rendered_faces = ONE`. Revit's
`Material.Color` + `Transparency` map straight onto it; double-sided surfaces (walls seen from
inside, faces with no thickness) want `TWO`.

## 8. Minimum viable file

Every field marked `(required)` must be present, even if empty — that is the whole `Meshes` table,
plus `guids`, `guids_items`, `local_ids`, `categories`, `meshes` and `guid` on `Model`. A writer
that emits empty vectors for the parts it does not support still produces a loadable file.

## Open questions still to settle against the live loader

1. Whether `@thatopen/fragments` refuses an uncompressed buffer, or sniffs the zlib magic.
2. Whether profile winding decides the face normal, or whether the loader derives normals per shell.
3. Whether `*_ids` may be omitted entirely, or whether item-level operations in the viewer need them.

These are answered by round-tripping our own output through the real loader — that is what
`tools/` is for.
