"""Export the RestraintStuffs collection of TheWall.blend into the mod's runtime model (assets/wallrestraint.wrm).

Usage (Blender 5.x, headless):
    blender -b --disable-autoexec <TheWall.blend> --python mods/WallRestraint/tools/export_model.py -- <out.wrm>

What it does:
  * evaluates every mesh of the collection with its modifiers (mirror, subdivision, solidify) - the hidden boolean cutter
    "Boolean CLICK ON ME!!" is left out, it only cuts the leg holes into the room's wall,
  * splits them into parts by kind, material (metal / leather) and side (y < 0 / y > 0 for the mirrored objects),
  * moves the origin onto the wall: x = distance out of the wall surface (into the room), y = sideways, z = height above the
    floor (Blender axes, metres); the runtime maps x -> wall normal, z -> up, y -> up x normal (Logic/ModelFormat.cs),
  * measures the anchors the pose is built around (collar ring, back rest, arm cuffs, leg tubes).

Format (little endian): "WRST" u32 version=1
  anchors: collar f32x3 center, f32 innerRadius
           arch   f32 apexOut, f32 bottom, f32 top, f32 halfWidth
           2 x arm cuff (side 0 = y < 0, 1 = y > 0): f32x3 center (middle of the opening), f32 innerRadius, f32 bottom, f32 top
           2 x leg tube (side as above): f32x3 mouth (axis point at the room-side end), f32 innerRadius, f32 outerRadius,
                                         f32 inside (how far the tube reaches behind the wall surface)
  u32 partCount, per part: u8 kind (0 collar, 1 arch, 2 arm cuff, 3 leg tube), u8 side (0, 1, 2 = centre), u8 material
      (0 metal, 1 leather), f32x3 color, u32 vertexCount, f32x3 positions[], f32x3 normals[], u32 indexCount, u32 indices[]
"""
import math
import struct
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

out_path = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "wallrestraint.wrm"
COLLECTION = "RestraintStuffs"
KIND = {"Collar": 0, "CollarLeather": 0, "Back-Arch": 1, "ArmRestraint": 2, "LegRestraintMetal": 3, "LegLeather": 3}
MIRRORED = {"ArmRestraint", "LegRestraintMetal", "LegLeather"}

dg = bpy.context.evaluated_depsgraph_get()

# --- the wall surface the restraints hang on ------------------------------------------------------
wall = bpy.data.objects["Wall"].evaluated_get(dg)
bvh = BVHTree.FromObject(wall, dg)
inv = wall.matrix_world.inverted()
probe = Vector((0.0, 0.25, 1.5))  # in the room, between the arch and an arm cuff, nothing in the way
loc, nor, _, _ = bvh.ray_cast(inv @ probe, (inv.to_3x3() @ Vector((-1, 0, 0))).normalized())
if loc is None:
    raise SystemExit("no wall found behind the restraints")
wall_x = (wall.matrix_world @ loc).x
floor_z = 0.0
print("WRM-CHECK wall surface x = %.4f" % wall_x)


def to_model(p):
    return Vector((p.x - wall_x, p.y, p.z - floor_z))


# --- leather colour: the texture's mean (the runtime uses flat toon colours) ---------------------
def mean_color(image_name, fallback):
    img = bpy.data.images.get(image_name)
    try:
        if img is None or img.size[0] == 0:
            raise RuntimeError("missing")
        small = img.copy()
        small.scale(32, 32)
        px = list(small.pixels)
        n = len(px) // 4
        c = tuple(sum(px[i * 4 + k] for i in range(n)) / n for k in range(3))
        bpy.data.images.remove(small)
        return c
    except Exception as e:  # the 4K textures live outside the .blend; fall back if they are not on this machine
        print("WRM-CHECK leather texture unavailable (%s), fallback colour" % e)
        return fallback


LEATHER = mean_color("Leather14_col.jpg", (0.18, 0.09, 0.06))
METAL = (0.14, 0.14, 0.14)
print("WRM-CHECK leather colour %.3f %.3f %.3f" % LEATHER)

# --- parts ---------------------------------------------------------------------------------------
parts = {}  # (kind, side, material) -> [positions, normals, indices, index_of]
raw = {}    # object name -> list of model-space vertex positions (for the anchors)
for name, kind in KIND.items():
    o = bpy.data.objects[name]
    e = o.evaluated_get(dg)
    m = e.to_mesh()
    m.calc_loop_triangles()
    normals = m.corner_normals
    mw = e.matrix_world
    nmat = mw.to_3x3().inverted().transposed()
    verts = [to_model(mw @ v.co) for v in m.vertices]
    raw[name] = verts
    for tri in m.loop_triangles:
        mat = o.material_slots[tri.material_index].material if o.material_slots else None
        material = 1 if mat is not None and mat.name == "Leather" else 0
        c = sum((verts[v] for v in tri.vertices), Vector()) / 3
        side = (1 if c.y > 0 else 0) if name in MIRRORED else 2
        key = (kind, side, material)
        if key not in parts:
            parts[key] = ([], [], [], {})
        pos, nrm, idx, index_of = parts[key]
        for li, v in zip(tri.loops, tri.vertices):
            n = (nmat @ normals[li].vector).normalized()
            k = (v, name, round(n.x, 4), round(n.y, 4), round(n.z, 4))
            if k not in index_of:
                index_of[k] = len(pos)
                pos.append(verts[v])
                nrm.append(n)
            idx.append(index_of[k])
    e.to_mesh_clear()


