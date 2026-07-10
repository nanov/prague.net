# PR56 perf-neutrality gate — baseline (`main` a9b0e63) vs fix branch

Apple M4 Pro, .NET 9.0.17, BenchmarkDotNet `--job short`, `[MemoryDiagnoser]`.
Baseline ran in a `main` worktree with the identical benchmark files copied in.

## PooledBTree (`BTreeChurnBenchmarks`, Items = 1000)

| Row | Baseline | Fix | Delta | Verdict |
|---|---|---|---|---|
| UpdateChurn_UniqueKeys (1000 add+remove) | 71.6 µs | 76.4 µs | +6.7% (≈ +2.4 ns per add+remove pair; error bars overlap: baseline ±18 µs) | parity within noise |
| UpdateChurn_SharedKey | 162 µs / 36 KB alloc | 418 µs / 1.75 KB | n/a | **not comparable**: baseline silently leaks (Remove returns false, tree grows unboundedly — the 36 KB/op is the runaway growth). Fix does correct cross-leaf DFS removes. This row is the price of *correct* behavior on the incident workload. |
| RangeFrom_ScanAll (1000 items) | 298 ns | 717 ns | +0.42 ns/item | see analysis |
| RangeFromExclusive_ScanAll | 298 ns | 794–869 ns | +0.5 ns/item | see analysis |
| Contains_HitAndMiss (256 lookups) | 2.40 µs | 3.43 µs | +4.0 ns/lookup | see analysis |

## PooledSet (`PooledSetBenchmarks`)

| Row | Baseline | Fix | Delta | Verdict |
|---|---|---|---|---|
| AddRemoveChurn (256 ops) | 405–417 ns | 421–442 ns | +0.07 ns/op | parity within noise (writer-side `_tables` indirection + ordered publication) |
| Contains_HitAndMiss (256 lookups) | 239–266 ns | 1.04–1.11 µs | +3.3 ns/lookup | see analysis |
| EnumerateStruct (100 / 5000) | 41.0 ns / 1.63 µs | 40.8 ns / 1.77 µs | 0 / +0.03 ns/item | parity |
| EnumerateBoxed (100 / 5000) | 196 ns / 10.0 µs | 223 ns / 10.9 µs | +0.3 ns/item; alloc 32 B → 56 B | parity-ish (larger enumerator object; it now carries its safety pin) |
| CreateAddDispose_SingletonBucket (16 buckets) | 358 ns / 1280 B | 906 ns / 1792 B | +34 ns and +32 B per bucket | accepted: the +32 B is the `Tables` generation object; the +34 ns is the gate hand-off (lock + list + fence). Both per bucket *lifecycle*, not per message on existing buckets. |

Zero-allocation status preserved on every read/scan/churn row (`Allocated = -`).

## Analysis of the three non-noise deltas

All three are the direct, measured cost of the new safety contract (readers can never
observe recycled pooled memory; scans/lookups are torn-read-safe), not incidental
implementation loss:

1. **+3–4 ns per `Contains`** (both structures): one ReaderGate pin/unpin — a padded
   thread-local store pair plus one local full fence (`dmb ish`) — and one non-inlined
   call that keeps the chain walk outside the wrapper's EH region. The baseline lookup
   was ~1 ns and completely unsynchronized.
2. **+0.4–0.5 ns per scanned item on `Range*`**: the gated wrapper is a call boundary,
   so the by-ref aggregator's accumulator lives in memory instead of a register — an
   artifact magnified by the microbenchmark's empty `Sum +=` body. Realistic
   aggregators (pooled result sets, list materialization) are memory-bound in `Add`
   already, so their relative delta is far smaller. Per-leaf acquire reads of `Count`
   (which close a real weak-memory hole that served pool dirt) are included in this
   number. Alternatives tried and measured: `AggressiveInlining` of the core into the
   EH wrapper (same 785 ns) and tiered-PGO vs `AggressiveOptimization` (±10%). The
   residual is irreducible without dropping the exception-safe unpin, which would let
   one throwing user comparator permanently stall process-wide reclamation.
3. **+34 ns / +32 B per bucket create→dispose**: `Tables` generation object plus gate
   hand-off. Amortized-seal variants were tried and REVERTED: deferring the seal
   starved the ArrayPool (arrays parked in limbo while fresh ones allocated — 3.9 µs
   and +4.7 KB per 16 buckets at the worst point). Seal-per-drain with the cheap local
   fence keeps the pool round-trip tight.

## Bugs found by this gate's stress companion (fixed on the branch)

- `InsertIntoLeaf` published `Count++` before the `Values[pos]` store (plain stores) —
  on arm64 a reader could scan one slot past the live range and serve dirt from the
  pooled array. Pre-existing; invisible on x64.
- Split leaves / new roots / promoted children were linked with plain stores — a reader
  could observe the pointer before the freshly rented arrays' contents. Pre-existing;
  upstream documents this hole and left it open ("split publication is not fenced").
- `Interlocked.MemoryBarrierProcessWide` is not a reliable remote-store-buffer drain on
  every platform (observed corruption on macOS/arm64); the gate uses symmetric local
  fences instead.

## Verdict

- Writer paths (the Kafka hot path): parity within noise.
- Read paths: zero-alloc preserved; single-digit-ns absolute overhead per operation as
  the measured price of the no-recycled-memory guarantee under lock-free concurrency.
- Pooling: fully preserved (bucket lifecycle round-trips through ArrayPool; B-tree
  nodes repool after grace).
