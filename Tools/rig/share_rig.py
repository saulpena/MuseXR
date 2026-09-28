"""
Give the other Tripo characters van Gogh's skeleton, so one Animator controller drives all five.

Run headless:

    blender -b --factory-startup -P Tools/rig/share_rig.py -- --all

Why this shape, rather than building an armature per character:

  All five Tripo characters are normalised by the generator into the same unit box - measured,
  height 0.977-0.982, feet at y=0, centred on x - so one skeleton fits all of them. And because
  glTFast imports these as *generic* Mecanim (not Humanoid), clips bind by transform path: an
  identical bone hierarchy under an identically named armature means VanGoghMaster.controller
  and its Idle/Talk/Walk clips play on every character with no retargeting.

  So the armature is never rebuilt or copied between files. Each run imports the finished rigged
  van Gogh, deletes his mesh, imports the target character's mesh in its place, and re-binds.
  Copying actions between armatures would mean re-pointing Blender 4.4+ action *slots*, which is
  the kind of thing that fails silently and exports an animation that moves nothing.

  The armature keeps the name "VanGogh_Rig" in every file on purpose. It reads oddly on Monet,
  but the name is part of the curve path, and changing it would mean re-exporting van Gogh too -
  which would renumber the fileIDs that VanGoghMaster.controller and two scenes already
  reference. Correctness over tidiness; rename as a separate, deliberate change.
"""

import argparse
import os
import sys

import bmesh
import bpy

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RIG_SRC = os.path.join(REPO, "Assets", "Tripo", "Rigged", "van-gogh.glb")
MODEL_DIR = os.path.join(REPO, "Assets", "Tripo", "Models")
OUT_DIR = os.path.join(REPO, "Assets", "Tripo", "Rigged")

# van Gogh is the source of the rig, so he is not a target.
TARGETS = ["monet", "picasso", "frida", "socrates"]


def wipe():
    """
    Empty the file between characters.

    Deliberately not read_factory_settings: that one resets addon state and drops the MCP
    bridge if this module is ever run inside a live Blender rather than headless.

    Best-effort by design. In background mode the startup object (a stray Icosphere on this
    build) can outlive the removal, so nothing downstream trusts "the scene is empty" - every
    import is scoped to the objects that import actually created, and the export is
    use_selection. That is the belt; this is the braces.
    """
    for ob in list(bpy.data.objects):
        try:
            bpy.data.objects.remove(ob, do_unlink=True)
        except (RuntimeError, ReferenceError):
            pass
    # a fresh import should not inherit orphaned actions from the previous character
    for act in list(bpy.data.actions):
        try:
            bpy.data.actions.remove(act, do_unlink=True)
        except (RuntimeError, ReferenceError):
            pass


def imported(before):
    return [o for o in bpy.data.objects if o not in before]


def supported(op, kwargs):
    """Keep only the keys this Blender's exporter actually declares, so the script survives upgrades."""
    props = op.get_rna_type().properties.keys()
    dropped = [k for k in kwargs if k not in props]
    if dropped:
        print("   note: exporter does not accept %s on this Blender - skipped" % dropped)
    return {k: v for k, v in kwargs.items() if k in props}


