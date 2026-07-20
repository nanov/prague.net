# Leak-Audit Follow-ups — PR Series Roadmap

**Context:** PR #23 (pool leak-safety) closed every confirmed ArrayPool leak/double-return and
added the `PragueArrayPool`/`TrackingArrayPool` harness with 28 leak tests. This roadmap covers
every residual issue from that audit, split into six independently mergeable PRs.

**Hard constraint (applies to every PR):** no performance regression on hot paths.

## Global performance gates (every PR)

- Any change to `src/Prague.Core` hot paths merges only with a BenchmarkDotNet short-job
  before/after run on the benchmarks named in its plan: means inside error bars, **allocation
  columns byte-identical**.
- Memory-safety guards are **Debug-only** (`Debug.Assert`) — zero Release cost (user decision
  2026-07-20).
- New/changed rent sites get leak tests (`LeakAssert.Balanced`) per `context/collections.md`.
- Full `Prague.Tests.slnf` green on net9.0 + net10.0 before merge.
- House skills apply: `code-style`, `high-performance-net`.

## PR series

| PR | Branch | Scope | Detailed plan |
|----|--------|-------|---------------|
| 1 | `fix/valuedictionary-filter-cursor` | Delegate `Filter` read-cursor bug; Debug asserts for ValueSet use-after-dispose + ValueDictionary over-capacity Add | `2026-07-20-pr1-valuedictionary-correctness.md` |
| 2 | `fix/joined-count-and-nested-candidates` | Inner-join `Count()` narrowing (Count == Execute count); nested-join seeded-candidates leak window | `2026-07-20-pr2-joined-count-and-nested-candidates.md` |
| 3 | `chore/dead-code-and-sortedarrayset` | Delete `TopKHeap` + `SeekAndTakePooled` (zero callers); harden `SortedArraySet` enumerator (double-Dispose, boxed-path refcount) | `2026-07-20-pr3-dead-code-and-sortedarrayset.md` |
| 4 | `test/generated-leak-harness` | Extend the tracking-pool harness to `Prague.Generated.Tests` (shared Infrastructure via link or file copy + global `SetUpFixture` + Balanced scenarios through codegen'd caches: pooled query, FK `JoinWith`, empty result, throwing filter) | authored at PR start |
| 5 | `test/di-coverage` | `Prague.DI.Tests` has zero test files — add registration/resolution coverage for the DI surface | authored at PR start |
| 6 | `chore/pooledbtree-defensive-hardening` | Debug-fail-fast on the `?? leaf` fallback (PooledBTree.cs:1101/1103) and `RepairPath` walk-guard trip; document the Dispose no-concurrent-use contract and ReaderGate pull-based drain in `context/collections.md` | authored at PR start |

## Explicitly deferred / accepted as-is

- **OOM windows** between consecutive rents in multi-array ctors (`Tables`, B-tree nodes,
  `ValueDictionary`, Kafka producer's second writer rent): not user-triggerable; guarding them
  costs real code on hot ctors for no practical risk. Accepted.
- **Struct-copy double-dispose** (`ValueSet`, `QueryResults` + `Slice`) and **no finalizer
  backstops** for forgotten Dispose: caller contracts, documented in `context/collections.md`.
  Not preventable in structs without perf cost.
- **ReaderGate pull-based drain** (limbo persists absent activity): by design; PR-6 documents it.
- **`ValueDictionary.Intersect`/`Filter` partial-compaction state after a mid-rebuild throw**:
  corruption-not-leak, reachable only via a throwing user comparer inside an already-doomed
  query; the containing query's cleanup (PR #23) returns the arrays. Documented, not fixed.

## Decisions taken (user, 2026-07-20)

1. `Count()` on inner joins **narrows** — must match Execute's row count (bench-gated).
2. **Delete** `TopKHeap` and `SeekAndTakePooled`; **keep + harden** `SortedArraySet` (public).
3. Memory guards are **Debug-only** — no Release-mode checks.

## Ordering

PR-1 → PR-2 → PR-3 are independent of each other but ordered by user impact (live correctness
bug first). PR-4 depends on nothing but is most valuable after PR-2 (covers the new Count path
through codegen). PR-5/PR-6 are independent, any time.
