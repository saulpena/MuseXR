"""Deformed-state check for a rigged generated character (skinning fails only when joints bend).

blender -b --factory-startup -P pose_check.py -- <animated.glb> <out_dir>

Renders first / middle / last frame of the file's animation from front, side and three-quarter,
and prints simple checks that can fail: spine within 40 deg of up, head above hips, feet below
hips, and the largest vertex distance from the skeleton's bounding box.
"""
import bpy, sys, math, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
model, out = argv[0], argv[1]
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=model)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = max((o for o in scene.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
for o in scene.objects:
    if o.type == "MESH" and o is not mesh: o.hide_render = True
for mat in bpy.data.materials:
    if mat.use_nodes:
        for n in mat.node_tree.nodes:
            if n.type == "BSDF_PRINCIPLED":
                for name, val in (("Metallic", 0.0), ("Roughness", 0.7), ("Emission Strength", 0.0)):
                    for l in list(n.inputs[name].links): mat.node_tree.links.remove(l)
                    n.inputs[name].default_value = val
act = arm.animation_data.action if arm.animation_data else None
f0, f1 = (int(act.frame_range[0]), int(act.frame_range[1])) if act else (1, 1)
frames = [f0, (f0 + f1) // 2, f1]

world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs[0].default_value = (0.32, 0.33, 0.35, 1); bg.inputs[1].default_value = 0.6
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sun.data.energy = 3.0
sun.rotation_euler = (math.radians(50), 0, math.radians(-30)); scene.collection.objects.link(sun)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam); scene.camera = cam
cam.data.lens = 50
try: scene.render.engine = "BLENDER_EEVEE"
except TypeError: scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x, scene.render.resolution_y = 500, 800

def bone_w(name):
    pb = arm.pose.bones.get(name)
    return arm.matrix_world @ pb.head if pb else None

report = []
for fr in frames:
    scene.frame_set(fr)
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg); me = ev.to_mesh()
    pts = [mesh.matrix_world @ v.co for v in me.vertices]; ev.to_mesh_clear()
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    hips, head = bone_w("Hips"), bone_w("Head")
    feet = [bone_w("LeftFoot"), bone_w("RightFoot")]
    spine = (head - hips).normalized()
    ang = math.degrees(math.acos(max(-1, min(1, spine.z))))
    bones = [arm.matrix_world @ pb.head for pb in arm.pose.bones]
    blo = Vector((min(b.x for b in bones), min(b.y for b in bones), min(b.z for b in bones)))
    bhi = Vector((max(b.x for b in bones), max(b.y for b in bones), max(b.z for b in bones)))
    far = max(Vector([max(blo[i] - p[i], 0, p[i] - bhi[i]) for i in range(3)]).length for p in pts)
    ok = ang < 40 and head.z > hips.z and all(f.z < hips.z for f in feet) and far < 0.30
    report.append(f"frame {fr}: spine_tilt {ang:.1f} deg, head_above_hips {head.z > hips.z}, feet_below_hips {all(f.z < hips.z for f in feet)}, max_vertex_outside_skeleton_box {far:.2f} m -> {'PASS' if ok else 'FAIL'}")
    c = (lo + hi) / 2; H = hi.z - lo.z
    for az in (0, 35, 90):
        a = math.radians(az)
        d = Vector((math.sin(a), -math.cos(a), 0.05)).normalized()
        cam.location = c + d * H * 2.3
        cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"f{fr:03d}_az{az:03d}.png")
        bpy.ops.render.render(write_still=True)
open(os.path.join(out, "pose_report.txt"), "w").write("\n".join(report) + "\n")
print("\n".join(report))
print("VERDICT:", "PASS" if all(r.endswith("PASS") for r in report) else "NOT YET")
