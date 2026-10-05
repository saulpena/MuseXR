#!/usr/bin/env bash
# Run generator jobs one at a time. The plan allows ONE pending task per account (HTTP 429 "NoMorePendingTasks"),
# and the account is shared, so a job refused that way waits and is sent again: a 429 creates no task and
# bills nothing. Any other failure stops that job and is never retried (a failed paid task is not re-billed).
#
#   bash queue.sh jobs.txt logdir        one model_master.py argument line per job; '#' lines skipped
cd "$(dirname "$0")"
jobs="$1"; logs="$2"; mkdir -p "$logs"
while IFS= read -r line; do
  [[ -z "$line" || "$line" == \#* ]] && continue
  name=$(echo "$line" | awk '{print $2"-"$3}')
  for attempt in $(seq 1 60); do
    python model_master.py $line > "$logs/$name.log" 2>&1
    if grep -q "NoMorePendingTasks" "$logs/$name.log"; then echo "[queue] $name: account busy, waiting"; sleep 30; continue; fi
    break
  done
  tail -1 "$logs/$name.log" | sed "s/^/[queue] $name: /"
done < "$jobs"
echo "[queue] all done"
