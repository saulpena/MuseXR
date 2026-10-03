"""Finish a static generated prop (miniature, lamp, relief): orient, scale to real size, origin at the bottom
centre, shrink textures, export GLB, measure, and render a review set. Headless Blender 5.1:

    blender -b --factory-startup -P prop_finish.py -- <in.glb> <out_dir> <name> '<spec json>'

spec keys
    height      target height in metres (uniform scale)        | or
    width       target width in metres (uniform scale; relief)
    depth       target total depth in metres (scales depth only; relief)
    yaw         degrees about the vertical axis, applied first, so the object's front faces the viewer
    pca         true: auto-align the thinnest horizontal axis to depth first (relief panels)
    tex         max edge for base colour and normal (default 2048)
    tex_mr      max edge for metallic/roughness (default 1024)
    no_metal    true: force metallic 0 (stone)
    tint        [r, g, b, mix] linear target colour for the base colour map, luminance kept (brass)
    kind        "object" | "relief" | "lamp"

Conventions of the exported GLB (glTF, Y up): FRONT faces +Z, origin at the bottom centre of the base
(bounding-box centre in X/Z, lowest point in Y). glTFast negates X on import; the front stays +Z in Unity.

Checks that can fail:
    relief  "relief depth" ray-casts a grid straight at the front: the spread of hit depths is the carved
            depth that the MESH carries. A flat slab with a painted normal map reads ~0.
            The "clay_rake" renders drop every texture, so only geometry can make shadows.
    lamp    flame point = the highest vertex near the cup's vertical axis inside the top 12 % of height.
"""
import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
src, out, name, spec = os.path.abspath(argv[0]), os.path.abspath(argv[1]), argv[2], json.loads(argv[3])
os.makedirs(out, exist_ok=True)
log = []
def say(*a):
    s = " ".join(str(x) for x in a); print(s); log.append(s)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scene = bpy.context.scene
meshes = [o for o in scene.objects if o.type == "MESH"]
for o in scene.objects: o.select_set(o in meshes)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
obj.parent = None
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(scene.objects):
    if o is not obj: bpy.data.objects.remove(o)
obj.name = name; obj.data.name = name
me = obj.data

def bbox():
    a = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", a); a = a.reshape(-1, 3)
    return Vector(a.min(0)), Vector(a.max(0))
def xform(m): me.transform(m); me.update()

if spec.get("pca"):
    cs = np.array([list(p.center[:2]) for p in me.polygons]); ws = np.array([p.area for p in me.polygons])
    mu = (cs * ws[:, None]).sum(0) / ws.sum(); d = cs - mu
    evals, evecs = np.linalg.eigh((d * ws[:, None]).T @ d / ws.sum())
    thin = evecs[:, 0]; ang = math.atan2(thin[0], thin[1])
    while ang > math.pi / 2: ang -= math.pi
    while ang < -math.pi / 2: ang += math.pi
    xform(Matrix.Rotation(ang, 4, "Z")); say(f"pca yaw {math.degrees(ang):.1f}")
# Blender view of a glTF: front (+Z glTF) is -Y here. yaw turns the model so its front looks down -Y.
xform(Matrix.Rotation(math.radians(spec.get("yaw", 0)), 4, "Z"))
lo, hi = bbox(); size = hi - lo
s = spec["height"] / size.z if "height" in spec else spec["width"] / size.x
xform(Matrix.Scale(s, 4))
if "depth" in spec:   # non-uniform: compress or stretch front-to-back only (shallow relief panels)
    lo, hi = bbox(); k = spec["depth"] / (hi.y - lo.y)
    xform(Matrix.Diagonal((1, k, 1, 1))); say(f"depth scaled x{k:.3f} to {spec['depth']} m")
lo, hi = bbox()
xform(Matrix.Translation(-Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))))
lo, hi = bbox(); size = hi - lo; H0 = size.z
tris = sum(len(p.vertices) - 2 for p in me.polygons)
say(f"triangles {tris}   vertices {len(me.vertices)}   scale applied {s:.5f}")
say(f"size metres  W(x) {size.x:.3f}  D(depth) {size.y:.3f}  H {size.z:.3f}")
result = dict(name=name, triangles=tris, size_whd=[size.x, size.z, size.y])

tree = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
if spec.get("kind") == "relief":
    # height field seen from the front: distance from the front plane to the first hit
    n = 120; dep = []
    for iz in range(n):
        for ix in range(n):
            o = Vector((lo.x + (ix + .5) / n * size.x, lo.y - 0.5, lo.z + (iz + .5) / n * size.z))
            h = tree.ray_cast(o, Vector((0, 1, 0)), size.y + 1)
            if h[0] is not None: dep.append(h[0].y - lo.y)
    dep = np.array(dep)
    p = np.percentile(dep, [2, 50, 98])
    say(f"relief depth (front surface, metres): p2 {p[0]:.3f} median {p[1]:.3f} p98 {p[2]:.3f}  carved range {p[2] - p[0]:.3f}"
        f"  hit {len(dep) / n / n:.0%} of grid")
    result["relief_depth_p2_p98"] = float(p[2] - p[0])
