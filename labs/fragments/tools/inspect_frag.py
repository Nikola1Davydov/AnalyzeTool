"""Decode a .frag file against the vendored schema and report how the fields are ACTUALLY used.

The schema comments are ambiguous about several index arrays; this probes them empirically.
"""
import sys, zlib, os, json
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "gen", "py"))
import Model as M
import DoubleVector, FloatVector, BoundingBox

def dv(o): return (o.X(), o.Y(), o.Z())
def tpos(t): return dv(t.Position(DoubleVector.DoubleVector()))
def txd(t): return dv(t.XDirection(FloatVector.FloatVector()))
def tyd(t): return dv(t.YDirection(FloatVector.FloatVector()))
def r3(t): return tuple(round(v, 3) for v in t)

path = sys.argv[1] if len(sys.argv) > 1 else "school_arq.frag"
raw = open(path, "rb").read()
buf = zlib.decompress(raw) if raw[:1] == b"\x78" else raw
m = M.Model.GetRootAs(bytearray(buf), 0)
mesh = m.Meshes()

n_items = m.LocalIdsLength()
local_ids = [m.LocalIds(i) for i in range(n_items)]
local_set = set(local_ids)

print("=" * 72)
print("MODEL", path, f"({len(raw):,} compressed -> {len(buf):,} raw)")
print("=" * 72)
print("metadata     :", m.Metadata())
print("guid         :", m.Guid())
print("max_local_id :", m.MaxLocalId(), " (max of local_ids =", max(local_ids), ")")
print("local_ids    :", n_items, "sorted:", local_ids == sorted(local_ids), "->", local_ids[:6])
print("categories   :", m.CategoriesLength(), "distinct:",
      len({m.Categories(i) for i in range(m.CategoriesLength())}))
print("attributes   :", m.AttributesLength())
print("relations    :", m.RelationsLength(), " relations_items:", m.RelationsItemsLength())
print("guids        :", m.GuidsLength(), " guids_items:", m.GuidsItemsLength())
print("unique_attrs :", m.UniqueAttributesLength(), " relation_names:", m.RelationNamesLength(),
      " indexes:", m.IndexesLength())

print("\n--- INDEX SEMANTICS (index-into-local_ids  vs  raw localId value) ---")
def probe(name, getter, length):
    vals = [getter(i) for i in range(length)]
    if not vals:
        print(f"{name:16s}: empty")
        return
    mx = max(vals)
    as_index = mx < n_items
    all_are_ids = all(v in local_set for v in vals[:4000])
    print(f"{name:16s}: len={length:6d} max={mx:9d}  fits-as-index={as_index!s:5s}  "
          f"first4-in-local_ids={all_are_ids}  head={vals[:5]}")

probe("guids_items", m.GuidsItems, m.GuidsItemsLength())
probe("relations_items", m.RelationsItems, m.RelationsItemsLength())
probe("meshes_items", mesh.MeshesItems, mesh.MeshesItemsLength())

# Cross-check: does meshes_items[k] index into local_ids?
mi = [mesh.MeshesItems(i) for i in range(mesh.MeshesItemsLength())]
print(f"\nmeshes_items: len={len(mi)} sorted={mi == sorted(mi)} max={max(mi)} n_items={n_items}")
print("  -> if index-into-local_ids, the items with geometry are:",
      [local_ids[k] for k in mi[:5]])
print("  -> global_transforms len =", mesh.GlobalTransformsLength(),
      "(should equal len(meshes_items) if 1 transform per geometric item)")
print("  -> match:", mesh.GlobalTransformsLength() == len(mi))

print("\n--- MESHES ---")
c = mesh.Coordinates()
print("coordinates  : pos", r3(tpos(c)), "xd", r3(txd(c)), "yd", r3(tyd(c)))
print("samples      :", mesh.SamplesLength())
print("representations:", mesh.RepresentationsLength())
print("materials    :", mesh.MaterialsLength())
print("shells       :", mesh.ShellsLength())
print("circle_extr. :", mesh.CircleExtrusionsLength())
print("local_transf.:", mesh.LocalTransformsLength())
print("global_transf:", mesh.GlobalTransformsLength())
for nm in ("MaterialIds", "RepresentationIds", "SampleIds", "LocalTransformIds", "GlobalTransformIds"):
    ln = getattr(mesh, nm + "Length")()
    print(f"  {nm:18s}: {ln}")

