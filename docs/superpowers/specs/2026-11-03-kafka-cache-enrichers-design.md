# `WithEnricher` — enrich a value from the host before it is cached

Date: 2026-11-03
Status: Implemented — 2026-11-03 (working tree of feat/kafka-cache-enrichers; registration keyed by cache type added after review)

## Goal

Let a host fill fields of a consumed value from its own state — typically other Prague caches — *before* the value
is stored and indexed. Today the only ingress extension points are filters (accept or drop a message) and
after-handlers (observe a change after it was applied, live phase only). Neither can change what the cache holds:
an after-handler sees a value that is already resident and indexed, so a field it would set cannot be changed in
place (indexes hold the old key; readers may hold the instance) and cannot be re-added without a clone and a second
`AddOrUpdate`.

The motivating case: a search row carries fields derived from a sport tree (display names, display orders,
disabled flags, top-event order) that the producer computed from *its* copy of the tree. The consumer owns a copy of
the same tree and wants those fields to reflect it on arrival, so that the row's indexes are built from the
consumer's view and the producer never has to republish rows when the tree changes.

## Non-goals

- Re-applying enrichers to values that are already resident (`Reenrich(key)`, `ReenrichAll()`, a compare-and-swap
  replace in `Prague.Core`). That is a separate design; this one only covers the ingress path.
- Ordering the initial load of one `AddKafkaCaches` section behind another ("startup gate"). An enricher that
  reads another cache sees whatever that cache holds at the moment the message is consumed; during the initial
  load that may be a partial view. The gate is a separate design. This spec documents the caveat and nothing more.
- Delegate / state-factory overloads in the style of `WithKeyFilter(stateFactory, predicate)`. The type and factory
  forms mirror `WithAfterHandler`; a delegate form can follow once there is a second user.
- An asynchronous enricher. The hook runs on the consume thread in both phases; `ValueTask` would turn the load
  into an await per message.
- A producer-side counterpart (nothing is "de-enriched" on `AddOrUpdateAndProduce`).

## Public API

New file `src/Prague.Kafka/ICacheEnricher.cs`:

```csharp
namespace Prague.Kafka;

public interface ICacheEnricher<in TKey, in TValue> {
	/// Called for every consumed value, in both phases, after Prague's own header/timestamp enrichment and
	/// before the value is stored. The value is not yet visible to any reader: change it in place.
	void Enrich(TKey key, TValue value);
}
```

Two builder methods on `KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>`, mirroring `WithAfterHandler`:

```csharp
public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithEnricher<TEnricher>()
	where TEnricher : class, ICacheEnricher<TKey, TValue>;

public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithEnricher<TEnricher>(
	Func<IServiceProvider, TEnricher> builder)
	where TEnricher : class, ICacheEnricher<TKey, TValue>;
```

Both register a **keyed** singleton `ICacheEnricher<TKey, TValue>` in the host's services, with the cache type
`typeof(TCacheEntity)` as the service key; `Build` resolves
`GetKeyedServices<ICacheEnricher<TKey, TValue>>(typeof(TCacheEntity))`. An enricher is therefore bound to exactly
the cache whose builder registered it — unlike after-handlers, which are keyed by `(TKey, TValue)` alone and so
are shared by every cache with the same key and value types. Today only attribute-form `[DataCache]` POCOs get the
Kafka partial, so two generated Kafka caches cannot share `(TKey, TValue)`; that sharing is reachable only through a
hand-written cache, so the keyed registration is defensive and the groundwork for resolving a cache's enrichers
later. Keyed services are already how a section's producer is resolved
(`GetRequiredKeyedService<KafkaCacheProducer>(OptionsSectionName)`). Several `WithEnricher` calls on one cache run
in registration order.

## Semantics

| Scenario | Behaviour |
|---|---|
| No enricher registered | No change on the hot path beyond one array-length test per consumed value. |
| Value accepted, initial load | Enrichers run after `SetPragueMetadata` and the header enricher, before the value enters the load buffer. The buffered, flushed and indexed value is the enriched one. |
| Value accepted, live phase | Same position: after the header enricher, before `PublishRaw`. `AddOrUpdate` compares the *enriched* value with the resident one, so an enrichment that changes nothing still reports `Same`. |
| Message dropped by a header, key or value filter | Enrichers never run. Filters judge the payload as produced, not the enriched value. |
| Tombstone | Enrichers never run; there is no value. |
| Enricher throws | Logged at Error (`EnricherError`: cache name, offset), the value is stored as the enrichers left it, and the remaining enrichers of that message still run. The message is **not** dropped: it is the log's statement of the key's value, and an enrichment failure is a host bug to fix, not a reason to pin a stale row or lose a key. |
| Several enrichers | Registration order, each sees the previous one's changes. |

