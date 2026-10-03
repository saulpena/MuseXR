"""Finish a generated door/frame for a portal: align, measure the opening, scale to spec, set the origin,
shrink textures, export GLB, and render a review set. Headless Blender:

    blender -b --factory-startup -P door_finish.py -- <in.glb> <out_dir> <name> '<spec json>'

spec keys
    kind      "leaf" | "frame"
    leaf_w, leaf_h, leaf_t          leaf: exact size in metres (non-uniform fit)
    open_w, open_h                  frame: target opening size in metres
    fit       "uniform_w" (default, scale so opening width = open_w) | "xz" (width and height each)
    shape     "rect" | "circle" | "arch"   (frame, for the fill-ratio check)
    tex       max texture edge (default 2048)
    yaw       extra degrees about Z after auto-alignment (180 flips front/back)
    cut       [shape, w, h, bottom] delete faces whose centroid lies inside this profile (membranes)

Conventions of the exported GLB (glTF, Y up): the FRONT faces +Z. glTFast negates X on import, so in
Unity the front also faces +Z. Frames: origin = floor level, centre of the opening, centre of depth.
Leaves: origin = bottom of the hinge edge (the viewer's LEFT edge seen from the front), centre of thickness.

The opening is measured by casting rays straight through the model on a grid: a cell is open when the
ray hits nothing. The opening is the open region around the centre that touches neither side nor the
top of the bounding box. A membrane, glass or back wall makes that region vanish, so this measurement
fails loudly ("opening: NONE") instead of reporting a size.
"""
import bpy, bmesh, sys, os, math, json
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
src, out, name, spec = argv[0], argv[1], argv[2], json.loads(argv[3])
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

def verts(): return [v.co.copy() for v in me.vertices]
def bbox():
    vs = verts()
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    return lo, hi
def xform(m):
    me.transform(m); me.update()

# 1. yaw-align: the thinnest horizontal direction (PCA on area-weighted face centres) becomes Y
import numpy as np
cs = np.array([list(p.center[:2]) for p in me.polygons]); ws = np.array([p.area for p in me.polygons])
mu = (cs * ws[:, None]).sum(0) / ws.sum(); d = cs - mu
cov = (d * ws[:, None]).T @ d / ws.sum()
evals, evecs = np.linalg.eigh(cov)
thin = evecs[:, 0]                                  # smallest variance
ang = math.atan2(thin[0], thin[1])                  # angle of thin axis from +Y
while ang > math.pi / 2: ang -= math.pi            # smallest rotation, keep front roughly where the generator put it
while ang < -math.pi / 2: ang += math.pi
rot = math.degrees(ang) + spec.get("yaw", 0)
xform(Matrix.Rotation(math.radians(rot), 4, "Z"))
say(f"yaw applied {rot:.1f} deg (auto {math.degrees(ang):.1f}, extra {spec.get('yaw', 0)})")

def bvh(): return BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())

def label(grid):
    """4-connected component labels of a boolean grid (no scipy inside Blender)."""
    nz, nx = grid.shape; lab = np.zeros(grid.shape, int); n = 0
    for z0 in range(nz):
        for x0 in range(nx):
            if grid[z0, x0] and not lab[z0, x0]:
                n += 1; lab[z0, x0] = n; st = [(z0, x0)]
                while st:
                    z, x = st.pop()
                    for dz, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        a, b = z + dz, x + dx
                        if 0 <= a < nz and 0 <= b < nx and grid[a, b] and not lab[a, b]:
                            lab[a, b] = n; st.append((a, b))
    return lab, n

def measure_opening(step_div=220):
    lo, hi = bbox(); tree = bvh()
    W, H = hi.x - lo.x, hi.z - lo.z
    step = max(W, H) / step_div
    nx, nz = int(W / step) + 1, int(H / step) + 1
    grid = np.zeros((nz, nx), bool)
    for iz in range(nz):
        for ix in range(nx):
            o = Vector((lo.x + (ix + .5) * step, lo.y - 1, lo.z + (iz + .5) * step))
            grid[iz, ix] = tree.ray_cast(o, Vector((0, 1, 0)), (hi.y - lo.y) + 2)[0] is None
    lab, n = label(grid)
    best = None
    for k in range(1, n + 1):
        m = lab == k
        if m[:, 0].any() or m[:, -1].any() or m[-1, :].any(): continue   # outside: touches sides/top
        a = m.sum()
        if best is None or a > best[1]: best = (k, a)
    if best is None or best[1] < 0.01 * nx * nz:
        return None
    m = lab == best[0]
    zs, xs = np.nonzero(m)
    x0, x1 = lo.x + xs.min() * step, lo.x + (xs.max() + 1) * step
    z0, z1 = lo.z + zs.min() * step, lo.z + (zs.max() + 1) * step
    return dict(x0=x0, x1=x1, z0=z0, z1=z1, w=x1 - x0, h=z1 - z0, cx=(x0 + x1) / 2, area=best[1] * step * step,
                touches_floor=bool(m[0, :].any()))

