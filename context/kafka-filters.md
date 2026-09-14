# Kafka message filters

> **Read when:** adding/changing header/key/value filters or the `FilterDecision` skip-vs-delete logic.

Filter types live under `src/Prague.Kafka/Filters/`. `KafkaCacheHandlerBuilder` builder methods, all **AND-composed** across calls:

- `WithHeaderFilter(...)` — evaluated **first**, in the raw consume loop via `KafkaCacheHandler.EvaluateHeaderGate(in RawHeaders)` against UTF-8 name/value **spans** (before key deserialization). **No `treatAsDelete`.** It also self-filters the producer-instance header (`KafkaCaches.ProducerInstanceIdHeaderName` == this instance's id) so a producer never re-consumes its own writes.
  Returns `HeaderGate { Accept, SelfProduced, Rejected, MissingRequiredHeader }` (`Filters/HeaderGate.cs`) — the *reason*, not a bare bool, because `ConsumeRawLoop` waives exactly one of them for a tombstone. `MissingRequiredHeader` is the only reason that describes the message's **shape** rather than judging its content; `SelfProduced` and `Rejected` are absolute.
- `WithKeyFilter(Func<TKey,bool>, bool treatAsDelete = false)`
- `WithValueFilter(Func<TValue,bool>, bool treatAsDelete = false)`

No-filter path is zero-alloc for the **key and value** gates (inline `IsEmpty` check, short-circuited at the three `DispatchRaw` call sites). The **header** gate still walks every header regardless — the producer self-filter has to inspect each one — but a name longer than the longest configured filter key is answered by a compare, so with zero filters configured (bound 0) nothing is transcoded or looked up.

**Required headers are a bitmask.** `KafkaHeaderFilters.RequiredMask` carries one bit per header name that a `WithHeaderExistsFilter` requires; the gate ORs in a bit when a name resolves and compares `seen == RequiredMask` once, after the last header. A single shared bool used to mean any one required header satisfied all of them — `exists("A") + exists("B")` composed as OR against the documented AND. Cap is 64 distinct required names; the 65th throws at handler build.

**The header-name `stackalloc` is bounded.** A header name is raw wire data, bounded by the broker only by `message.max.bytes`, so sizing a `stackalloc` by it let a producer take the whole host down with an uncatchable stack overflow — replayed from the log into a crash loop. `ShouldProcess` rejects a name longer than the longest configured key before allocating. The bound is exact, not merely conservative: `GetByteCount(GetString(b)) >= b.Length` for *every* byte sequence (well-formed ones round-trip; ill-formed ones decode to U+FFFD, which re-encodes to three bytes and never shrinks), so a name over the bound cannot decode to any key. The transcode lives in a separate `Resolve` so the `localloc` does not block inlining of the bound check.

A thrown predicate is caught and treated as **reject** at all three gates — key and value at the `DispatchRaw` call sites (maps to `Skip`, never `Delete`), header at the `ConsumeRawLoop` call site (maps to `Rejected`, never a waived reason, so a throw cannot let a foreign tombstone cross a sub-stream gate). All logged via `LoggerMessage`. The header catch is by **origin, not by type**: nothing inside the gate observes the consumer's token or talks to the broker, so anything thrown there is the user's predicate. Filtering on exception type would let a predicate impersonate shutdown (an `OperationCanceledException` reaching the loop's cancellation handler) or a broker failure (a `KafkaException` latching the consumer fatal) — so a real shutdown is distinguished by `ct.IsCancellationRequested`, which is the only thing that actually knows. It is load-bearing: the filter chain itself does not catch, and the enclosing handler rethrows — one throwing header predicate would otherwise stop the raw worker of **every** cache on the consumer and replay from the log into a crash loop.

## FilterDecision (key + value share it)

`FilterDecision { Accept, Skip, Delete }` (`Filters/FilterDecision.cs`). Aggregates `KafkaKeyFilters<TKey>.Evaluate(key)` / `KafkaValueFilters<TValue>.Evaluate(value)` return it; each concrete filter carries `internal abstract bool TreatAsDelete`. **First-reject-wins** — the first rejecting filter's flag picks `Delete` vs `Skip`:

- `Skip` → silent drop on load / live publishes `RAW_KIND_FILTERED` → after-handlers fire with `UpdateType.Filtered`.
- `Delete` → live publishes `RAW_KIND_DELETE` → `HandleRawLiveDelete` (removes key, fires `UpdateType.Delete` with old value only if key was present) / on load `RemoveDuringLoad` cancels any buffered value **and** removes the key from the cache (no after-handler). Buffer-only was #35: the compacting buffer is flushed mid-load, so once a key's value had reached the cache the delete was silently lost.

**Caveat:** a key is immutable, so key-filter `treatAsDelete` only evicts when the predicate closes over mutable external state and a *new* message for that key arrives; for pure key predicates it is inert.

## Tombstones beat filters

A tombstone (empty value span) is the log saying the key is gone, and no **key or value** predicate may contradict it. The check lives in **one** place — `DispatchRaw`, above the key gate and below the key deserialization it needs — so the load and live branches cannot drift apart. That duplication is what #35 was.

Consequences, all deliberate:

- A key predicate is **never invoked** for a tombstone. A rejected key no longer swallows the delete (that was the bug: the entry survived in both phases, and on load even an unflushed buffered value was committed at EOF).
- A tombstone for a key the cache never held is a total no-op — no `UpdateType.Delete`, no `UpdateType.Filtered`. `InMemoryDataCache.Remove` returns before the index walk on a miss, and `HandleRawLiveDelete` returns before the after-handler.
- `treatAsDelete: true` on a key filter is unchanged; it was already emitting the identical calls the tombstone branch does, which is why it was the only workaround.

Two ingress rules still stop a tombstone, both on purpose:

- **`HeaderGate.SelfProduced`** — a process never re-consumes its own writes, deletes included.
- **`HeaderGate.Rejected`** — a filter saw its header and rejected the value. That is how a consumer selects a sub-stream of a shared topic; honouring a foreign tombstone there would let any producer evict another stream's key.

`HeaderGate.MissingRequiredHeader` is waived, because a delete carries no headers to satisfy a `WithHeaderExistsFilter` with. Without the waiver **Prague's own `KafkaCacheProducer.Delete` could never remove a key** from such a consumer — it stamps only `X-Producer-Id`.

*Residual hole, accepted:* a headerless tombstone from any producer now passes an exists filter. Harmless for a key this cache never held; it only bites if two streams share a topic **and** a key space **and** the foreign producer omits the discriminating header on deletes — a producer that stamps a header on values but not on deletes is already broken.

*Invariant the waiver rests on:* `KafkaHeaderExistsFilter` is the only executor whose `RequiresHeader` is true, so an incomplete `seen` mask at the end of the loop can only mean "a required header never appeared". Adding another such executor breaks the waiver silently — it would start admitting tombstones that violated an explicit rule. Pinned by `tests/Prague.Kafka.Tests/Filters/HeaderGateStateTests.cs`.

Eval site: key and value filters both run inside `KafkaCacheHandler.DispatchRaw` (`IO/KafkaCacheConsumer.cs`), branching on the `isLoading` flag; the tombstone check sits above both, in one place. The header gate runs earlier in `ConsumeRawLoop` via `EvaluateHeaderGate`.
