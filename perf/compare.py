#!/usr/bin/env python3
import json, sys, argparse, datetime, glob, os

def tolerance(mid, thr):
    for suf, t in thr.get("bySuffix", {}).items():
        if mid.endswith(suf): return t
    best = None
    for pre, t in thr.get("byPrefix", {}).items():
        if mid.startswith(pre.rstrip("*")): best = t
    return best if best is not None else thr.get("default", 0.08)

def load(path):
    with open(path) as f: return json.load(f)

def regressed(mid, cur, base, higher_is_better, tol):
    if base == 0: return False, 0.0
    delta = (cur - base) / base
    signed = delta if higher_is_better else -delta   # positive = improvement
    return (signed < -tol), delta

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--machine", required=True)
    ap.add_argument("--configs", nargs="+", required=True)
    ap.add_argument("--out-dir", default="perf/out")
    ap.add_argument("--baseline-dir", default="perf/baseline")
    ap.add_argument("--thresholds", default="perf/thresholds.json")
    ap.add_argument("--bless", action="store_true")
    args = ap.parse_args()

    thr = load(args.thresholds)
    base_path = os.path.join(args.baseline_dir, args.machine + ".json")
    baseline = load(base_path) if os.path.exists(base_path) else {"configs": {}}
    baseline.setdefault("configs", {})

    any_regression = False
    for config in args.configs:
        cur = load(os.path.join(args.out_dir, config + ".json"))
        cur_metrics = {m["id"]: m for m in cur["metrics"]}
        if args.bless:
            baseline["configs"][config] = {
                "env": cur["env"], "commit": cur["commit"], "timestampUtc": cur["timestampUtc"],
                "metrics": cur["metrics"],
            }
            print(f"[bless] {config}: {len(cur['metrics'])} metrics")
            continue
        base_metrics = {m["id"]: m for m in baseline["configs"].get(config, {}).get("metrics", [])}
        print(f"\n== {config} ({args.machine}) ==")
        print(f"{'metric':32} {'baseline':>14} {'current':>14} {'delta':>9} {'tol':>6}  status")
        for mid, m in sorted(cur_metrics.items()):
            b = base_metrics.get(mid)
            if b is None:
                print(f"{mid:32} {'-':>14} {m['value']:>14.2f} {'NEW':>9}")
                continue
            tol = tolerance(mid, thr)
            reg, delta = regressed(mid, m["value"], b["value"], m["higherIsBetter"], tol)
            status = "REGRESSED" if reg else "ok"
            if reg: any_regression = True
            print(f"{mid:32} {b['value']:>14.2f} {m['value']:>14.2f} {delta*100:>8.1f}% {tol*100:>5.0f}% {status}")

    if args.bless:
        with open(base_path, "w") as f: json.dump(baseline, f, indent=2)
        write_rollup(args.baseline_dir)
        print(f"[bless] wrote {base_path}")
        return 0
    return 1 if any_regression else 0

def write_rollup(baseline_dir):
    lines = ["# Prague performance baseline\n",
             f"_Generated {datetime.datetime.utcnow().isoformat()}Z_\n"]
    for path in sorted(glob.glob(os.path.join(baseline_dir, "*.json"))):
        data = load(path)
        machine = os.path.splitext(os.path.basename(path))[0]
        lines.append(f"\n## {machine}\n")
        for config, block in sorted(data.get("configs", {}).items()):
            env = block.get("env", {})
            lines.append(f"\n### {config}  \n`{env.get('cpu','?')}` · `{env.get('os','?')}` · "
                         f"`{env.get('dotnet','?')}` · commit `{block.get('commit','?')}`\n")
            lines.append("| metric | value | unit |\n|---|---:|---|\n")
            for m in block.get("metrics", []):
                lines.append(f"| {m['id']} | {m['value']:.2f} | {m['unit']} |\n")
    with open("perf/BASELINE.md", "w") as f: f.writelines(lines)

if __name__ == "__main__":
    sys.exit(main())