if spec.get("kind") == "lamp":
    a = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", a); a = a.reshape(-1, 3)
    top = a[a[:, 2] > hi.z - 0.12 * size.z]
    cx, cy = (top[:, 0].min() + top[:, 0].max()) / 2, (top[:, 1].min() + top[:, 1].max()) / 2
    rim_r = max(top[:, 0].max() - top[:, 0].min(), top[:, 1].max() - top[:, 1].min()) / 2
    near = a[np.hypot(a[:, 0] - cx, a[:, 1] - cy) < 0.15 * rim_r]
    near_top = near[near[:, 2] > hi.z - 0.35 * size.z]
    wick = near_top[:, 2].max() if len(near_top) else hi.z
    # cup floor under the axis: ray down from above the rim
    h = tree.ray_cast(Vector((cx, cy, hi.z + 0.05)), Vector((0, 0, -1)), 1.0)
    floor = h[0].z if h[0] is not None else None
    say(f"cup axis (blender x {cx:.3f}, y {cy:.3f}), rim radius {rim_r:.3f}, rim top z {hi.z:.3f}, "
        f"first surface down the axis z {floor if floor is None else round(floor, 3)}, highest vertex near axis z {wick:.3f}")
    fz = max(wick, floor or 0) + 0.01
    # glTF / Unity local: x = blender x (Unity negates it, so 0 stays 0), y = blender z, z = -blender y
    say(f"FLAME POINT glTF local (x, y, z) = ({cx:.3f}, {fz:.3f}, {-cy:.3f}) m   [1 cm above the wick/cup floor on the axis]")
    result["flame_point_gltf"] = [cx, fz, -cy]

# materials and textures
mx, mxr = spec.get("tex", 2048), spec.get("tex_mr", 1024); texs = []
for mat in me.materials:
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if spec.get("no_metal"):
        for l in list(bsdf.inputs["Metallic"].links): mat.node_tree.links.remove(l)
        bsdf.inputs["Metallic"].default_value = 0.0
    for l in list(bsdf.inputs["Emission Color"].links): mat.node_tree.links.remove(l)
    bsdf.inputs["Emission Strength"].default_value = 0.0
if "tint" in spec:
    # recolour the base colour toward a target hue, keeping its light/dark detail (luminance), as door_finish
    tr, tg, tb, mix = spec["tint"]
    for mat in me.materials:
        bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        if not bsdf.inputs["Base Color"].links: continue
        img = bsdf.inputs["Base Color"].links[0].from_node.image
        px = np.empty(len(img.pixels), np.float32); img.pixels.foreach_get(px); px = px.reshape(-1, 4)
        lum = px[:, :3] @ np.array([0.2126, 0.7152, 0.0722], np.float32); lum = lum / max(float(np.median(lum)), 1e-4)
        rgb = px[:, :3]; mxc = rgb.max(1); sat = (mxc - rgb.min(1)) / np.maximum(mxc, 1e-4)
        keep = (sat < 0.25) & (mxc > 0.35)          # pale unsaturated texels (cotton wick) stay as they are
        m = (~keep)[:, None] * mix
        px[:, :3] = np.clip(rgb * (1 - m) + np.outer(lum, np.array([tr, tg, tb], np.float32)) * m, 0, 1)
        say(f"tint skipped {keep.mean():.1%} pale texels")
        img.pixels.foreach_set(px.ravel()); img.update(); img.pack()
        say(f"tinted {img.name} toward linear ({tr}, {tg}, {tb}) mix {mix}")
used = set()
for mat in me.materials:
    for n in mat.node_tree.nodes:
        if n.type == "TEX_IMAGE" and n.image and any(l.to_node.type in ("BSDF_PRINCIPLED", "NORMAL_MAP", "SEPARATE_COLOR") for l in n.outputs[0].links):
            used.add(n.image.name)
role = {}
for mat in me.materials:
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if bsdf.inputs["Base Color"].links: role[bsdf.inputs["Base Color"].links[0].from_node.image.name] = "base"
    for n in mat.node_tree.nodes:
        if n.type == "NORMAL_MAP" and n.inputs["Color"].links: role[n.inputs["Color"].links[0].from_node.image.name] = "normal"
for img in bpy.data.images:
    if not img.size[0]: continue
    lim = mx if role.get(img.name) in ("base", "normal") else mxr
    if img.size[0] > lim: img.scale(lim, lim)
    if img.name in used: texs.append(f"{role.get(img.name, 'metal-rough')}:{img.size[0]}x{img.size[1]}")
say("textures", texs); result["textures"] = texs

