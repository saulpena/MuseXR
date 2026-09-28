"""
Author extra animation clips for the shared master skeleton.

Run headless:

    blender -b --factory-startup -P Tools/rig/author_clips.py -- --all

Output: Assets/Tripo/Rigged/MasterClips.glb - the armature and nothing else, carrying the new
clips. Unity imports the AnimationClips from it and they play on any of the five characters.

Why a separate library file rather than adding clips to each character:

  Each of the five *.glb already carries Idle / Talk / Walk, and VanGoghMaster.controller plus
  two scenes reference those clips by fileID inside those files. Re-exporting a character would
  renumber its sub-assets and silently break those references. Clips live in their own file for
  the same reason the skeleton is shared - generic Mecanim binds by transform path, so a clip
  authored against `VanGogh_Rig/hips/...` plays on anything with that hierarchy, whatever asset
  it came from.

House conventions, read off the existing three clips rather than invented:

  - 24 fps. Idle is frames 1-97 (4.0s), Talk 1-73 (3.0s), Walk 1-25 (1.0s).
  - Every bone keyed on every frame, quaternion, LINEAR interpolation.
  - location and scale pinned at identity with two keys; only hips.location animates.
  - Every clip is loop-closed: the value at the first frame equals the value at the last.
    Note that loop-closed does NOT mean "returns to rest" - Present deliberately holds the arms
    out and oscillates around that, which loops fine because f(0) == f(1).

Two different ways of posing, because the skeleton needs both:

  SPINE CHAIN (hips/spine/chest/neck/head) is posed with small Euler angles. Those bones stand
  vertically, so their local axes line up with the body and the mapping is clean - measured by
  rotating each 30 deg and watching the tail:
      +X bows / nods FORWARD and down
      +Z leans toward the .L side
      +Y twists (that axis runs along the bone)

  ARMS are posed by AIMING the bone at a world direction, never by Euler angles. The arm bones
  hang down-and-outward in the A-pose, so their local axes are tilted and do not correspond to
  body directions. Measured: rotating upperarm.R 30 deg about local Z moves the tail
  (-0.093, +0.042, -0.014) - dominated by the sideways component, so it is abduction with a bit
  of forward, not the forward raise it looks like on paper. A first version of Greet was authored
  that way and produced an arm stuck straight out sideways. `aim()` converts a world direction
  into the bone's local rest space exactly, so a gesture can be written as what it should look
  like rather than as three angles that have to be guessed and re-guessed.

  Body axes for those directions: forward = -Y, up = +Z, the character's RIGHT = +X
  (the .L bones sit at -X).

  Elbows stay on Euler: +X on a forearm is flexion, which is what an elbow does.
"""

import argparse
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RIG_SRC = os.path.join(REPO, "Assets", "Tripo", "Rigged", "van-gogh.glb")
OUT = os.path.join(REPO, "Assets", "Tripo", "Rigged", "MasterClips.glb")

FPS = 24
BONES = ["hips", "spine", "chest", "neck", "head",
         "upperarm.L", "forearm.L", "hand.L", "upperarm.R", "forearm.R", "hand.R",
         "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R"]

# parents before children, so a world-space aim always sees an already-posed parent
CHAIN_ORDER = ["spine", "chest", "neck", "head",
               "upperarm.L", "forearm.L", "hand.L",
               "upperarm.R", "forearm.R", "hand.R"]

TAU = math.tau


def bump(t):
    """0 at both ends, 1 in the middle. Smooth, so a one-shot gesture eases in and out."""
    return math.sin(math.pi * t) ** 2


def plateau(t, sharpness=1.7):
    """Rise, hold, fall - still 0 at both ends. For a gesture that arrives and waits."""
    return max(0.0, min(1.0, math.sin(math.pi * t) * sharpness))


# --- the clips -------------------------------------------------------------------------------
# Each returns {bone: (rx, ry, rz) in degrees}, optionally "_hips_loc": (x, y, z) in metres.

def greet(rig, t):
    """A master notices the visitor: right hand up, a couple of small waves, one nod."""
    e = bump(t)
    wave = math.sin(TAU * 3 * t) * e
    return {
        "_aim": {
            # mostly FORWARD, only a little out. Aiming it sideways puts the hand further from
            # the body than the A-pose and reads as pointing at something, not greeting anyone.
            # capped at 0.80 of the way - see the coat-width ceiling noted in present()
            "upperarm.R": blend_dir(rig, "upperarm.R", (0.34, -0.60, 0.06), 0.80 * e),
        },
        "forearm.R": (88 * e + 13 * wave, 0, 0),   # elbow flexed so the hand comes up
        "hand.R": (9 * wave, 0, 0),
        "head": (5 * e, -5 * e, 0),                # small nod, slight turn toward the visitor
        "neck": (3 * e, -3 * e, 0),
        "chest": (2 * e, -3 * e, 0),
        "_hips_loc": (0.0, 0.0, 0.004 * e),
    }


