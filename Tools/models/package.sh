#!/bin/bash
# package.sh <master> <gen-tag> <rig-tag>  -> out/<master>/final/
m=$1; g=$2; r=$3; F=out/$m/final; mkdir -p $F
cp out/$m/$r/$m-rigged.fbx out/$m/$r/$m-rigged.glb $F/
cp out/$m/$r/$m-anim-*.glb $F/ 2>/dev/null
cp out/$m/$g/${m}_base_color_0.png $F/${m}_base_color.png
cp out/$m/$g/${m}_normal_0.png $F/${m}_normal.png
cp out/_renders/$m-$r/sheet.jpg $F/${m}_review_sheet.jpg
for a in walking running; do [ -f out/_renders/pose-$m-$r-$a/pose_report.txt ] && cp out/_renders/pose-$m-$r-$a/pose_report.txt $F/pose_check_$a.txt; done
echo "$g (generate task $(cut -d' ' -f2 out/$m/$g/task_id.txt)), rig $(cut -d' ' -f2 out/$m/$r/task_id.txt)" > $F/SOURCE.txt
python -c "
from PIL import Image;import glob
for f in glob.glob('$F/*.png'): print(f, Image.open(f).size)"
ls -la $F
