# Query execution

> **Read when:** working on the query builder, candidate narrowing/intersection, OR clauses, pooled results, or the query-string API.

## Builder & intersection

- `Query()` returns a fluent builder. Each `WithXxx(...)` / `UseIndex(...)` adds a candidate-narrowing lane; the runtime intersects lanes on a **stackalloc bitmap** and short-circuits on the first empty set — no further index walks, no allocations.
- Discriminators gate which operations are callable at compile time: `ExecutableQuery<TCache>` (full surface), `NonExecutableQuery<TCache>` (join filter callbacks — `WithXxx`/`UseIndex`/`Or` only, `Execute*` hidden), `NarrowOnlyQuery<TCache>` (OR branch lambdas — narrowing only). `AsNonExecutable()` (internal) swaps `Executable → NonExecutable` preserving other generics.

## Execution flavors

- `Execute()` — allocating.
- `ExecutePooled()` — returns a disposable `QueryResults<T>` the caller **must `Dispose()`**. Default for hot paths.
- `QueryResults<T>` has a **ref enumerator**: `Enumerator.Current` is `ref T`, so `foreach (ref var x in results)` mutates the backing array in place. `IEnumerator<T>.Current` stays an explicit by-value impl so the interface contract is unchanged.

## OR clause — disjunctive narrowing

- `.Or(b1, b2)` and `.Or(b1, b2, arg)` — UNION-style candidate narrowing. Branch lambdas receive a `NarrowOnlyQuery<TCache>` discriminator.
- Protocol: bitmap mark-and-prune via `IncrementalIntersecter` ([`collections.md`](collections.md)); cross-branch UNION via `BitHelper` SIMD `Vector<int>` ops; survivors pruned by `RetainOnly`. **Cost scales with surviving candidates, not index-result size.** No-op branches (`q => q`) are detected and excluded.
- Works inside `JoinOne` filter callbacks (paired core uses a hybrid: `IncrementalIntersecter` per-branch, `ValueSet` merge cross-branch — pairs dedup by `.Key`). Orchestrated by `IOrCapable` on both the unpaired and paired cores.
- Canonical refs: README "OR Queries", `docs/superpowers/specs/2026-05-19-or-query-clause-design.md`.

## Query-string API

Codegen emits `TryApplyParam`, `ApplyFilter`, `StringQueryInternal` per cache (string-keyed dynamic filtering), all using the `ExecutableQuery<{cacheClassName}>` discriminator.

## Related

Index lanes & bulk-intersect primitives: [`indexes.md`](indexes.md). Joins build on the paired variant of this builder: [`joins.md`](joins.md).
