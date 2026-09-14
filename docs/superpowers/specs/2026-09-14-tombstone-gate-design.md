# Tombstones beat ingress filters

Date: 2026-09-14
Status: Implemented — supersedes the tombstone row of [`2026-05-14-with-key-filter-design.md`](2026-05-14-with-key-filter-design.md)

## The bug

Two ingress gates ran *before* Prague recognised a tombstone, so a genuine Kafka delete could be swallowed, leaving an entry pinned in the cache that not even the producer could remove.

- **Key gate** — `_keyFilters.Evaluate(key)` in `DispatchRaw` ran before the `valueSpan.IsEmpty` checks, which were duplicated per phase further down. A `Skip` decision returned before either.
- **Header gate** — `IsHeaderFiltered` in `ConsumeRawLoop` ran before `DispatchRaw` entirely, so a header-filtered tombstone never reached the delete at all.

The value gates sat *after* the tombstone checks, which is why `WithValueFilter`'s documented rule — "tombstones skip the filter entirely and still remove the key" — was true, and why the contract was split against itself.

Exactly two of the twelve key-gate cells were broken, both `Skip × tombstone`. `Skip` is the default. `treatAsDelete: true` was accidentally correct: its branches already emitted the byte-identical calls the tombstone branches did.

On **load** the damage was worse than the #35 regression it resembles. #35 was "the delete only cancelled the buffer, so an already-flushed value survived", and the fix made `RemoveDuringLoad` do both. The `Skip` path called it not at all, so it failed on both halves: the pending buffered value survived *and* an already-flushed one did. The first case does not even need the mid-load flush — an unflushed value is committed at EOF.

The **header** case is the more reachable one, and it is first-party: `KafkaCacheProducer.Delete` stamps only `X-Producer-Id` and no user headers, and `KafkaHeaderExistsFilter.IsInitialFalse` drives `InitialState = false`, so a headerless tombstone never runs the loop body and is filtered. `WithHeaderExistsFilter("anything")` plus any Prague delete pinned the key permanently, in both phases, on every replay — with no mutable-state predicate and no third-party producer needed.

## The contract

A tombstone is the log's statement that the key is gone, and Prague always applies it. It is recognised before the key filter and before the value filter, in both phases, so no user predicate can suppress a delete.

Two ingress rules still stop a tombstone, both deliberately:

- **`HeaderGate.SelfProduced`** — a process never re-consumes its own writes, deletes included.
- **`HeaderGate.Rejected`** — a filter saw its header and rejected the value it carried. That is how a consumer selects a sub-stream of a shared topic; honouring a foreign tombstone there would let any producer evict another stream's key.

`HeaderGate.MissingRequiredHeader` is waived. It is the only reject reason that describes the message's *shape* rather than judging its content — a delete has no headers to satisfy a requirement with.

## The change

**`Filters/HeaderGate.cs`** (new) — `{ Accept, SelfProduced, Rejected, MissingRequiredHeader }`, so the call site can tell the three reject reasons apart. They were previously indistinguishable, which is what made the behaviour unfixable from the call site.

**`IO/KafkaCacheConsumer.cs`**

- `IsHeaderFiltered` → `EvaluateHeaderGate`, returning the reason. The `foreach` body is unchanged; only what the three `return`s carry changed.
- The call site waives exactly one reason for a tombstone:
  ```csharp
  if (gate != HeaderGate.MissingRequiredHeader || !raw.Value.IsEmpty) { …drop… }
  ```
  `raw.Value` is read inside the `gate != Accept` block, so an accepted message never touches it.
- `DispatchRaw` gains one tombstone check above the key gate, and loses the two per-phase copies below it. One decision in one place — #35 was one decision written twice.

- The gate call is now wrapped in a `try`/`catch` that logs and degrades to `HeaderGate.Rejected`, matching what the key and value gates have always done. It was the only gate touching raw wire bytes and user code with no handler, and the enclosing `catch` rethrows — so one throwing header predicate stopped the raw worker of *every* cache on the consumer, and the record replayed from the log into a crash loop. The catch is by origin, not by type: nothing inside the gate observes the consumer's token or talks to the broker, so anything thrown there is the user's predicate. An earlier draft excluded `KafkaException` and `OperationCanceledException` to let the loop's existing handlers see them — but that left a predicate able to impersonate either, stopping the loop with nobody having asked or latching the consumer fatal. Real shutdown is distinguished by `ct.IsCancellationRequested` instead. Mapped to `Rejected` rather than a waived reason on purpose: a throw must not become a way for a foreign tombstone to cross a sub-stream gate.

