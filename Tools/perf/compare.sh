#!/usr/bin/env bash
# Compare a measured test against a baseline, scale by scale, from Tools/perf/results.csv.
#
#   Tools/perf/compare.sh <baseline-id> <test-id>
#
# GPU ms is the judged number (FPS is quantised to 72/48/36 and hides small changes).
# A delta beyond the tolerance is flagged; the tolerance is the run-to-run noise measured by
# re-running an unchanged build (T10 vs T11: every scale within 0.6 ms).
# Exit status 1 if anything regressed, so it can gate a script.
set -u
BASE="$1"; TEST="$2"; TOL="${TOL:-0.7}"
CSV="$(cd "$(dirname "$0")" && pwd)/results.csv"
awk -F, -v b="$BASE" -v t="$TEST" -v tol="$TOL" '
  $1==b { bw=$6; bg[$7]=$11; bf[$7]=$9"-"$10 }
  $1==t { tw=$6; tg[$7]=$11; tf[$7]=$9"-"$10 }
  END {
    if (!length(bg)) { print "no rows for baseline " b; exit 2 }
    if (!length(tg)) { print "no rows for test " t; exit 2 }
    if (bw != tw) printf "WARNING: different worlds (%s vs %s)\n", bw, tw
    printf "%s vs %s  (%s, tolerance %.1f ms)\n", t, b, tw, tol
    printf "  scale  baseline            test                delta\n"
    n = split("0.60 0.70 0.80 0.90 1.00", s, " "); bad = 0
    for (i = 1; i <= n; i++) {
      k = s[i]; if (!(k in bg) || !(k in tg)) continue
      d = tg[k] - bg[k]
      v = d > tol ? "REGRESSED" : (d < -tol ? "faster" : "same")
      if (d > tol) bad = 1
      printf "  %s   %5.1f ms  %-7s   %5.1f ms  %-7s   %+5.1f  %s\n", k, bg[k], bf[k] " fps", tg[k], tf[k] " fps", d, v
    }
    print (bad ? "VERDICT: REGRESSION" : "VERDICT: NO REGRESSION")
    exit bad
  }' "$CSV"