if spec.get("back_fix"):
    # The back of a wall panel came out with a garbage texture. Point every back face's UVs at one texel
    # of plain front stone: the front texel whose colour is closest to the median front colour.
    uv = me.uv_layers.active.data
    bsdf = next(n for n in me.materials[0].node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    img = bsdf.inputs["Base Color"].links[0].from_node.image
    iw, ih = img.size; px = np.empty(iw * ih * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(ih, iw, 4)
    front = [p for p in me.polygons if p.normal.y < -0.8]
    cand = []
    for p in front[::7]:
        u = uv[p.loop_indices[0]].uv
        cand.append((u.copy(), px[min(int(u.y * ih), ih - 1), min(int(u.x * iw), iw - 1), :3]))
    med = np.median(np.array([c for _, c in cand]), 0)
    tgt = min(cand, key=lambda t: float(((t[1] - med) ** 2).sum()))[0]
    nb = 0
    for p in me.polygons:
        if p.normal.y > 0.7 and p.center.y > lo.y + 0.6 * (hi.y - lo.y):
            for li in p.loop_indices: uv[li].uv = tgt
            nb += 1
    say(f"back_fix: {nb} back faces re-pointed at the median front-stone texel {tuple(round(c, 3) for c in tgt)}, linear rgb {tuple(round(float(c), 3) for c in med)}")
glb = os.path.join(out, name + ".glb")
bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=False, export_yup=True)
say("exported", glb, f"{os.path.getsize(glb) // 1024} KB"); result["glb"] = glb

# renders
r = os.path.join(out, "renders"); os.makedirs(r, exist_ok=True)
world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
bgn = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bgn.inputs[0].default_value = (0.42, 0.43, 0.45, 1); bgn.inputs[1].default_value = 0.55
def sunl(nm, e, rx, rz):
    o = bpy.data.objects.new(nm, bpy.data.lights.new(nm, "SUN")); scene.collection.objects.link(o)
    o.data.energy = e; o.rotation_euler = (math.radians(rx), 0, math.radians(rz)); return o
key = sunl("key", 3.0, 50, -30); back = sunl("back", 1.2, 60, 160)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam); scene.camera = cam
try: scene.render.engine = "BLENDER_EEVEE"
except TypeError: scene.render.engine = "BLENDER_EEVEE_NEXT"
vt = [i.identifier for i in scene.view_settings.bl_rna.properties["view_transform"].enum_items]
scene.view_settings.view_transform = "AgX" if "AgX" in vt else "Standard"
W, H, D = size.x, size.z, size.y
ctr = Vector((0, 0, H / 2))
def shoot(fn, target, dist, az, el, res=(800, 800), lens=50):
    cam.data.lens = lens; scene.render.resolution_x, scene.render.resolution_y = res
    a, e = math.radians(az), math.radians(el)
    cam.location = target + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * dist
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(r, fn + ".png"); bpy.ops.render.render(write_still=True)
S = max(W, H, D)
far = S / (2 * math.tan(math.atan(18 / 50))) * 1.3
for az in range(0, 360, 45): shoot(f"az{az:03d}", ctr, far, az, 10)
shoot("top", ctr, far, 0, 80); shoot("low", ctr, far, 30, -20)
shoot("close_top", Vector((0, 0, H * 0.8)), far * 0.45, 25, 10)
shoot("close_mid", Vector((0, 0, H * 0.5)), far * 0.45, -30, 10)
shoot("close_base", Vector((0, 0, H * 0.12)), far * 0.45, 20, 15)
if spec.get("kind") == "relief":
    # raking POINT light, as the scene uses: ~25 deg off the panel plane, from left / right / above,
    # almost no ambient. Textured first, then clay (no textures) so only mesh depth can cast shadows.
    back.data.energy = 0; key.data.energy = 0; bgn.inputs[1].default_value = 0.06
    pt = bpy.data.objects.new("rake", bpy.data.lights.new("rake", "POINT")); scene.collection.objects.link(pt)
    pt.data.energy = 60 * H * H; pt.data.shadow_soft_size = 0.05
    spots = {"L": (-W * 0.95, lo.y - 0.55 * W, H * 0.6), "R": (W * 0.95, lo.y - 0.55 * W, H * 0.6),
             "top": (0, lo.y - 0.45 * H, H * 1.45)}
    for side, loc in spots.items():
        pt.location = loc; shoot(f"rake_{side}", ctr, far, 0, 0)
    clay = bpy.data.materials.new("clay"); clay.use_nodes = True
    cb = next(n for n in clay.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    cb.inputs["Base Color"].default_value = (0.75, 0.72, 0.66, 1); cb.inputs["Roughness"].default_value = 0.8
    keep = list(me.materials); me.materials.clear(); me.materials.append(clay)
    for side in ("L", "R"):
        pt.location = spots[side]; shoot(f"clay_rake_{side}", ctr, far, 0, 0)
    pt.location = spots["L"]; shoot("clay_rake_34", ctr, far, 40, 10)
    shoot("clay_face", Vector((0, 0, H * 0.62)), far * 0.35, 0, 0)
    me.materials.clear()
    for m in keep: me.materials.append(m)
json.dump(result, open(os.path.join(out, "measure.json"), "w"), indent=1)
open(os.path.join(out, "measure.txt"), "w").write("\n".join(log) + "\n")
print("FINISH_DONE")