def fill_ratio(op, shape):
    w, h = op["w"], op["h"]
    ideal = {"rect": w * h, "circle": math.pi / 4 * w * h, "arch": w * (h - w / 2) + math.pi * w * w / 8}[shape]
    return op["area"] / ideal

lo, hi = bbox()
if spec["kind"] == "frame":
    op = measure_opening()
    if op is None:
        say("opening before scale: NONE (membrane or no hole)")
        if not spec.get("cut"): raise SystemExit("NO OPENING")
    else:
        say(f"opening before scale: {op['w']:.3f} x {op['h']:.3f} (model units)")
    # optional cut of a membrane: profile in final metres is applied after scaling, see below
    if op is None:  # scale by the outer box instead, cut later
        sx = sz = spec["outer_h"] / (hi.z - lo.z)
    elif spec.get("fit", "uniform_w") == "xz":
        sx, sz = spec["open_w"] / op["w"], spec["open_h"] / op["h"]
    else:
        sx = sz = spec["open_w"] / op["w"]
    sy = spec.get("depth_scale", (sx + sz) / 2)
    if "depth" in spec: sy = spec["depth"] / (hi.y - lo.y)
else:
    sx = spec["leaf_w"] / (hi.x - lo.x); sz = spec["leaf_h"] / (hi.z - lo.z); sy = spec["leaf_t"] / (hi.y - lo.y)
say(f"scale x {sx:.4f} y {sy:.4f} z {sz:.4f}  (non-uniformity {max(sx, sz) / min(sx, sz) - 1:.1%} in XZ)")
xform(Matrix.Diagonal((sx, sy, sz, 1)))

if spec.get("cut"):
    shape, cw, ch, cb = spec["cut"]
    lo, hi = bbox(); cx = (lo.x + hi.x) / 2
    def inside(p):
        x, z = p.x - cx, p.z - (lo.z + cb)
        if shape == "circle":
            r = cw / 2; return x * x + (z - r) ** 2 < r * r
        if shape == "arch":
            r = cw / 2
            return (abs(x) < r and 0 < z < ch - r) or (x * x + (z - (ch - r)) ** 2 < r * r and z >= ch - r)
        return abs(x) < cw / 2 and 0 < z < ch
    bm = bmesh.new(); bm.from_mesh(me)
    dead = [f for f in bm.faces if all(inside(v.co) for v in f.verts)]
    say(f"cut: deleted {len(dead)} faces inside {shape} {cw} x {ch} from {cb}")
    bmesh.ops.delete(bm, geom=dead, context="FACES"); bm.to_mesh(me); bm.free(); me.update()

# 2. origin
lo, hi = bbox()
if spec["kind"] == "frame":
    op = measure_opening()
    if op is None: raise SystemExit("NO OPENING after scale/cut")
    org = Vector((op["cx"], (lo.y + hi.y) / 2, lo.z))
else:
    org = Vector((lo.x, (lo.y + hi.y) / 2, lo.z))
xform(Matrix.Translation(-org))
lo, hi = bbox()
tris = sum(len(p.vertices) - 2 for p in me.polygons)
say(f"triangles {tris}")
say(f"bbox metres  x {lo.x:.3f}..{hi.x:.3f}  y {lo.y:.3f}..{hi.y:.3f}  z {lo.z:.3f}..{hi.z:.3f}   size W {hi.x - lo.x:.3f} D {hi.y - lo.y:.3f} H {hi.z - lo.z:.3f}")
result = dict(name=name, triangles=tris, size=[hi.x - lo.x, hi.z - lo.z, hi.y - lo.y])
if spec["kind"] == "frame":
    op = measure_opening(300)
    fr = fill_ratio(op, spec.get("shape", "rect"))
    say(f"opening metres: W {op['w']:.3f} H {op['h']:.3f}  x {op['x0']:.3f}..{op['x1']:.3f}  z {op['z0']:.3f}..{op['z1']:.3f}  "
        f"centre (x {op['cx']:.3f}, z {(op['z0'] + op['z1']) / 2:.3f})  floor-open {op['touches_floor']}  fill vs ideal {spec.get('shape', 'rect')} {fr:.2f}")
    result["opening"] = op; result["fill_ratio"] = fr