# --- anchors ---------------------------------------------------------------------------------------
def bbox(points):
    return (Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points))),
            Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points))))


# Collar: the leather lining is the ring the neck goes through (the metal ring also carries the wall bracket).
lining = raw["CollarLeather"]
lo, hi = bbox(lining)
collar_center = (lo + hi) / 2
collar_inner = min(math.hypot(p.x - collar_center.x, p.y - collar_center.y) for p in lining)

# Back rest: a half cylinder standing out of the wall.
arch = raw["Back-Arch"]
lo, hi = bbox(arch)
arch = (hi.x, lo.z, hi.z, max(abs(lo.y), abs(hi.y)))

# Arm cuffs: upright rings (about 10 x 9 cm, open at the top) on a narrow bracket from the wall. The ring is the part farther
# than ~9.5 cm from the room-side end towards the wall; a least-squares circle through it gives the centre, the nearest ring
# vertex the inner radius.
def fit_circle(points):
    import numpy as np
    a = np.array([[p.x, p.y, 1.0] for p in points])
    b = np.array([p.x * p.x + p.y * p.y for p in points])
    (cx2, cy2, _), *_ = np.linalg.lstsq(a, b, rcond=None)
    return cx2 / 2, cy2 / 2


arms = []
for side in (0, 1):
    pts = [p for p in raw["ArmRestraint"] if (p.y > 0) == (side == 1)]
    lo, hi = bbox(pts)
    ring = [p for p in pts if p.x > hi.x - 0.095]
    cx, cy = fit_circle(ring)
    inner = min(math.hypot(p.x - cx, p.y - cy) for p in ring)
    arms.append((Vector((cx, cy, (lo.z + hi.z) / 2)), inner, lo.z, hi.z))

# Leg tubes: along x, through the wall; the leather lining gives the inner radius.
legs = []
for side in (0, 1):
    metal = [p for p in raw["LegRestraintMetal"] if (p.y > 0) == (side == 1)]
    leather = [p for p in raw["LegLeather"] if (p.y > 0) == (side == 1)]
    lo, hi = bbox(metal)
    axis_y, axis_z = (lo.y + hi.y) / 2, (lo.z + hi.z) / 2
    inner = min(math.hypot(p.y - axis_y, p.z - axis_z) for p in leather)
    outer = max(math.hypot(p.y - axis_y, p.z - axis_z) for p in metal)
    legs.append((Vector((hi.x, axis_y, axis_z)), inner, outer, -lo.x))

print("WRM-CHECK collar center %s inner r %.4f" % (tuple(round(v, 4) for v in collar_center), collar_inner))
print("WRM-CHECK arch apex out %.4f z %.4f..%.4f half width %.4f" % arch)
for s, (c, r, b, t) in enumerate(arms):
    print("WRM-CHECK arm %d center %s inner r %.4f z %.4f..%.4f" % (s, tuple(round(v, 4) for v in c), r, b, t))
for s, (m, ri, ro, inside) in enumerate(legs):
    print("WRM-CHECK leg %d mouth %s inner r %.4f outer r %.4f inside %.4f" % (s, tuple(round(v, 4) for v in m), ri, ro, inside))

# --- write -----------------------------------------------------------------------------------------
def v3(f, v):
    f.write(struct.pack("<3f", v[0], v[1], v[2]))


with open(out_path, "wb") as f:
    f.write(b"WRST")
    f.write(struct.pack("<I", 1))
    v3(f, collar_center)
    f.write(struct.pack("<f", collar_inner))
    f.write(struct.pack("<4f", *arch))
    for c, r, b, t in arms:
        v3(f, c)
        f.write(struct.pack("<3f", r, b, t))
    for m, ri, ro, inside in legs:
        v3(f, m)
        f.write(struct.pack("<3f", ri, ro, inside))
    f.write(struct.pack("<I", len(parts)))
    total = 0
    for (kind, side, material), (pos, nrm, idx, _) in sorted(parts.items()):
        f.write(struct.pack("<3B", kind, side, material))
        v3(f, LEATHER if material == 1 else METAL)
        f.write(struct.pack("<I", len(pos)))
        for p in pos:
            v3(f, p)
        for n in nrm:
            v3(f, n)
        f.write(struct.pack("<I", len(idx)))
        f.write(struct.pack("<%dI" % len(idx), *idx))
        total += len(idx) // 3
        print("WRM-CHECK part kind %d side %d material %d: %d vertices, %d triangles" % (kind, side, material, len(pos), len(idx) // 3))
print("WRM-CHECK wrote %s: %d parts, %d triangles" % (out_path, len(parts), total))