smax_item = max(mesh.Samples(i).Item() for i in range(mesh.SamplesLength()))
smax_repr = max(mesh.Samples(i).Representation() for i in range(mesh.SamplesLength()))
smax_mat = max(mesh.Samples(i).Material() for i in range(mesh.SamplesLength()))
smax_lt = max(mesh.Samples(i).LocalTransform() for i in range(mesh.SamplesLength()))
print(f"\nsample.item      max={smax_item:6d}  vs len(meshes_items)={len(mi)}  -> in range: {smax_item < len(mi)}")
print(f"sample.repr      max={smax_repr:6d}  vs len(representations)={mesh.RepresentationsLength()} -> in range: {smax_repr < mesh.RepresentationsLength()}")
print(f"sample.material  max={smax_mat:6d}  vs len(materials)={mesh.MaterialsLength()} -> in range: {smax_mat < mesh.MaterialsLength()}")
print(f"sample.localT    max={smax_lt:6d}  vs len(local_transforms)={mesh.LocalTransformsLength()} -> in range: {smax_lt < mesh.LocalTransformsLength()}")

print("\nrepresentation.id -> index into the per-class array (shells / circle_extrusions):")
from collections import Counter
cls = Counter()
for i in range(mesh.RepresentationsLength()):
    cls[mesh.Representations(i).RepresentationClass()] += 1
print("  representation_class histogram:", dict(cls), "(1=SHELL, 2=CIRCLE_EXTRUSION)")
shell_ids = [mesh.Representations(i).Id() for i in range(mesh.RepresentationsLength())
             if mesh.Representations(i).RepresentationClass() == 1]
print(f"  shell repr ids: n={len(shell_ids)} max={max(shell_ids) if shell_ids else '-'} "
      f"vs len(shells)={mesh.ShellsLength()}")

print("\n--- SHELL ANATOMY (first 3) ---")
for i in range(min(3, mesh.ShellsLength())):
    sh = mesh.Shells(i)
    print(f"shell[{i}] type={sh.Type()} points={sh.PointsLength()} profiles={sh.ProfilesLength()} "
          f"holes={sh.HolesLength()} big_prof={sh.BigProfilesLength()} big_holes={sh.BigHolesLength()} "
          f"face_ids={sh.ProfilesFaceIdsLength()}")
    sizes = Counter(sh.Profiles(j).IndicesLength() for j in range(sh.ProfilesLength()))
    print(f"   profile vertex-count histogram: {dict(sorted(sizes.items()))}")
    for j in range(min(3, sh.ProfilesLength())):
        p = sh.Profiles(j)
        print(f"   profile[{j}]:", [p.Indices(k) for k in range(p.IndicesLength())])
    for j in range(min(3, sh.PointsLength())):
        pt = sh.Points(j)
        print(f"   point[{j}] =", r3(dv(pt)))
    print("   face_ids head:", [sh.ProfilesFaceIds(k) for k in range(min(12, sh.ProfilesFaceIdsLength()))])

print("\n--- GLOBAL TRANSFORMS (first 3) ---")
for i in range(min(3, mesh.GlobalTransformsLength())):
    t = mesh.GlobalTransforms(i)
    print(f"  gT[{i}] pos={r3(tpos(t))} xd={r3(txd(t))} yd={r3(tyd(t))}")
print("--- LOCAL TRANSFORMS (first 3) ---")
for i in range(min(3, mesh.LocalTransformsLength())):
    t = mesh.LocalTransforms(i)
    print(f"  lT[{i}] pos={r3(tpos(t))} xd={r3(txd(t))} yd={r3(tyd(t))}")

print("\n--- MATERIALS (first 6) ---")
for i in range(min(6, mesh.MaterialsLength())):
    mt = mesh.Materials(i)
    print(f"  mat[{i}] rgba=({mt.R()},{mt.G()},{mt.B()},{mt.A()}) rendered_faces={mt.RenderedFaces()} stroke={mt.Stroke()}")

print("\n--- ATTRIBUTES / CATEGORIES for the first 3 items ---")
for i in range(min(3, n_items)):
    a = m.Attributes(i)
    print(f"  item[{i}] local_id={local_ids[i]} category={m.Categories(i)}")
    for j in range(a.DataLength()):
        print("      ", a.Data(j))

print("\n--- SPATIAL STRUCTURE (alternating category/local_id nodes?) ---")
def walk(node, depth=0, budget=[16]):
    if budget[0] <= 0 or node is None:
        return
    budget[0] -= 1
    print("  " + "  " * depth + f"local_id={node.LocalId()} category={node.Category()} children={node.ChildrenLength()}")
    for i in range(min(2, node.ChildrenLength())):
        walk(node.Children(i), depth + 1, budget)
walk(m.SpatialStructure())