def listen(rig, t):
    """Attentive stillness while somebody else speaks. Two soft nods, one slow head tilt.

    Arms stay almost still on purpose - listening reads through the head, and moving the arms
    would make it look like another talking gesture.
    """
    slow = math.sin(TAU * t)
    nod = math.sin(TAU * 2 * t)
    return {
        "_aim": {
            "upperarm.L": blend_dir(rig, "upperarm.L", (-0.30, -0.16, -0.94), 0.25 + 0.05 * slow),
            "upperarm.R": blend_dir(rig, "upperarm.R", (0.30, -0.16, -0.94), 0.25 - 0.05 * slow),
        },
        "forearm.L": (7.0 + 2.0 * slow, 0, 0),
        "forearm.R": (7.0 - 2.0 * slow, 0, 0),
        "head": (4.0 * nod, 2.5 * slow, 5.0 * slow),
        "neck": (2.0 * math.sin(TAU * 2 * t + 0.5), 0, 2.0 * slow),
        "chest": (1.0 * slow, 0, 2.0 * slow),
        "spine": (1.2 * math.sin(TAU * t + 0.8), 0, 1.0 * slow),
        "_hips_loc": (0.004 * slow, 0.0, 0.0),
    }


def ponder(rig, t):
    """Hand toward the chin, head tilted down and toward it, held, then released."""
    p = plateau(t)
    return {
        "_aim": {
            # upper arm hangs close to the body, elbow just ahead of the hip
            "upperarm.R": blend_dir(rig, "upperarm.R", (0.22, -0.30, -0.93), p),
            "upperarm.L": blend_dir(rig, "upperarm.L", (-0.26, -0.10, -0.96), 0.35 * p),
        },
        # the forearm is aimed in WORLD space, from the elbow up and inward toward the chin.
        # Euler flexion cannot express this: the elbow's local axes turn with the upper arm, so
        # the same angle that folds the hand to the face with the arm down sends it behind the
        # hip once the arm swings forward. Measured that exact failure at 138 degrees.
        "_point": {
            "forearm.R": (-0.30 * p + 0.43 * (1 - p),
                          -0.16 * p,
                          0.94 * p - 0.90 * (1 - p)),
        },
        "hand.R": (10 * p, 0, -8 * p),
        "head": (12 * p, 6 * p, -6 * p),        # -Z tilts toward the .R side, where the hand is
        "neck": (5 * p, 3 * p, -3 * p),
        "chest": (4 * p, 0, 0),
        "spine": (2 * p, 0, 0),
        "forearm.L": (16 * p, 0, 0),
        "_hips_loc": (0.0, 0.006 * p, 0.0),
    }


def present(rig, t):
    """Both hands open toward the work being discussed. Holds the pose and breathes in it."""
    o = math.sin(TAU * t)
    o2 = math.sin(TAU * t + 0.6)
    return {
        "_aim": {
            # open, forward and a little down - offering rather than reaching.
            # Weighted well toward the target: at 0.78 the hands came only 0.10 forward of the
            # body, which is not a gesture, it is standing with the arms slightly apart.
            # MEASURED CEILING, do not raise these past ~0.85. Sweeping the arms from rest to
            # the full target and watching the coat width in the hip/waist band: flat to k=0.6,
            # +8.5% at 0.8, +16% at 0.9, then +180% at 1.0 - the jacket panels carry upperarm
            # weight and blow out into a flapping sheet. This clip used to peak at exactly 1.00.
            "upperarm.L": blend_dir(rig, "upperarm.L", (-0.34, -0.91, -0.23), 0.78 + 0.05 * o),
            "upperarm.R": blend_dir(rig, "upperarm.R", (0.34, -0.91, -0.23), 0.78 - 0.05 * o),
        },
        "forearm.L": (34 + 9 * o2, 0, 0),
        "forearm.R": (34 - 9 * o2, 0, 0),
        "hand.L": (7 * o, 0, 0),
        "hand.R": (-7 * o, 0, 0),
        "chest": (3 * o, 3 * o, 0),
        "spine": (1.5 * o, 1.5 * o, 0),
        "neck": (-2 + 2 * o, 0, 0),
        "head": (-3 + 3 * o, -2 * o, 0),
        "_hips_loc": (0.005 * o, 0.0, 0.0),
    }


