"""
Export each master as a T-posed, armature-free FBX, ready to drop into Mixamo's auto-rigger.

Run headless:

    blender -b --factory-startup -P Tools/rig/export_for_mixamo.py -- --all

Output: Tools/mixamo/<name>-tpose.fbx. Deliberately OUTSIDE Assets/ - these are upload artefacts
for a web tool, not project assets, and Unity should not import them.

What Mixamo wants, and why each step is here:

  - ONE mesh, NO skeleton. The auto-rigger builds its own `mixamorig` hierarchy (~65 bones with
    fingers) and an existing armature is not used. So the current 17-bone rig is posed, baked
    into the mesh by applying the Armature modifier, and then deleted along with its vertex
    groups. The rig we have is not discarded on disk - the .glb files are untouched - it just
    does not travel with the upload.
  - T-POSE. Mixamo places markers on chin, wrists, elbows, knees and groin, and tolerates an
    A-pose, but it is tuned for a T. These characters are generated in an A-pose, so the arms are
    aimed flat along X first. Using the rig we already have to do it costs nothing.
  - FACING THE CAMERA. Blender's FBX exporter maps Blender +Y to FBX -Z, and these characters
    face Blender -Y, so they come out facing FBX +Z - toward the viewer, which is what Mixamo's
    marker placement expects.
  - TEXTURES EMBEDDED, so the rigged character comes back still looking like the person.

Scale note: exported at the native ~0.98 units tall rather than rescaled to human height. Mixamo
normalises to its own scale anyway and hands the character back at that scale, so the conversion
is a Unity import-scale question either way - better to keep one arbitrary rescale in the pipeline
than two.
"""

import argparse
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import author_clips as rigkit   # aim(), point_at(), CHAIN_ORDER - already measured and tested

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RIGGED_DIR = os.path.join(REPO, "Assets", "Tripo", "Rigged")
OUT_DIR = os.path.join(REPO, "Tools", "mixamo")

NAMES = ["van-gogh", "monet", "picasso", "frida", "socrates"]

# Blender space for this rig: forward is -Y, up is +Z, and the .L bones sit at -X.
# A T-pose is simply both arms flat along X.
T_POSE = {
    "upperarm.L": (-1.0, 0.0, 0.0),
    "forearm.L": (-1.0, 0.0, 0.0),
    "hand.L": (-1.0, 0.0, 0.0),
    "upperarm.R": (1.0, 0.0, 0.0),
    "forearm.R": (1.0, 0.0, 0.0),
    "hand.R": (1.0, 0.0, 0.0),
}


def ensure_fbx_exporter():
    if hasattr(bpy.ops.export_scene, "fbx"):
        try:
            bpy.ops.export_scene.fbx.poll()
            return
        except Exception:
            pass
    try:
        bpy.ops.preferences.addon_enable(module="io_scene_fbx")
        print("   enabled io_scene_fbx")
    except Exception as err:
        raise SystemExit("FBX exporter unavailable and could not be enabled: %s" % err)


def wipe():
    for ob in list(bpy.data.objects):
        try:
            bpy.data.objects.remove(ob, do_unlink=True)
        except (RuntimeError, ReferenceError):
            pass
    for act in list(bpy.data.actions):
        try:
            bpy.data.actions.remove(act, do_unlink=True)
        except (RuntimeError, ReferenceError):
            pass


def export(name, dry_run=False):
    src = os.path.join(RIGGED_DIR, "%s.glb" % name)
    out = os.path.join(OUT_DIR, "%s-tpose.fbx" % name)
    if not os.path.exists(src):
        raise SystemExit("missing %s" % src)

    print("\n=== %s ===" % name)
    wipe()

    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=src)
    new = [o for o in bpy.data.objects if o not in before]

    rig = [o for o in new if o.type == 'ARMATURE'][0]
    meshes = [o for o in new if o.type == 'MESH' and len(o.vertex_groups) > 0]
    if len(meshes) != 1:
        raise SystemExit("expected one skinned mesh in %s, got %d" % (src, len(meshes)))
    mesh = meshes[0]

    # drop the importer's bone display shape and any other passenger
    for junk in [o for o in new if o.type == 'MESH' and o is not mesh]:
        bpy.data.objects.remove(junk, do_unlink=True)

    bpy.context.view_layer.objects.active = rig
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
    bpy.context.view_layer.update()

    width_before = mesh.dimensions.x
    for bone in rigkit.CHAIN_ORDER:          # parents before children
        if bone in T_POSE:
            rigkit.point_at(rig, bone, T_POSE[bone])
    bpy.context.view_layer.update()

    # bake the pose into the mesh, then let the skeleton go
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    armature_mods = [m for m in mesh.modifiers if m.type == 'ARMATURE']
    if not armature_mods:
        raise SystemExit("%s has no Armature modifier to bake" % name)
    bpy.ops.object.modifier_apply(modifier=armature_mods[0].name)

    mesh.parent = None
    mesh.matrix_world = mesh.matrix_world.copy()
    bpy.data.objects.remove(rig, do_unlink=True)
    for g in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(g)

    d = mesh.dimensions
    tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
    zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
    span_ratio = d.x / d.z if d.z else 0.0
    print("   tris=%d  arm span %.3f -> %.3f  height=%.3f  floor=%.4f  vgroups=%d  modifiers=%d"
          % (tris, width_before, d.x, d.z, min(zs), len(mesh.vertex_groups), len(mesh.modifiers)))
    print("   span/height = %.2f  %s" % (span_ratio,
          "(T-pose: expect ~0.85-1.05)" if 0.80 <= span_ratio <= 1.15 else "<-- CHECK, not a T-pose?"))

    if dry_run:
        print("   dry run - not exported")
        return

    if not os.path.isdir(OUT_DIR):
        os.makedirs(OUT_DIR)

    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh

    props = bpy.ops.export_scene.fbx.get_rna_type().properties.keys()
    kwargs = {k: v for k, v in dict(
        filepath=out,
        use_selection=True,
        object_types={'MESH'},
        add_leaf_bones=False,
        bake_anim=False,
        path_mode='COPY',
        embed_textures=True,
        axis_forward='-Z',
        axis_up='Y',
        apply_unit_scale=True,
        mesh_smooth_type='FACE',
    ).items() if k in props}
    bpy.ops.export_scene.fbx(**kwargs)
    print("   wrote %s (%.2f MB)" % (out, os.path.getsize(out) / 1e6))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--only", nargs="+", default=None)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args(argv)

    targets = args.only if args.only else (NAMES if args.all else [])
    if not targets:
        raise SystemExit("pass --all or --only <name>")

    if not args.dry_run:
        ensure_fbx_exporter()
    for t in targets:
        export(t, dry_run=args.dry_run)

    print("\nUpload these to mixamo.com > Upload Character.")
    print("Markers: chin, both wrists, both elbows, both knees, groin. Skeleton: 65 (with fingers).")


if __name__ == "__main__":
    main()
