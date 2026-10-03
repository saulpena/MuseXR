"""Render finished doors assembled as Unity will use them (front faces -Y in Blender = +Z in glTF/Unity).

blender -b --factory-startup -P door_assembly.py -- <out_dir> <frame.glb|-> <leaf.glb> <hinge_x> <hinge_z> <open_deg> [pair]

The leaf's origin is its hinge edge (bottom, mid-thickness). With "pair" a mirrored second leaf is
hung at -hinge_x, as two leaves of a double door.
"""
import bpy, sys, os, math
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
out, frame, leaf, hx, hz, deg = os.path.abspath(a[0]), a[1], a[2], float(a[3]), float(a[4]), float(a[5])
pair = len(a) > 6 and a[6] == "pair"
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
def imp(p):
    before = set(sc.objects); bpy.ops.import_scene.gltf(filepath=p)
    o = [o for o in sc.objects if o not in before and o.type == "MESH"][0]; o.rotation_mode = "XYZ"; return o
if frame != "-": imp(frame)
L = imp(leaf); L.location = (hx, 0, hz); L.rotation_euler = (0, 0, math.radians(deg))  # swings away from the viewer
if pair:
    R = imp(leaf); R.location = (-hx, 0, hz); R.scale = (-1, 1, 1); R.rotation_euler = (0, 0, math.radians(-deg))
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND"); bg.inputs[0].default_value = (0.42, 0.43, 0.45, 1); bg.inputs[1].default_value = 0.6
for nm, e, r in (("s", 3.0, (50, 0, -30)), ("b", 1.2, (60, 0, 160))):
    o = bpy.data.objects.new(nm, bpy.data.lights.new(nm, "SUN")); sc.collection.objects.link(o); o.data.energy = e
    o.rotation_euler = tuple(math.radians(v) for v in r)
cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 40
try: sc.render.engine = "BLENDER_EEVEE"
except TypeError: sc.render.engine = "BLENDER_EEVEE_NEXT"
vt = [i.identifier for i in sc.view_settings.bl_rna.properties["view_transform"].enum_items]
sc.view_settings.view_transform = "AgX" if "AgX" in vt else "Standard"
pts = [o.matrix_world @ Vector(c) for o in sc.objects if o.type == "MESH" for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (lo + hi) / 2; S = max(hi.x - lo.x, hi.z - lo.z)
for nm, az, el in (("asm_front", 0, 5), ("asm_34", 30, 10), ("asm_back34", 150, 10), ("asm_top", 0, 80)):
    sc.render.resolution_x = sc.render.resolution_y = 900
    aa, ee = math.radians(az), math.radians(el); d = S * 1.6
    cam.location = ctr + Vector((math.sin(aa) * math.cos(ee), -math.cos(aa) * math.cos(ee), math.sin(ee))) * d
    cam.rotation_euler = (ctr - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = os.path.join(out, nm + ".png"); bpy.ops.render.render(write_still=True)
print("ASM_DONE")
