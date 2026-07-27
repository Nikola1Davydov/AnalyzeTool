"""Reconstruct a .frag's geometry the way the viewer does and dump it as a Wavefront OBJ.

This is the geometry counterpart to inspect_frag.py: it proves the transform chain and profile
winding produce a sane solid, without needing a browser. Any viewer (or `python3 -c` sanity check on
the printed bounds) can then confirm the model looks like the building it came from.

    python3 tools/frag_to_obj.py out/demo.frag out/demo.obj

Profiles are fan-triangulated, which is correct for the convex loops a tessellator emits. The real
viewer uses earcut, so concave profiles may differ here — that is a limitation of this debug tool,
not of the file.
"""
import sys, os, zlib

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "gen", "py"))
try:
    import Model as M
    import DoubleVector, FloatVector
except ImportError:
    sys.exit("Generated Python bindings are missing — run tools/generate-bindings.sh first.")


def read_transform(t):
    """(position, x_direction, y_direction) with the z axis implied by the cross product."""
    p = t.Position(DoubleVector.DoubleVector())
    x = t.XDirection(FloatVector.FloatVector())
    y = t.YDirection(FloatVector.FloatVector())
    return (p.X(), p.Y(), p.Z()), (x.X(), x.Y(), x.Z()), (y.X(), y.Y(), y.Z())


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def apply(transform, p):
    pos, xd, yd = transform
    zd = cross(xd, yd)
    return (
        pos[0] + p[0] * xd[0] + p[1] * yd[0] + p[2] * zd[0],
        pos[1] + p[0] * xd[1] + p[1] * yd[1] + p[2] * zd[1],
        pos[2] + p[0] * xd[2] + p[1] * yd[2] + p[2] * zd[2],
    )


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else os.path.splitext(src)[0] + ".obj"

    raw = open(src, "rb").read()
    buf = zlib.decompress(raw) if raw[:1] == b"\x78" else raw
    model = M.Model.GetRootAs(bytearray(buf), 0)
    meshes = model.Meshes()

    vertices = []
    faces = []
    stats = {"samples": 0, "triangles": 0, "skipped_big": 0}

    for i in range(meshes.SamplesLength()):
        sample = meshes.Samples(i)
        item_index = meshes.MeshesItems(sample.Item())
        local_id = model.LocalIds(item_index)

        representation = meshes.Representations(sample.Representation())
        if representation.RepresentationClass() != 1:  # SHELL
            continue
        shell = meshes.Shells(representation.Id())
        if shell.Type() != 0:
            stats["skipped_big"] += 1
            continue

        local = read_transform(meshes.LocalTransforms(sample.LocalTransform()))
        world = read_transform(meshes.GlobalTransforms(sample.Item()))

        base = len(vertices)
        for j in range(shell.PointsLength()):
            pt = shell.Points(j)
            vertices.append(apply(world, apply(local, (pt.X(), pt.Y(), pt.Z()))))

        faces.append(f"g item_{local_id}")
        for j in range(shell.ProfilesLength()):
            profile = shell.Profiles(j)
            n = profile.IndicesLength()
            if n < 3:
                continue
            loop = [profile.Indices(k) for k in range(n)]
            for k in range(1, n - 1):
                faces.append(f"f {base+loop[0]+1} {base+loop[k]+1} {base+loop[k+1]+1}")
                stats["triangles"] += 1

        stats["samples"] += 1

    with open(dst, "w") as out:
        out.write(f"# reconstructed from {os.path.basename(src)}\n")
        for v in vertices:
            out.write(f"v {v[0]:.6f} {v[1]:.6f} {v[2]:.6f}\n")
        out.write("\n".join(faces) + "\n")

    xs = [v[0] for v in vertices] or [0]
    ys = [v[1] for v in vertices] or [0]
    zs = [v[2] for v in vertices] or [0]
    print(f"wrote {dst}")
    print(f"  samples   : {stats['samples']}")
    print(f"  vertices  : {len(vertices)}")
    print(f"  triangles : {stats['triangles']}")
    if stats["skipped_big"]:
        print(f"  skipped   : {stats['skipped_big']} BIG shells (not handled by this debug tool)")
    print(f"  bounds x  : {min(xs):.3f} .. {max(xs):.3f}")
    print(f"  bounds y  : {min(ys):.3f} .. {max(ys):.3f}")
    print(f"  bounds z  : {min(zs):.3f} .. {max(zs):.3f}")


if __name__ == "__main__":
    main()