**`Filters/HeaderFilters.cs`** — `KafkaCombinedHeaderFilter.IsInitialFalse` aggregated with AND seeded `true`; it is now OR seeded `false`, matching how `KafkaHeaderFilters` aggregates the same flag. This is a prerequisite, not a drive-by: with AND, pairing `WithHeaderExistsFilter("h")` with any other filter on `"h"` silently dropped the exists requirement, which would have made the entire new tombstone behaviour unreachable for that configuration without any error. The redundant second array fill in the same constructor is gone.

### Hot path

`raw.Value` is read **inline** in `DispatchRaw` rather than hoisted into a local. A local there would be live-in to the key filter's `catch` handler, and RyuJIT then emits an EH write-thru stack home for it — paid by every message, including consumers with no key filters at all. Liveness is static over the flow graph, so "the branch is not taken at runtime" does not avoid it. The earlier draft of this fix hoisted the span and claimed zero delta; that claim was wrong.

The accepted path otherwise pays one extra `raw.Value.IsEmpty` test that replaces the one it used to pay further down. The header gate returns a `byte`-backed enum in the register the `bool` used.

## Declared behaviour changes

1. **Key filter, `Skip` + tombstone.** The key is now removed instead of the delete being dropped. Live: fires `UpdateType.Delete` (with the old value) when the key was resident, and **nothing at all** when it was not — where it previously fired `UpdateType.Filtered` in both cases. Load: removes the key where it previously did nothing. Downstream projectors counting `Filtered` will see the number move.

   A delete of an absent key is a provable total no-op — `InMemoryDataCache.Remove` returns before the index walk and before the statistics hook, `HandleRawLiveDelete` returns before the after-handler, and both of `RemoveDuringLoad`'s statements are no-ops when the key is absent. So "bypass the filter" and "bypass it only for admitted keys" are observationally identical, and the first needs no admission bookkeeping and no race with the unflushed load buffer.

2. **Header exists-filter + headerless tombstone.** Previously swallowed in both phases, now delivered. This is what makes Prague's own `KafkaCacheProducer.Delete` work for these consumers, and it is the largest observable change here.

3. **Key predicates stop seeing tombstones.** The hoist means a key predicate is no longer invoked for a null-value message. Observable to anyone whose predicate counts invocations or has side effects.

4. **A throwing key predicate no longer costs a tombstone.** The exception-to-`Skip` downgrade previously lost a delete on any transient predicate exception, even with `treatAsDelete: true`. The predicate no longer runs for tombstones, so the question does not arise.

5. **Combined header filters.** `WithHeaderExistsFilter("h")` plus another filter on `"h"` now still requires the header. It previously did not.

**Residual hole, accepted:** a headerless tombstone from *any* producer now passes an exists filter. Harmless for a key this cache never held. It bites only if two streams share a topic **and** a key space **and** the foreign producer omits the discriminating header on its tombstones. Such a producer is already broken, and the alternative — today's behaviour — breaks Prague's own producer unconditionally.

**Compound case worth stating once:** a consumer that shards by key filter *and* requires a header now lets a foreign headerless tombstone cross both gates at once. Changes 1 and 2 each cover half of it; that is the half an operator actually meets.

**New in this path:** a header-rejected tombstone now reaches `CacheSerde<TKey>.DeserializeFromSpan` where it previously died before the key was decoded. On a topic carrying a foreign sub-stream with an incompatible key encoding, that turns a silent `continue` into an Error-level `ErrorDeserializingKey` per tombstone.

## Invariant the waiver rests on

`KafkaHeaderExistsFilter` is the only executor with `IsInitialFalse => true`, so a false search state at the end of the loop can only mean "a required header never appeared". `KafkaHeaderNotExistsFilter` exists but has no builder method and is never constructed. **Adding another such executor breaks the waiver silently** — it would start admitting tombstones that explicitly violated a rule. Recorded on the `HeaderGate.MissingRequiredHeader` doc comment and pinned by `tests/Prague.Kafka.Tests/Filters/HeaderGateStateTests.cs`.

## Verification

`HeaderGateStateTests` (9 tests, no Docker) pins the `InitialState` / search-state invariant and the combined-filter regression; the combined-filter test was confirmed red against the old AND aggregation and green after.

**The behavioural fix is not verified locally.** `DualKafkaClusterFixture` is a namespace-wide `[SetUpFixture]` that starts two `cp-kafka` containers unconditionally, and Docker is not running on this machine, so the integration assembly cannot run at all — there is no partial run. The red-before-green integration cases are the only thing that actually proves the bug and the fix; run them before treating either as verified.

`RawMessage` / `RawHeaders` are byref-like with internal-only constructors and Confluent.Kafka grants `InternalsVisibleTo` to nobody, so no unit test can drive `DispatchRaw` or `EvaluateHeaderGate` directly. Extracting a span-taking seam would be a shape change to a hot method and belongs in its own PR with its own measurement.

