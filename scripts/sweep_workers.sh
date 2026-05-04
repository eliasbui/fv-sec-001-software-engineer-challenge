#!/usr/bin/env bash
# Measure AdPerformance aggregation time across a range of --workers values.
# Reports min/median/max over N trials per setting so a single noisy run
# can't mislead.
set -euo pipefail

BIN="${BIN:-./publish/AdPerformance}"
INPUT="${INPUT:-./data/ad_data.csv}"
OUTDIR="${OUTDIR:-./output}"
TRIALS="${TRIALS:-5}"
WORKERS="${WORKERS:-1 2 4 6 8 9 10 11 12 16}"

if [[ ! -x "$BIN" ]]; then
  echo "binary not found or not executable: $BIN" >&2
  exit 1
fi
if [[ ! -f "$INPUT" ]]; then
  echo "input not found: $INPUT" >&2
  exit 1
fi

# Pre-warm page cache so we measure CPU work, not cold disk reads.
echo "warming page cache..." >&2
cat "$INPUT" > /dev/null
cat "$INPUT" > /dev/null

printf "%8s %8s %8s %8s %8s %8s\n" "workers" "min" "median" "max" "stdev" "trials"
printf "%8s %8s %8s %8s %8s %8s\n" "-------" "------" "------" "------" "------" "------"

for w in $WORKERS; do
  samples=""
  for t in $(seq 1 "$TRIALS"); do
    out=$("$BIN" -i "$INPUT" -o "$OUTDIR" -w "$w" 2>&1 || true)
    secs=$(echo "$out" | awk -F'00:00:' '/Aggregation complete/ {print $2}' | awk '{print $1}')
    if [[ -z "$secs" ]]; then
      echo "  trial $t: failed to parse output" >&2
      continue
    fi
    samples="$samples $secs"
  done

  python3 - "$w" $samples <<'PY'
import sys
import statistics

w = int(sys.argv[1])
vals = [float(x) for x in sys.argv[2:]]
if not vals:
    print(f"{w:8d} (no samples)")
    sys.exit(0)
vals.sort()
mn = vals[0]
mx = vals[-1]
med = statistics.median(vals)
sd = statistics.stdev(vals) if len(vals) > 1 else 0.0
print(f"{w:8d} {mn:8.3f} {med:8.3f} {mx:8.3f} {sd:8.3f} {len(vals):8d}")
PY
done
