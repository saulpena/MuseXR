#!/bin/bash
# render.sh <master> <tag> [glb-name]   -> out/_renders/<master>-<tag>/sheet.jpg
m=$1; t=$2; g=${3:-$m-textured.glb}
H="C:/Git/PicoHackathon/MuseXR/Tools/models"
B="/c/Program Files/Blender Foundation/Blender 5.1/blender.exe"
"$B" -b --factory-startup -P "$H/render_turntable.py" -- "$H/out/$m/$t/$g" "$H/out/_renders/$m-$t" 2>&1 | grep -E "RENDER_DONE|Traceback|Error:"
python "$H/make_sheet.py" "$H/out/_renders/$m-$t" "$m $t" "C:/Git/PicoHackathon/muse-infinity/assets/generated/turnarounds/views/$m/front.png"