CLIPS = [
    ("Greet", greet, 2.0, "One-shot. A master registers the visitor."),
    ("Listen", listen, 4.0, "Loop. Attentive while somebody else is speaking."),
    ("Ponder", ponder, 3.0, "One-shot. The beat before answering."),
    ("Present", present, 3.0, "Loop. Two-handed, for gesturing at a work - Talk is one-handed."),
]


def quat(rx, ry, rz):
    return (Quaternion((1, 0, 0), math.radians(rx))
            @ Quaternion((0, 1, 0), math.radians(ry))
            @ Quaternion((0, 0, 1), math.radians(rz)))


def aim(rig, bone_name, world_dir):
    """
    The local-space rotation that points this bone along `world_dir`.

    A bone's own space has +Y running along it, and `matrix_local` takes bone space to armature
    space (the rig sits at the origin unrotated, so armature space is world space here). Pull the
    target into bone space and the answer is the rotation from +Y onto it - exact, and
    independent of how the bone happens to be tilted in the rest pose.
    """
    basis = rig.data.bones[bone_name].matrix_local.to_3x3()
    local = basis.inverted() @ Vector(world_dir).normalized()
    return Vector((0.0, 1.0, 0.0)).rotation_difference(local)


def point_at(rig, bone_name, world_dir):
    """
    Point a bone along `world_dir` **taking its parent's current pose into account**.

    `aim()` above only works for a bone whose parent is unposed, because it reasons from the rest
    matrix. That is fine for an upper arm hanging off a barely-moved chest, and wrong for a
    forearm, whose local axes rotate with the upper arm. Posing an elbow by Euler flexion has the
    same flaw: measured, 138 degrees of "flexion" with the upper arm swung forward put the hand
    behind the hip instead of at the chin.

    So set the bone's WORLD matrix and let Blender back-solve the local rotation through the
    chain. Only the orientation is ours to choose - the head position belongs to the parent, so
    it is preserved. Requires the parent to already be posed and the view layer updated.
    """
    pb = rig.pose.bones[bone_name]
    bpy.context.view_layer.update()

    y = Vector(world_dir).normalized()
    ref = Vector((0.0, 0.0, 1.0))
    if abs(y.dot(ref)) > 0.99:                 # degenerate when the target is straight up
        ref = Vector((0.0, 1.0, 0.0))
    x = ref.cross(y).normalized()
    z = x.cross(y).normalized()        # x cross y, NOT y cross x - see below

    m = Matrix((x, y, z)).transposed().to_4x4()
    # A left-handed basis here is the subtle killer. Blender happily accepts a determinant -1
    # matrix and back-solves it as a rotation PLUS a negative scale. The bone then looks correct
    # on screen, but only the rotation gets keyed, so the baked clip points the bone exactly
    # backwards. Measured: requested (-0.30, -0.16, 0.94), got (0.30, 0.16, -0.94).
    if m.to_3x3().determinant() < 0:
        raise SystemExit("point_at built a left-handed basis for %s - would flip when keyed" % bone_name)
    m.translation = pb.matrix.translation.copy()
    pb.matrix = m
    bpy.context.view_layer.update()

    # back-solving can leave a residual offset; these bones never translate
    pb.location = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()


def rest_dir(rig, bone_name):
    b = rig.data.bones[bone_name]
    return (Vector(b.tail_local) - Vector(b.head_local)).normalized()


def blend_dir(rig, bone_name, target, amount):
    """Ease from the A-pose direction toward `target`, so a gesture grows out of the rest pose."""
    d = rest_dir(rig, bone_name).lerp(Vector(target).normalized(), amount)
    return d if d.length > 1e-6 else rest_dir(rig, bone_name)


