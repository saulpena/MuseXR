#!/usr/bin/env bash
# One performance test on a Quest, identical every time.
#
#   Tools/perf/measure_quest.sh <apk> <test-id> "<one-variable description>" [world-key]
#
# Installs the APK (checksum-verified), launches it in CAPTURE MODE (fixed view, head tracking
# paused -- do not wear the headset), optionally in a chosen world, then:
#   - screenshot + GPU time split at the default splat scale
#   - FPS / GPU time at every splat scale, stepped with the app's F9 binding over adb
#   - relaunches normally so the headset is safe to wear again
# and appends one CSV row per scale to Tools/perf/results.csv.
#
# It refuses to report if the app is not the foreground activity: with a system dialog up
# (controller required, tracking lost, sensor lock) every counter reads the system shell.
set -u
APK="$1"; ID="$2"; VAR="$3"; WORLD="${4:-}"
PKG=com.musexr.impossiblemuseum
ACT=$PKG/com.unity3d.player.UnityPlayerActivity
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/Captures/perf"; CSV="$ROOT/Tools/perf/results.csv"
mkdir -p "$OUT"
die() { echo "ABORT: $*" >&2; exit 1; }

adb get-state >/dev/null 2>&1 || die "no headset on adb"
MODEL=$(adb shell getprop ro.product.model | tr -d '\r')
[ -f "$APK" ] || die "no such apk: $APK"
LOCAL_MD5=$(md5sum "$APK" | cut -c1-32)
adb install -r "$APK" >/dev/null || die "install failed"
P=$(adb shell pm path $PKG | sed 's/package://;s/\r//')
[ "$(MSYS_NO_PATHCONV=1 adb shell md5sum "$P" | cut -c1-32)" = "$LOCAL_MD5" ] || die "installed apk checksum differs"
echo "installed $(basename "$APK") ($LOCAL_MD5) on $MODEL"

adb shell input keyevent KEYCODE_WAKEUP
adb shell am force-stop $PKG
adb logcat -c
EXTRA="--ez musexr.capturePose true"; [ -n "$WORLD" ] && EXTRA="$EXTRA --es musexr.homeWorld $WORLD"
MSYS_NO_PATHCONV=1 adb shell am start -n $ACT $EXTRA >/dev/null
sleep 20
adb shell dumpsys activity activities | grep -q "topResumedActivity=.*$PKG" \
    || die "app is not in front: $(adb shell dumpsys activity activities | grep -m1 topResumedActivity)"
LOG=$(adb logcat -d -s Unity:I)
echo "$LOG" | grep -aq "CapturePose\] ON" || die "capture mode did not engage"
LOADED=$(echo "$LOG" | grep -a "\[WorldCycler\]" | tail -1 | sed 's/.*\[WorldCycler\] //')
PROPS=$(echo "$LOG" | grep -a "\[WorldProps\]" | tail -1 | sed 's/.*\[WorldProps\] //')
echo "world: $LOADED"; [ -n "$PROPS" ] && echo "props: $PROPS"

scale() { adb logcat -d -s Unity:I | grep -a "\[SplatScale\]" | tail -1 | grep -ao "layer [0-9.]*" | awk '{print $2}'; }
sample() {  # prints: fps_avg fps_min fps_max gpu_ms
  adb logcat -c
  timeout 10 adb logcat -s VrApi:I 2>/dev/null | grep -ao "FPS=[^,]*\|App=[^,]*" | paste -sd' ' | sed 's/FPS=/\nFPS=/g' | tail -6 |
  awk '{split($1,f,"[=/]"); split($2,a,"[=m]"); if(f[2]!=""){s+=f[2]; m+=a[2]; n++; if(lo==""||f[2]+0<lo)lo=f[2]+0; if(f[2]+0>hi)hi=f[2]+0}} END{if(n)printf "%.0f %d %d %.1f", s/n, lo, hi, m/n; else printf "NA NA NA NA"}'
}

adb exec-out screencap -p > "$OUT/$ID.png"
SPLIT=$(timeout 9 adb shell "ovrgpuprofiler -r=\"31,24,42\"" 2>&1 | tr -d '\r' | awk -F: '/Fragments/{f=$2}/Vertices/{v=$2}/Compute/{c=$2} END{gsub(/ /,"",f);gsub(/ /,"",v);gsub(/ /,"",c); printf "frag %.0f%% vert %.0f%% compute %.0f%%", f, v, c}')
echo "gpu split at default scale: $SPLIT"

COMMIT=$(git -C "$ROOT" rev-parse --short HEAD)
[ -f "$CSV" ] || echo "test,date,commit,apk_md5,device,world,splat_scale,fps_avg,fps_min,fps_max,gpu_ms,variable" > "$CSV"
DATE=$(date +%Y-%m-%dT%H:%M)
declare -A SEEN
for i in 1 2 3 4 5 6; do
  S=$(scale); [ -z "$S" ] && S="?"
  if [ -z "${SEEN[$S]:-}" ]; then
    SEEN[$S]=1
    read FA FL FH G < <(sample)
    printf "  splat %-5s FPS %s (%s-%s)  GPU %s ms\n" "$S" "$FA" "$FL" "$FH" "$G"
    echo "$ID,$DATE,$COMMIT,$LOCAL_MD5,$MODEL,${WORLD:-default},$S,$FA,$FL,$FH,$G,\"$VAR\"" >> "$CSV"
  fi
  adb shell input keyevent KEYCODE_F9; sleep 3
done

adb shell am force-stop $PKG; adb logcat -c
adb shell am start -n $ACT >/dev/null; sleep 12
adb logcat -d -s Unity:I | grep -aq "CapturePose\] ON" && die "normal relaunch still in capture mode"
echo "relaunched normally (head tracking on). screenshot: Captures/perf/$ID.png"
