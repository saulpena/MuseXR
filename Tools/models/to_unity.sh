#!/usr/bin/env bash
# A generated model into the project: finish it in Blender (real size, base origin, front +Z, textures capped),
# then unpack it into Assets/Resources/<folder>/<name>.gltf with its images as separate files, so Unity
# compresses them for Android (inside a .glb, glTFast keeps them uncompressed: 89.5 MB for one 20k statue).
#
#   bash to_unity.sh <in.glb> <name> <folder> '<prop_finish spec json>'
#   e.g. bash to_unity.sh out/props/plinth/v1/plinth.glb plinth Props '{"height":1.0,"tex":1024,"tex_mr":512}'
#
# Prints the finish measurements (triangles, size, textures). A <in.glb> that is already finished
# (out/props/_finished/...) can skip Blender with spec '-'.
set -e
cd "$(dirname "$0")"
in="$1"; name="$2"; folder="$3"; spec="$4"
B="/c/Program Files/Blender Foundation/Blender 5.1/blender.exe"
if [ "$spec" != "-" ]; then
  "$B" -b --factory-startup -P prop_finish.py -- "$in" "out/_unity/$name" "$name" "$spec" 2>&1 | grep -E "^(triangles|size metres|textures|exported)|FINISH|Error" || true
  in="out/_unity/$name/$name.glb"
fi
node unpack.cjs "$in" "../../Assets/Resources/$folder/$name.gltf"
