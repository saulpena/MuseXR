"""Headless Blender turntable for judging a generated character outside Unity.

blender -b --factory-startup -P render_turntable.py -- <model.glb> <out_dir> [label]

Writes 8 azimuth renders (full body), face front + 3/4, both hands, then a labelled contact
sheet <out_dir>/sheet.jpg (built with Blender's own image API-free PIL-less path: tiles are
stitched by a separate python step, see make_sheet.py).
"""
import bpy, sys, math, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
model, out = argv[0], argv[1]
os.makedirs(out, exist_ok=True)
EXPO = float(os.environ.get("EXPO", "0.45"))

bpy.ops.wm.read_factory_settings(use_empty=True)
ext = os.path.splitext(model)[1].lower()
if ext in (".glb", ".gltf"):
    bpy.ops.import_scene.gltf(filepath=model)
else:
    bpy.ops.import_scene.fbx(filepath=model)

scene = bpy.context.scene
meshes = [o for o in scene.objects if o.type == "MESH"]
meshes = [max(meshes, key=lambda o: len(o.data.vertices))]
for o in scene.objects:
    if o.type == "MESH" and o not in meshes: o.hide_render = True
arm = next((o for o in scene.objects if o.type == "ARMATURE"), None)
if arm and arm.animation_data: arm.animation_data.action = None
if arm:
    for pb in arm.pose.bones: pb.matrix_basis.identity()
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
pts = []
for o in meshes:
    ev = o.evaluated_get(dg)
    me = ev.to_mesh()
    mw = o.matrix_world
    pts += [mw @ v.co for v in me.vertices]
    ev.to_mesh_clear()
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
size = hi - lo
H = size.z
center = (lo + hi) / 2

# Ship like Monet/Picasso: base colour + normal only. Drop metallic/roughness maps.
for mat in bpy.data.materials:
    if not mat.use_nodes: continue
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            for name, val in (("Metallic", 0.0), ("Roughness", 0.7), ("Emission Strength", 0.0)):
                inp = n.inputs[name]
                for l in list(inp.links): mat.node_tree.links.remove(l)
                inp.default_value = val
# Texture sizes
texs = sorted({f"{i.size[0]}x{i.size[1]}" for i in bpy.data.images if i.size[0]})
with open(os.path.join(out, "stats.txt"), "w") as f:
    f.write(f"triangles {tris}\nheight {H:.3f}\nwidth {size.x:.3f}\ndepth {size.y:.3f}\ntextures {texs}\nmeshes {len(meshes)}\n")

# Head: top 13% of height. Hands: extreme-x vertices in the lower 2/3.
head_pts = [p for p in pts if p.z > hi.z - 0.13 * H]
head_c = sum(head_pts, Vector()) / len(head_pts)
body = [p for p in pts if p.z < lo.z + 0.70 * H]
def hand(sign):
    ext = max(body, key=lambda p: sign * p.x)
    near = [p for p in body if sign * p.x > sign * ext.x - 0.09 * H]
    return sum(near, Vector()) / len(near)
hand_l, hand_r = hand(1), hand(-1)
if arm:
    hb = {b.name.lower(): b for b in arm.pose.bones}
    def bone(side):
        for n, b in hb.items():
            if "hand" in n and side in n: return arm.matrix_world @ b.head
    bl, br = bone("left"), bone("right")
    if bl and br: hand_l, hand_r = (bl, br) if bl.x > br.x else (br, bl); hand_l = hand_l - Vector((0, 0, 0.05 * H)); hand_r = hand_r - Vector((0, 0, 0.05 * H))

# World + lights
world = bpy.data.worlds.new("w"); scene.world = world
world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs[0].default_value = (0.32, 0.33, 0.35, 1); bg.inputs[1].default_value = 0.9 * EXPO

def light(name, kind, energy, loc, rot, size=2.0):
    d = bpy.data.lights.new(name, kind); d.energy = energy
    if kind == "AREA": d.size = size
    o = bpy.data.objects.new(name, d); scene.collection.objects.link(o)
    o.location = loc; o.rotation_euler = rot
    return o
# camera-relative rig is simpler: lights parented to an empty that rotates with the camera
rig = bpy.data.objects.new("rig", None); scene.collection.objects.link(rig)
rig.location = center
for l in (light("key", "AREA", EXPO * 400 * H * H, (-1.6 * H, -2.0 * H, 1.2 * H), (math.radians(55), 0, math.radians(-38)), H),
          light("fill", "AREA", EXPO * 160 * H * H, (1.8 * H, -1.6 * H, 0.4 * H), (math.radians(70), 0, math.radians(48)), H),
          light("rim", "AREA", EXPO * 250 * H * H, (0.4 * H, 2.0 * H, 1.4 * H), (math.radians(-55), 0, math.radians(170)), H)):
    l.parent = rig
    l.location = l.location  # relative to rig

cam_d = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_d)
scene.collection.objects.link(cam); scene.camera = cam
cam_d.lens = 85

try:
    scene.render.engine = "BLENDER_EEVEE"
except TypeError:
    scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.view_settings.view_transform = "AgX" if "AgX" in [i.identifier for i in scene.view_settings.bl_rna.properties["view_transform"].enum_items] else "Standard"
scene.render.image_settings.file_format = "PNG"

def shoot(name, target, dist, az_deg, el_deg=5, res=(700, 1000)):
    scene.render.resolution_x, scene.render.resolution_y = res
    az = math.radians(az_deg)
    # azimuth 0 = front. glTF characters face -Y in Blender.
    d = Vector((math.sin(az) * math.cos(math.radians(el_deg)), -math.cos(az) * math.cos(math.radians(el_deg)), math.sin(math.radians(el_deg))))
    cam.location = target + d * dist
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    rig.location = target
    rig.rotation_euler = (0, 0, az)
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)

fov_h = 2 * math.atan(36 / 2 / cam_d.lens)  # sensor 36mm on the wider axis
full_dist = (H * 1.12 / 2) / math.tan(fov_h / 2) * 0.98
for az in range(0, 360, 45):
    shoot(f"az{az:03d}", center, full_dist, az, 4)
face_dist = H * 0.95
face_dist = H * 0.62
shoot("face_front", head_c, face_dist, 0, 3, (800, 800))
shoot("face_34", head_c, face_dist, 35, 3, (800, 800))
shoot("face_side", head_c, face_dist, 90, 3, (800, 800))
shoot("hand_L", hand_l, H * 0.45, 60, 15, (700, 700))   # character's left = +x
shoot("hand_R", hand_r, H * 0.45, -60, 15, (700, 700))
print("RENDER_DONE", tris, texs)