def build(rig, name, fn, seconds):
    frames = int(round(seconds * FPS)) + 1

    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'

    # PASS 1 - solve every frame with NO action assigned, and record the local rotations.
    #
    # This split is load-bearing, not tidiness. point_at() has to call view_layer.update() to see
    # where its parent ended up, and if an action is assigned that update re-evaluates the rig
    # FROM the action - silently throwing away the pose just set and keying the interpolated old
    # value instead. Symptom: the _aim clips come out fine (pure local maths, no depsgraph) while
    # the one clip using _point quietly reverts to something near the rest pose.
    rig.animation_data.action = None
    solved = []
    for i in range(frames):
        t = i / float(frames - 1)      # t == 1 on the last frame, so f(0) == f(1) closes the loop
        pose = fn(rig, t)
        aims = pose.get("_aim", {})
        points = pose.get("_point", {})

        for bone in BONES:
            pb = rig.pose.bones[bone]
            if bone in aims:
                pb.rotation_quaternion = aim(rig, bone, aims[bone])
            else:
                rx, ry, rz = pose.get(bone, (0.0, 0.0, 0.0))
                pb.rotation_quaternion = quat(rx, ry, rz)

        for bone in CHAIN_ORDER:       # parents before children
            if bone in points:
                point_at(rig, bone, points[bone])

        solved.append((
            {b: rig.pose.bones[b].rotation_quaternion.copy() for b in BONES},
            pose.get("_hips_loc", (0.0, 0.0, 0.0)),
        ))

    # PASS 2 - now bind an action and write the recorded values straight in.
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    rig.animation_data.action = action
    if hasattr(action, "slots"):
        slot = action.slots.new(id_type='OBJECT', name=rig.name)
        rig.animation_data.action_slot = slot

    for i, (rots, hips_loc) in enumerate(solved):
        f = i + 1
        for bone in BONES:
            pb = rig.pose.bones[bone]
            pb.rotation_quaternion = rots[bone]
            pb.keyframe_insert(data_path="rotation_quaternion", frame=f)

        hips = rig.pose.bones["hips"]
        hips.location = hips_loc
        hips.keyframe_insert(data_path="location", frame=f)

        # location and scale are pinned, matching the existing clips: two keys, no motion
        if i in (0, frames - 1):
            for bone in BONES:
                pb = rig.pose.bones[bone]
                pb.scale = (1.0, 1.0, 1.0)
                pb.keyframe_insert(data_path="scale", frame=f)
                if bone != "hips":
                    pb.location = (0.0, 0.0, 0.0)
                    pb.keyframe_insert(data_path="location", frame=f)

    fcurves = []
    if hasattr(action, "layers") and len(action.layers):
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    fcurves.extend(bag.fcurves)
    else:
        fcurves = list(action.fcurves)
    for fc in fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'

    # check the loop actually closes, rather than trusting the maths
    worst = 0.0
    for fc in fcurves:
        kps = fc.keyframe_points
        if len(kps) > 2:
            worst = max(worst, abs(kps[0].co[1] - kps[-1].co[1]))

    rig.animation_data.action = None
    print("   %-9s %4.1fs  %3d frames  %3d fcurves  loop gap %.6f%s" % (
        name, seconds, frames, len(fcurves), worst, "  <-- NOT CLOSED" if worst > 1e-5 else ""))
    return action


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args(argv)
    if not args.all:
        raise SystemExit("pass --all")

    for ob in list(bpy.data.objects):
        try:
            bpy.data.objects.remove(ob, do_unlink=True)
        except (RuntimeError, ReferenceError):
            pass

    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=RIG_SRC)
    new = [o for o in bpy.data.objects if o not in before]

    rig = [o for o in new if o.type == 'ARMATURE'][0]
    # the character mesh is not wanted here; this file is clips only
    for o in [o for o in new if o.type == 'MESH']:
        bpy.data.objects.remove(o, do_unlink=True)
    # and neither are the imported Idle/Talk/Walk - they already ship on every character
    for act in list(bpy.data.actions):
        bpy.data.actions.remove(act, do_unlink=True)
    if rig.animation_data:
        rig.animation_data_clear()
    rig.animation_data_create()

    bpy.context.scene.render.fps = FPS
    bpy.context.view_layer.objects.active = rig
    print("rig: %s, %d bones, fps=%d" % (rig.name, len(rig.data.bones), FPS))

    actions = [(name, build(rig, name, fn, secs), note) for name, fn, secs, note in CLIPS]

    # one NLA track per action, which is how the source file stores its three
    rig.animation_data.action = None
    for name, action, _ in actions:
        track = rig.animation_data.nla_tracks.new()
        track.name = name
        track.strips.new(name, 1, action)

    if args.dry_run:
        print("dry run - not exported")
        return

    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig

    props = bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
    kwargs = {k: v for k, v in dict(
        filepath=OUT, export_format='GLB', use_selection=True,
        export_animations=True, export_animation_mode='ACTIONS',
        export_skins=True, export_apply=False, export_yup=True,
    ).items() if k in props}
    bpy.ops.export_scene.gltf(**kwargs)
    print("\nwrote %s (%.0f KB)" % (OUT, os.path.getsize(OUT) / 1024))
    for name, _, note in actions:
        print("   %-9s %s" % (name, note))


if __name__ == "__main__":
    main()
