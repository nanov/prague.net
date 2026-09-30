# Forced writes: bypass cache equality end-to-end

Date: 2026-09-30
Status: Implemented 2026-09-30 (uncommitted in the fork's working tree); plan in [`../plans/2026-09-30-force-write.md`](../plans/2026-09-30-force-write.md). Integration tests compile but were not run locally — no Docker daemon.

## The problem

Every write is conditional. `InMemoryDataCache.AddOrUpdate` asks the store to replace the resident value only when `!old.CacheEquals(new)`; an equal value is reported as `Same` and nothing else happens — no reference swap, no index walk, no `LastUpdated` refresh, no `__PragueMetadata__` update, and on the Kafka side no after-handler beyond `UpdateType.Same`. The generated `AddOrUpdateAndProduce(document)` inherits it: an unchanged document is not even produced.

That is the right default (see `www/docs/articles/core-concepts/conditional-updates.md`), but there is no way to opt out of it for a single write. A producer that needs to re-emit the current state *and have every replica treat it as a change* — to bump `LastUpdated`, to re-run projectors, to re-stamp metadata — currently has to smuggle a nonce into the entity so `CacheEquals` fails, which pollutes the schema for every consumer.

## The contract

A **forced write** is applied unconditionally, on the producing process and on every consuming process:

- The store swaps the resident reference even when `CacheEquals` says equal.
- Every index receives `Update`. For a key-set / unique / range index whose index key did not move this is a no-op; the `LastUpdated` adapters re-stamp their group with the write's timestamp (monotonic, as always). A custom-timestamp adapter re-reads the entity's own timestamp property instead, so an equal document leaves it where it was.
- `__PragueMetadata__` becomes the forced message's offset and timestamp (consumer side).
- After-handlers see `Add` if the key was absent, otherwise `Update` with both values set. A forced write **never** reports `Same`.
- The message carries the marker header `X-Prague-Force`. Presence is the signal; the value (ASCII `1`) is reserved and not inspected.

What force does **not** do:

- It does not bypass ingress policy. Header, key and value filters still run; a rejected forced message is skipped or deleted exactly as an unforced one would be.
- It does not affect tombstones. A delete is already unconditional; `Delete` gains no `force` parameter.
- It does not re-apply on the producer that wrote it. The self-filter (`HeaderGate.SelfProduced`) runs before the marker is read, so the local apply happens exactly once, in `AddOrUpdateAndProduce` itself.
- It does not change statistics. A forced update is counted as an `Update`.

Any producer may set the header — Prague's own or a foreign one — which is the same trust model the self-filter and the user header filters already have.

**Compatibility.** A consumer built before this change passes the unknown header (`KafkaHeaderFilters.ShouldProcess` does not reject names it has no filter for) and applies the message conditionally, i.e. it sees `Same`. Degradation is graceful.

## The change

### `src/Prague.Core`

**`InMemoryDataCache.cs`** — one more `AddOrUpdate` overload, with a required `force`:

```csharp
public bool AddOrUpdate(TKey key, TValue value, long timestamp, bool force, out TValue? oldValue);
```

With `force` the store predicate is `static (_, _, _) => true`, so the result is `Add` or `Update`, never `Same`, and the method — keeping `AddOrUpdate`'s "the cache changed" contract — always returns `true`; `oldValue` is `null` when the key was added. With `force` false it is the existing conditional write (`!old.CacheEquals(new)`). The four existing overloads are untouched; no separate `Force…` method exists (an earlier draft had one and was revised on review).

The post-store index walk (statistics hook + `foreach index → Update/Add`) existed as two copies, one per `AddOrUpdate` body. It is one private `[AggressiveInlining]` helper, `ApplyToIndexes(key, in UpdateResult, timestamp)`, shared by all three bodies. This touches the existing write path only by factoring, measured under "Hot path" below.

**`IDataCacheEntity.cs`** — `IDataCache<TKey, TValue>` gains the matching document-keyed overload, `bool AddOrUpdate(TValue document, long timestamp, bool force, out TValue? value)`. The only implementors are generated caches; no hand-written implementor exists in `src/`, `tests/`, `perf/` or `benchmarks/`.

### `src/Prague.Codegen/CacheGenerator.cs`

- `GenerateCacheInterfaceMethods` emits the forced overload next to the existing four, delegating to `Cache.AddOrUpdate(document.<Key>, document, timestamp, force, out oldDocument)`.
- `GenerateProducerExtensions`:
  - `CacheMarshall.Produce(cache, key, value, bool force)` — a second overload beside the existing one, which now delegates with `force: false`; forwards the flag to the producer.
  - `AddOrUpdateAndProduce(this XxxCache cache, Xxx document, bool force)` — likewise a second overload; the existing `AddOrUpdateAndProduce(cache, document)` delegates with `force: false`:
    ```csharp
    var key = document.<Key>;
    var changed = cache.Cache.AddOrUpdate(key, document, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), force, out _);
    if (changed)
        CacheMarshall.Produce(cache, key, document, force);
    ```
    A forced write always reports a change, so the produce is unconditional for it and carries the header.
- `GenerateProducerHelper` — `XxxCacheProducerHelper.Produce(key, value, bool force)` beside the existing two-argument one, same forwarding, for symmetry.

`force` is a required parameter on a separate overload everywhere, never an optional one: the existing signatures stay exactly as they were and delegate with `force: false`. Callers are expected to write `force: true`.

### `src/Prague.Kafka`

**`KafkaCaches.cs`**

```csharp
public const string ForceHeaderName = "X-Prague-Force";
public static readonly byte[] ForceHeaderValue = "1"u8.ToArray();
```

**`IO/KafkaCacheProducer.cs`** — a second overload `Produce<TKey, TCacheValue>(string topic, TKey key, TCacheValue value, bool force)` carries the body; the existing three-argument `Produce` delegates to it with `force: false`. After `Derich`, `if (force) headers.Add(KafkaCaches.ForceHeaderName, KafkaCaches.ForceHeaderValue);`. `KafkaHeaders.Add` stores the memory by reference and the static array outlives the call, so this stays allocation-free.

**`IO/KafkaCacheConsumer.cs`**

- `EvaluateHeaderGate(in RawHeaders headers, out bool forced)`. `forced` starts `false`; inside the existing loop, after the self-produced check and before `ShouldProcess`:
  ```csharp
  if (System.Text.Ascii.Equals(name, KafkaCaches.ForceHeaderName))
      forced = true;
  ```
  One length-guarded ASCII compare per header, in a loop that already walks every header. The `catch` in `ConsumeRawLoop` leaves `forced = false` alongside `gate = Rejected`.
- `DispatchRaw(in RawMessage raw, bool isLoading, bool forced)` (abstract and override). The tombstone branch ignores the flag. Live: `PublishRaw(forced ? RAW_KIND_FORCE_UPDATE : RAW_KIND_UPDATE, …)`. Load: `buffer.AddOrReplace(key, value, ts, forced)`.
- `RAW_KIND_FORCE_UPDATE = 3` — a fourth `Kind` value, so `RawWorkItem` does not grow. `ApplyRawLiveAsync` maps it to `HandleRawLiveUpdate(key, value, ts, force: true)`.
- `HandleRawLiveUpdate(…, bool force)`:
  ```csharp
  var changed = _cache.AddOrUpdate(value, timestampMs, force, out var old);
  return (changed, old) switch { … unchanged … };
  ```
- `FlushRawLoadBufferToCache` applies each slot with `_cache.AddOrUpdate(value, ts, forced, out _)`.

**`Utils/ValueCompactingBuffer.cs`** — a `bool[] _forced` beside `_timestamps`. `AddOrReplace(key, value, ts, bool forced)` **ORs** the incoming flag with the superseded slot's flag before storing it; `Remove(key)` drops the flag with the slot (a tombstone makes any later write for that key an `Add` regardless). The enumerator yields `(Value, TimestampMs, Forced)`.

The OR is the load/live parity rule: *if any superseded write for a key within one compaction batch was forced, the surviving write is applied unconditionally.* Live would have applied the forced write (refreshing `LastUpdated`) and then reported later equal writes as `Same`; load collapses that to one unconditional apply of the last value. Timestamps differ by the same approximation the buffer already makes for unforced runs (two equal writes compact to the later timestamp on load, but the live path keeps the earlier one), so this does not introduce a new class of divergence — it avoids introducing one, which is what `LoadLivePhaseParityTests` exists to catch.

### Hot path

- Unforced write path in `Core`: unchanged modulo the extracted helper. Measured with `*CoreIngestBenchmarks*` (in-process, 1 warmup + 5 iterations, idle machine, test runs finished beforehand): `IngestAll` 1.372 ms ± 0.026 (HEAD) → 1.340 ms ± 0.058 (after), 2.22 MB allocated on both — within noise, the helper stays.
- Consumer: one `Ascii.Equals` per header per message. The gate already pays one for `X-Producer-Id`; a name of a different length fails on the length check.
- Producer: one `KafkaHeaders.Add` of a static array, only when `force` is set. Zero allocations either way.
- Live worker: `RawWorkItem` layout unchanged; the `switch` on `Kind` gains one arm.
- Load buffer: 50 more bytes per handler.

## Tests

**`tests/Prague.Core.Tests`** (broker-free)

- `AddOrUpdate(…, force: true, …)` with a value equal under `CacheEquals`: returns `true`, `oldValue` is the previous instance, `TryGet` returns the new reference (`ReferenceEquals`), key-set and unique indexes still resolve the key, and a `LastUpdated` adapter reports the new timestamp.
- `AddOrUpdate(…, force: true, …)` for an absent key: returns `true`, `oldValue` is `null`, indexes contain the key.
- Regression guard: the same overload with `force: false` and an equal value returns `false` and leaves the reference and `LastUpdated` untouched; with a different value it updates. (`Cache/AddOrUpdateForceTests.cs`.)

**`tests/Prague.Generated.Tests`** — the generated cache exposes `AddOrUpdate(document, timestamp, force, out old)` and `AddOrUpdateAndProduce(doc, force: true)` compiles (a compile-and-call smoke test; the producer throws `InvalidOperationException` when not configured, which is the asserted outcome). (`Kafka/AddOrUpdateForceTests.cs`.)

**`tests/Prague.Kafka.Tests`** (broker-free)

- `ValueCompactingBuffer`: forced then unforced replace → surviving slot forced; unforced then forced → forced; `Remove` then `AddOrReplace` unforced → not forced; enumerator yields the flag.

**`tests/Prague.Kafka.IntegrationTests`** (Docker)

- Foreign producer writes `V`, then `V` again with `X-Prague-Force`: the after-handler receives `Update` (not `Same`) with `Name` equal on both sides, and `__PragueMetadata__` offset advanced.
- `Produce(topic, key, value, force: true)` from Prague's own producer: the record on the wire carries the marker (read back with a plain consumer) and an unforced one does not; the co-located consumer applies neither — the self-filter runs before the marker is read — so after a settle the cache holds neither key and the after-handler saw at most `Filtered`. An in-process A→B pair cannot be tested: `KafkaCaches.InstanceId` is static per process, so B would self-filter A's record too; B's side is covered by the foreign-producer tests above, which put the same header on the wire.
- Load phase across a compaction flush: `V` flushed, then forced `V` before EOF → after the load the resident value's `__PragueMetadata__` carries the forced message's offset (an unforced repeat would have left the earlier one).
- Forced message rejected by a value filter with `treatAsDelete: true` → the key is deleted; force does not override filters.
- Existing `AfterHandler_OnSameValue_ReceivesSame` stays as the unforced baseline.

## Documentation

- `www/docs/articles/core-concepts/conditional-updates.md` — new section "Forcing a write" (the forced `AddOrUpdate` overload, `AddOrUpdateAndProduce(doc, force: true)`, `X-Prague-Force`, what force does and does not do). The existing "Producer-side dispatch" paragraph is corrected: `AddOrUpdateAndProduce` *does* skip an unchanged document; `KafkaCacheProducer.Produce` does not.
- `www/docs/articles/advanced/kafka-integration.md` § Producer — the `force` parameter and the header.
- `context/core.md` — the forced overload next to `AddOrUpdate`.
- `context/kafka.md` — conditional-updates bullet gains the force escape hatch; `context/kafka-filters.md` — the header gate also reports the force marker, and the "no-filter path" note records the extra compare.

## Declared behaviour changes

None for existing callers. Every existing signature is untouched and the forced behaviour lives on new overloads; the header is new; the only observable difference for an unmodified consumer is that a forced message from an upgraded producer is applied conditionally (`Same`), as any message is today.

## Out of scope

- A per-cache "always force" switch (`[DataCache(IgnoreEquality = true)]` or a builder option). Expressible later as sugar over this design.
- Forced deletes.
- Inspecting the header's value.