def bind(name, dry_run=False):
    print("\n=== %s ===" % name)
    src_model = os.path.join(MODEL_DIR, "%s.glb" % name)
    out = os.path.join(OUT_DIR, "%s.glb" % name)
    if not os.path.exists(src_model):
        raise SystemExit("missing source mesh: %s" % src_model)

    wipe()

    # 1. the finished rig, whole - armature, actions, NLA tracks, van Gogh's mesh
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=RIG_SRC)
    rig_objects = imported(before)

    arms = [o for o in rig_objects if o.type == 'ARMATURE']
    if len(arms) != 1:
        raise SystemExit("expected exactly one armature in %s, got %d" % (RIG_SRC, len(arms)))
    rig = arms[0]
    print("   rig: %s, %d bones, actions %s" % (
        rig.name, len(rig.data.bones), [a.name for a in bpy.data.actions]))

    # 2. keep van Gogh's mesh - it is the weight donor, not scenery.
    #
    #    Pick it by "is skinned to this armature" rather than "is the only mesh". Importing an
    #    armature makes Blender's glTF importer build a 42-vertex Icosphere as the bone display
    #    shape and park it in a collection named glTF_not_exported. It is NOT in the file -
    #    van-gogh.glb is 19 nodes and one mesh - and it never exports. But it does make a naive
    #    "the only MESH object" lookup grab the wrong object, which is worth one line to avoid.
    rig_meshes = [o for o in rig_objects if o.type == 'MESH']
    donors = [o for o in rig_meshes if len(o.vertex_groups) > 0]
    if len(donors) != 1:
        raise SystemExit("expected exactly one skinned mesh in %s, got %d of %d meshes" % (
            RIG_SRC, len(donors), len(rig_meshes)))
    donor = donors[0]
    for shape in [o for o in rig_meshes if o is not donor]:
        print("   ignoring importer-generated bone display shape: %s (%d verts, %s)" % (
            shape.name, len(shape.data.vertices),
            ", ".join(c.name for c in shape.users_collection) or "no collection"))
    print("   donor: %s, %d vertex groups" % (donor.name, len(donor.vertex_groups)))

    # 3. the target character's mesh in its place
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=src_model)
    new = imported(before)
    meshes = [o for o in new if o.type == 'MESH']
    if len(meshes) != 1:
        raise SystemExit("expected exactly one mesh in %s, got %d" % (src_model, len(meshes)))
    mesh = meshes[0]
    # the importer parents the mesh under an empty for the glTF scene root; detach it so the
    # only parent it ends up with is the armature
    mesh.parent = None
    for ob in [o for o in new if o.type == 'EMPTY']:
        bpy.data.objects.remove(ob, do_unlink=True)

    tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
    zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
    print("   mesh: %s  tris=%d  dims=%s  floor=%.4f top=%.4f" % (
        mesh.name, tris, tuple(round(v, 4) for v in mesh.dimensions), min(zs), max(zs)))

    # 4. weights come from van Gogh, not from the solver.
    #
    #    Bone heat cannot bind these meshes, and welding them is not enough. Measured on all
    #    five: 15,000-21,500 non-manifold edges and 7,600-11,300 duplicate vertices each, because
    #    a glTF splits a vertex per UV seam and per normal break. Binding directly leaves 37-41k
    #    of ~39k vertices unweighted; welding first (39k -> 30k verts) still leaves the solver
    #    failing on three of the four. Only Socrates happened to be clean enough to solve.
    #
    #    But we do not need a solver: van Gogh's mesh is already correctly bound to this exact
    #    armature, and every character is normalised by Tripo into the same box - height
    #    0.977-0.982, feet at y=0, centred on x - so the two bodies are in register. Nearest-face
    #    interpolated lookup against a known-good weighting beats a solve that does not converge.
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh

    xfer = mesh.modifiers.new("WeightTransfer", 'DATA_TRANSFER')
    xfer.object = donor
    xfer.use_vert_data = True
    xfer.data_types_verts = {'VGROUP_WEIGHTS'}
    xfer.vert_mapping = 'POLYINTERP_NEAREST'
    bpy.ops.object.datalayout_transfer(modifier=xfer.name)
    bpy.ops.object.modifier_apply(modifier=xfer.name)
    method = "weights transferred from van Gogh (POLYINTERP_NEAREST)"

    # glTF and Unity both carry four influences per vertex; trim before export rather than
    # letting the exporter drop the smallest ones silently and leave the rest un-normalised.
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)

    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE')

    bpy.data.objects.remove(donor, do_unlink=True)

    groups = [g.name for g in mesh.vertex_groups]
    unweighted = sum(1 for v in mesh.data.vertices if not v.groups)
    print("   bound by %s: %d vertex groups, %d unweighted verts" % (method, len(groups), unweighted))
    missing = [b.name for b in rig.data.bones if b.name not in groups]
    if missing:
        print("   bones with no vertices: %s" % missing)

    if dry_run:
        print("   dry run - not exported")
        return {"name": name, "tris": tris, "groups": len(groups),
                "unweighted": unweighted, "missing": missing, "method": method, "bytes": 0}

    # 5. export exactly the rig and the mesh, animations included
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig

    kwargs = supported(bpy.ops.export_scene.gltf, dict(
        filepath=out,
        export_format='GLB',
        use_selection=True,
        export_animations=True,
        export_animation_mode='ACTIONS',
        export_skins=True,
        export_morph=False,
        export_apply=False,
        export_yup=True,
        export_texture_dir="",
        export_image_format='AUTO',
    ))
    bpy.ops.export_scene.gltf(**kwargs)
    size = os.path.getsize(out)
    print("   wrote %s (%.2f MB)" % (out, size / 1e6))
    return {"name": name, "tris": tris, "groups": len(groups), "unweighted": unweighted,
            "missing": missing, "method": method, "bytes": size}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--all", action="store_true", help="rig every target")
    ap.add_argument("--only", nargs="+", default=None, help="rig only these, e.g. --only frida")
    ap.add_argument("--dry-run", action="store_true", help="bind and report, write nothing")
    args = ap.parse_args(argv)

    targets = args.only if args.only else (TARGETS if args.all else [])
    if not targets:
        raise SystemExit("nothing to do: pass --all or --only <name> [<name>...]")

    rows = [bind(t, dry_run=args.dry_run) for t in targets]

    print("\n===== SUMMARY =====")
    print("%-10s %8s %8s %12s  %s" % ("character", "tris", "groups", "unweighted", "method"))
    for r in rows:
        print("%-10s %8d %8d %12d  %s" % (
            r["name"], r["tris"], r["groups"], r["unweighted"], r["method"]))
    bad = [r for r in rows if r["unweighted"] or r["missing"] or "FALLBACK" in r["method"]]
    print("\n%d of %d clean" % (len(rows) - len(bad), len(rows)))
    for r in bad:
        print("  CHECK %s: unweighted=%d missing=%s" % (r["name"], r["unweighted"], r["missing"]))


if __name__ == "__main__":
    main()
