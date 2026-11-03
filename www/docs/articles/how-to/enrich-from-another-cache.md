---
title: Enrich from Another Cache
description: Fill fields of a consumed value from the host's own state with ICacheEnricher before Prague stores and indexes it, in both the initial load and the live phase.
---

# Enrich from Another Cache

**Goal:** a search row arrives with the fields its producer knew; before it is cached, fill the fields the *consumer* knows better — here the customer's name and tier from the host's own `CustomerCache` — so that the row's indexes are built from the consumer's view.

**Prerequisite:** two caches registered through `AddKafkaCaches` in one host. The code is in [`EnrichFromAnotherCache.cs`](https://github.com/nanov/prague.net/blob/main/samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs); the registration block is compiled-only, the rest runs as tests.

## 1. Give the row somewhere to put the enriched fields

An enricher changes the value in place, so the fields it fills are settable, not `init`-only. An index on such a field is built from what the enricher wrote.

[!code-csharp[](../../../../samples/Prague.Samples/Models/OrderSearchRow.cs#order-search-row)]

## 2. Implement the enricher

`ICacheEnricher<TKey, TValue>.Enrich` is called for every value that passed the filters, in the initial load and in the live phase alike, after Prague filled the `[DataCacheHeader]` and `[DataCacheFromTimestamp]` properties and before the value enters the cache. Nothing else holds the instance yet.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs#enrich-enricher)]

## 3. Register it

Type registration makes it a singleton built by the container and bound to this cache, which is how it receives the `CustomerCache`; the factory form, `WithEnricher(sp => …)`, is for enrichers with constructor arguments DI cannot supply. Several enrichers on one cache run in registration order, each seeing the previous one's changes.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs#enrich-register)]

## 4. What the cache holds

The consumer is the only caller in production; a test is the other one. Enrich a row by hand, store it, and query the index that only the enriched value could satisfy.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/EnrichFromAnotherCache.cs#enrich-by-hand)]

In the live phase the cache compares the *enriched* value with the resident one, so a re-emitted message whose enrichment reproduces the resident row is reported as `Same` to after-handlers, and one that changes it as `Update`.

## Pitfalls

- **Blocking in the enricher.** It runs synchronously on the consume thread shared by every cache of the section, for every message of the initial replay too. A `TryGet` on another cache is the intended cost; no I/O, no locks that wait.
- **Reading a cache that is still loading.** Sections and topics load concurrently. While the host is loading, the cache an enricher reads may hold only part of its topic, or an older version of a key: a compacted topic still replays older versions of a key during the load. A miss then means "not known yet" rather than "does not exist", and a hit can be stale. A value enriched during the load keeps that view until the next message for its key. If that matters, keep the producer's value on a miss, as the sample does.
- **Throwing.** Each throw is logged (`EnricherError`, with the cache name and offset) and the value is stored as enriched so far; the message is never dropped, and the enrichers after the one that threw still run. Fix the enricher; do not rely on the log to find stale rows.
- **Changing the key or a `[DataCacheIgnoreEquality]` field.** The message key decides load-phase compaction and which entry a tombstone removes, but the cache stores the value under its own `[DataCacheKey]` property: an enricher that changes that property files the value under a key that later messages and tombstones for the original key never reach. After-handlers still receive the message key, so they would see a key that differs from the stored one. Keep the key `init`-only, as the sample does. Fields excluded from equality do not make a re-emit an `Update`, and on `Same` the resident row keeps its old value of them: an enricher's write to such a field is lost.
- **Expecting a re-run.** The hook runs on ingress only. A change in the cache it reads does not re-enrich rows that are already resident; that needs the next message for the row's key.
- **Expecting a local write to be enriched.** Enrichers run on consumed messages only. A local `cache.AddOrUpdate(...)` or `AddOrUpdateAndProduce(...)` stores the document as given, and the producing instance never re-consumes its own message, so it keeps the unenriched value while the other instances hold the enriched one. In the other direction, `ForceProduceAll()`, or `AddOrUpdateAndProduce(row, force: true)` with a resident row, publishes the enriched fields to the topic as if the producer had sent them. Registration has two silent no-ops: `WithEnricher` on an `AddInternalCache` builder (that cache has no consumer), and a plain `services.AddSingleton<ICacheEnricher<…>>`, which no cache reads — register through `WithEnricher`, which binds the enricher to its cache.
