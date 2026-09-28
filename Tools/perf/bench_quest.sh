#!/usr/bin/env bash
# Quality benchmark on a Quest, from the desk: install the Bench APK, launch it in capture mode
# (fixed view, head tracking paused), and for every version record FPS and GPU ms from VrApi and
# a screenshot, at splat resolution 0.6 (shipped) and 1.0 (full).
#
#   Tools/perf/bench_quest.sh Builds/MuseXR-Quest-Bench.apk <out_dir> [versions=9] [settle=12]
#
# Version order is QualityBenchCatalog.All minus anything not built. N = next version, F9 = cycle
# the splat resolution 1.0 -> 0.9 -> 0.8 -> 0.7 -> 0.6 -> 1.0, starting at 0.6.
set -euo pipefail
APK=$1; OUT=$2; N=${3:-9}; SETTLE=${4:-12}
PKG=com.musexr.impossiblemuseum
ACT=$PKG/com.unity3d.player.UnityPlayerActivity
mkdir -p "$OUT"
adb install -r "$APK" >/dev/null
adb shell am force-stop $PKG
adb logcat -c
adb shell am start -n $ACT --ez musexr.capturePose true >/dev/null
sleep 25   # first world load

sample() {   # $1 = tag. Averages the last SETTLE seconds of VrApi, then a screenshot.
  adb logcat -c; sleep "$SETTLE"
  local vr bench
  vr=$(adb logcat -d -s VrApi:I | grep -o "FPS=[0-9]*/[0-9]*\|App=[0-9.]*ms" | tail -8 | tr '\n' ' ')
  bench=$(adb logcat -d -s Unity:I | grep "\[Bench\]" | tail -1 | sed 's/.*\[Bench\] //')
  adb exec-out screencap -p > "$OUT/$1.png"
  echo "$1 | $bench | $vr" | tee -a "$OUT/results.txt"
}

for i in $(seq 1 "$N"); do
  sample "v${i}-res0.6"
  adb shell input keyevent KEYCODE_F9; sleep 2      # 0.6 -> 1.0
  sample "v${i}-res1.0"
  for k in 1 2 3 4; do adb shell input keyevent KEYCODE_F9; sleep 1; done   # back to 0.6
  adb shell input keyevent KEYCODE_N
  sleep 15   # load the next version (the 4.32M ones take longest)
done
adb shell am force-stop $PKG
echo "done: $OUT/results.txt"