## Not done — separate issues

Each verified independently after the fix landed; two of the original seven did not survive.

**Worth filing**

- **P2 — `CacheMarshall.Delete` / `KafkaCacheProducer.Delete` leave the producing process's own cache stale.** Wider than first thought: it is not delete-specific (`Produce` has the same shape), and it does not need `CacheMarshall` at all — `www/docs/.../kafka-integration.md`'s "## Producer" section teaches injecting `KafkaCacheProducer` and calling `Delete` directly, never mentions `RemoveAndProduce`, and never warns that the local cache is untouched. The safe alternative has a hole that pushes users back: `RemoveAndProduce` returns `false` without producing any tombstone when the key is not locally resident, so for a filtered cache there is no correct "tombstone this key" API at all. Bounded to the producing process's lifetime — `InstanceId` is a per-process `Guid`, so a restart re-applies the delete.
- **P2 — unbounded producer-controlled `stackalloc` in the header gate.** `KafkaHeaderFilters.ShouldProcess` does `stackalloc char[headerName.Length]` on the raw UTF-8 header name straight off the wire, unconditionally, *before* the dictionary lookup and even when zero header filters are configured. Kafka bounds a header key only by `message.max.bytes` (~1 MiB default), so a ~800 KiB header name is an uncatchable `StackOverflow` that kills the whole host — and the poison record replays from the log, so it is a crash loop. Fix: a length gate computed once at construction (two ordinally-equal strings have identical UTF-8 byte counts, so a name longer than the longest configured key can never match).

  The original claim's headline was **wrong** and is corrected here: the `stackalloc` is *not* inside a loop and the frame does *not* grow. It sits in a callee whose frame is reclaimed on return — measured, the buffer lands at the same address on every iteration. The hazard is the unbounded size, not accumulation.
- **P3 — `WithHeaderExistsFilter` composes as OR, not AND.** One shared search-state bool for the whole filter set, and `KafkaHeaderExistsFilter` sets it true without regard to which header resolved to it. Confirmed empirically: `exists("A") + exists("B")` with only `A` present returns `Accept` where the documented AND semantics require a drop. Distinct from the `KafkaCombinedHeaderFilter` bug fixed here, which was about multiple filters on the *same* name. Fix: a requirement bitmask instead of a bool.
- **P3 — no `IsEmpty` short-circuit on `KafkaHeaderFilters`.** Real but smaller than claimed: measured ~11.7 ns/header recoverable (13.4 → 1.7). The header *walk* is not avoidable — the producer self-filter must inspect every header regardless — so only the managed suffix inside each iteration can go. That is ~1.4–1.6 % of the 716–850 ns/entity managed ingest tail, not "a larger win than this patch costs". The guard must go inside the loop body after the self-filter test, never around the `foreach`.
- **P3 — dead code.** `KafkaHeaderNotExistsFilter` is never constructed anywhere in the tree, and `KafkaHeaderFilters.ShouldProcess(ref bool, string, ReadOnlySpan<byte>)` has no callers. Both orphaned by the raw zero-copy port. Both `internal`, so deleting them cannot break a downstream build. Worth doing because a type named "NotExists" sitting next to the exists filter is a trap for the next reader of the invariant this fix depends on.
- **P3 (docs only) — `UpdateType.Filtered` never carries a key.** Every `Filtered` callback receives `default(TKey)`. This is a uniform contract restated at three sites, not an accidental discard, so the fix is to document it — on the enum member and in `kafka-integration.md` — not to change it. For a reference-type `TKey` it also hands `null` to a non-nullable parameter.

**Found by the completeness sweep** — the header-gate `try`/`catch` was fixed here (see *The change*); this one is not:

- **`_cachesLoading` leaks on every post-load rebalance.** Incremented unconditionally per assigned partition in the partitions-assigned callback, which fires on every rebalance; the only decrement is guarded by a first-ever EOF. So any rebalance after the initial load pins the readiness probe to NOT READY permanently. The invariant currently holds only because a rebalance happens not to occur after load.

**Verified NOT bugs** — do not file:

- **Empty-payload conflation.** `raw.Value.IsEmpty` really is length-only, but a legitimate zero-byte value cannot exist on a Prague topic: MessagePack's smallest encoding is one byte (`nil`, `0xC0`), and `SpanMessagePackDeserializer` independently treats an empty span as "no value" before MessagePack sees it. The empty span is Prague's deliberate tombstone encoding on both the produce and consume sides.
- **The self-filter's delete semantics as a *design* question.** Keeping `SelfProduced` absolute is right; what is broken is the producer-side contract above, which is a different defect with a different fix.
