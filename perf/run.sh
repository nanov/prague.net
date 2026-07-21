#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."          # repo root
TARGET="${1:-all}"; shift || true
BLESS=""; MACHINE="${PRAGUE_PERF_MACHINE:-}"
while [ $# -gt 0 ]; do
  case "$1" in
    --bless) BLESS="--bless" ;;
    --machine) MACHINE="$2"; shift ;;
  esac; shift
done
export PRAGUE_PERF_MACHINE="${MACHINE:?set --machine or PRAGUE_PERF_MACHINE}"
export PRAGUE_PERF_COMMIT="$(git rev-parse --short HEAD)"
TFM=net9.0
CONFIGS=()

run_core() { dotnet run -c Release --project perf/Prague.Baseline.Bdn --framework $TFM -- --filter '*Core*'; CONFIGS+=(core-only); }
run_sim()  { dotnet run -c Release --project perf/Prague.Baseline.Harness --framework $TFM -- --config full-sim --runs 3; CONFIGS+=(full-sim); }
run_real() { dotnet run -c Release --project perf/Prague.Baseline.Harness --framework $TFM -- --config full-real --runs 3; CONFIGS+=(full-real); }
run_concurrent() { dotnet run -c Release --project perf/Prague.Baseline.Harness --framework $TFM -- --config concurrent --runs 3; CONFIGS+=(concurrent); }

case "$TARGET" in
  core) run_core ;;
  sim)  run_sim ;;
  real) run_real ;;
  concurrent) run_concurrent ;;
  all)  run_core; run_sim; run_concurrent ;;   # 'all' = core + sim + concurrent (real is opt-in)
  *) echo "usage: run.sh core|sim|real|concurrent|all [--bless] [--machine <class>]"; exit 2 ;;
esac

python3 perf/compare.py --machine "$PRAGUE_PERF_MACHINE" --configs "${CONFIGS[@]}" $BLESS