What an enricher may read: anything the host gave it through its constructor. It runs on the single consume thread
shared by every cache of that `AddKafkaCaches` section, in both phases, so it must not block or perform I/O; a
dictionary lookup or a `TryGet` on another cache is the intended cost. Another Prague cache read from an enricher is
in whatever state its own consumption has reached — complete once both caches are live, possibly partial while the
section is loading, because sections and topics load concurrently.

## Internal changes

**`src/Prague.Kafka/ICacheEnricher.cs`** (new) — the interface above.

**`src/Prague.Kafka/DependencyInjection.cs`**

- The two `WithEnricher` overloads on `KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>`, next to
  `WithAfterHandler`.
- `internal ICacheEnricher<TKey, TValue>[] BuildEnrichers(IServiceProvider sp)` — the broker-free test seam, the
  same shape as `BuildKeyFilters` / `BuildValueFilters`: `GetKeyedServices<ICacheEnricher<TKey, TValue>>(typeof(TCacheEntity))`
  materialised once. `Build` passes its result to the handler constructor.

**`src/Prague.Kafka/IO/KafkaCacheConsumer.cs`**

- `KafkaCacheHandler<TCacheEntity, TKey, TValue>` gains `private readonly ICacheEnricher<TKey, TValue>[] _enrichers`
  and a constructor parameter after `afterHandlers`.
- `DispatchRaw`: after each of the two `_enricher.Enrich(value, raw.Headers, timestamp)` calls (load and live
  branches), `if (_enrichers.Length != 0) RunEnrichers(key, value, offset);`.
- `RunEnrichers` is a separate, non-inlined method holding the loop and the per-enricher `try`/`catch`, so the
  per-enricher EH region and the loop locals stay out of `DispatchRaw` (which already carries catch handlers of
  its own; the point is to add nothing to it for consumers without enrichers).
- A `LoggerMessage` partial `EnricherError(Exception, string cacheName, long offset)` that declares no `EventId`,
  like every other `[LoggerMessage]` in `KafkaCacheConsumerLog`; Error level, message `[Prague] Enricher threw for {CacheName} - {Offset}; value stored as enriched so far`.

**`context/kafka.md`** — add `ICacheEnricher` to the extension-point list with its position in the ingress order
(filters → metadata → header enricher → enrichers → buffer/publish) and the partial-view caveat.

**Docs site** — a how-to article `www/docs/articles/how-to/enrich-from-another-cache.md` whose code blocks come from
a compiled sample `samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs` (`#region` blocks, like
`ReactToChanges.cs`), linked from `www/docs/toc.yml` and `www/docs/index.md`. The sample adds one small model,
`OrderSearchRow`, because every existing attribute-form model is `init`-only and an enricher needs settable
fields; it is enriched from the existing `CustomerCache`. The registration block is compiled-only, the enricher itself runs as a test.

## Hot path

The accepted path pays one `_enrichers.Length != 0` test per consumed value. With enrichers registered it pays the
call and whatever the enrichers do. No allocation is added: the array is built once per handler, and the loop is a
`foreach` over the array (lowered to an indexed loop).

## Declared behaviour changes

None for a host that registers no enricher. The handler constructor gains a parameter; it is `internal`, and its
only call site is `KafkaCacheHandlerBuilder.Build`.

## Verification

- `tests/Prague.Kafka.Tests/DependencyInjection/EnricherRegistrationTests.cs` (no Docker): the type overload
  registers one keyed singleton `ICacheEnricher<TKey, TValue>` under the cache type and nothing in the unkeyed
  slot; the factory overload resolves through the factory once; `BuildEnrichers` returns the enrichers in
  registration order; an enricher registered under another cache type with the same key and value types is not
  part of this cache's `BuildEnrichers`; a builder with no `WithEnricher` builds an empty array; registration
  does not resolve anything.
- `tests/Prague.Kafka.IntegrationTests/EnricherTests.cs` (Docker, `DualKafkaClusterFixture`): a value produced
  before the host starts is enriched when the load completes (load branch); a value produced after the start is
  enriched (live branch); two enrichers run in registration order, the second seeing the first's change; an
  enricher that throws on one key leaves that value stored as enriched so far, logs once, and does not affect the
  next message; an enricher sees the header-enriched value (a `[DataCacheHeader]` property is already set when it
  runs); a value rejected by a value filter never reaches the enricher; an unchanged enrichment of a re-produced
  value fires `UpdateType.Same`, a changing one fires `Update`.
- `dotnet build Prague.sln` (warnings are errors), `dotnet test Prague.Tests.slnf`, and `Prague.Samples` compiles
  and passes, since it is part of the test filter.