# 3. textures
mx = spec.get("tex", 2048); texs = []
for img in bpy.data.images:
    if img.size[0] > mx:
        img.scale(mx, mx)
    if img.size[0]: texs.append(f"{img.name}:{img.size[0]}x{img.size[1]}")
say("textures", texs); result["textures"] = texs

glb = os.path.join(out, name + ".glb")
bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=False, export_yup=True)
say("exported", glb, f"{os.path.getsize(glb) // 1024} KB")
result["glb"] = glb; result["glb_kb"] = os.path.getsize(glb) // 1024

# 4. renders
r = os.path.join(out, "renders"); os.makedirs(r, exist_ok=True)
world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
bgn = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bgn.inputs[0].default_value = (0.42, 0.43, 0.45, 1); bgn.inputs[1].default_value = 0.6
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); scene.collection.objects.link(sun)
sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
back = bpy.data.objects.new("back", bpy.data.lights.new("back", "SUN")); scene.collection.objects.link(back)
back.data.energy = 1.2; back.rotation_euler = (math.radians(60), 0, math.radians(160))
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam); scene.camera = cam
cam.data.lens = 50
try: scene.render.engine = "BLENDER_EEVEE"
except TypeError: scene.render.engine = "BLENDER_EEVEE_NEXT"
vt = [i.identifier for i in scene.view_settings.bl_rna.properties["view_transform"].enum_items]
scene.view_settings.view_transform = "AgX" if "AgX" in vt else "Standard"
W, H, D = hi.x - lo.x, hi.z - lo.z, hi.y - lo.y
ctr = Vector(((lo.x + hi.x) / 2, 0, H / 2))

def shoot(fn, target, dist, az, el, res=(900, 900), lens=50):
    cam.data.lens = lens
    scene.render.resolution_x, scene.render.resolution_y = res
    a, e = math.radians(az), math.radians(el)
    cam.location = target + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * dist
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(r, fn + ".png")
    bpy.ops.render.render(write_still=True)

S = max(W, H)
far = S / (2 * math.tan(math.atan(18 / 50))) * 1.25
for fn, az, el in (("front", 0, 3), ("front34", 35, 8), ("side", 90, 3), ("back34", 145, 8), ("back", 180, 3), ("low", 20, -25), ("high", -25, 45)):
    shoot(fn, ctr, far, az, el)
shoot("detail_top", Vector((ctr.x, 0, H * 0.8)), far * 0.42, 15, 5)
shoot("detail_bottom", Vector((ctr.x, 0, H * 0.18)), far * 0.42, -15, 10)
# see-through: magenta emitter behind, camera at eye height in front looking through
pl = bpy.data.meshes.new("bd"); bdo = bpy.data.objects.new("backdrop", pl); scene.collection.objects.link(bdo)
bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=S * 3); bm.to_mesh(pl); bm.free()
bdo.rotation_euler = (math.radians(90), 0, 0); bdo.location = (ctr.x, S * 1.2, H / 2)
mat = bpy.data.materials.new("emit"); mat.use_nodes = True; nt = mat.node_tree
for n in list(nt.nodes):
    if n.type != "OUTPUT_MATERIAL": nt.nodes.remove(n)
em = nt.nodes.new("ShaderNodeEmission"); em.inputs[0].default_value = (1, 0, 1, 1); em.inputs[1].default_value = 3
nt.links.new(em.outputs[0], next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL").inputs[0]); pl.materials.append(mat)
eye = Vector((ctr.x, 0, min(1.6, H * 0.5)))
shoot("through_front", ctr, far, 0, 0)
shoot("through_eye", eye, max(W, H) * 0.9, 0, 0, lens=28)
bdo.location = (ctr.x, -S * 1.2, H / 2)
shoot("through_back", ctr, far, 180, 0)
json.dump(result, open(os.path.join(out, "measure.json"), "w"), indent=1)
open(os.path.join(out, "measure.txt"), "w").write("\n".join(log) + "\n")
print("FINISH_DONE")
