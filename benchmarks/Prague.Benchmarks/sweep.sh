#!/usr/bin/env bash
# Full frozen-query sweep: every category in FrozenQueryBenchmarks + FrozenFkJoinBenchmarks, one
# category per run (--inProcess, net9.0), the 1-minute load recorded before each, tables appended to
# $OUT. ~2 min a category; the whole sweep is ~2 h. Run with nothing else on the box.
#   benchmarks/Prague.Benchmarks/sweep.sh [out-file] [category ...]
set -u
cd "$(dirname "$0")/../.."
OUT="${1:-/tmp/prague-sweep-$(date +%Y%m%d-%H%M).md}"; shift || true
if [ $# -gt 0 ]; then CATS=("$@"); else
	mapfile -t CATS < <(grep -ho 'BenchmarkCategory("[^"]*")' benchmarks/Prague.Benchmarks/FrozenQueryBenchmarks.cs benchmarks/Prague.Benchmarks/FrozenFkJoinBenchmarks.cs | sed 's/.*("\(.*\)")/\1/' | awk '!seen[$0]++')
fi
dotnet build benchmarks/Prague.Benchmarks -c Release -f net9.0 >/dev/null || { echo "build failed" >&2; exit 1; }
{ echo "# Frozen sweep — $(date '+%Y-%m-%d %H:%M') — $(git rev-parse --short HEAD) — $(uname -m) $(sw_vers -productVersion 2>/dev/null)"; echo; } >> "$OUT"
for c in "${CATS[@]}"; do
	until [ -z "$(pgrep -f 'testhos[t]|bin/Release/net[0-9.]*/Prague.Benchmark[s]')" ]; do sleep 5; done
	load=$(uptime | sed 's/.*load averages*: *//')
	echo "## $c  (load $load, $(date +%H:%M))" >> "$OUT"
	dotnet run -c Release -f net9.0 --no-build --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories "$c" 2>&1 \
		| awk '/^\| Method/{p=1} p{print} /^$/{if(p)exit}' >> "$OUT"
	echo >> "$OUT"
	printf '%s done\n' "$c"
done
echo "sweep written to $OUT"
