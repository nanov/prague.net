---
title: Prague Documentation
description: Documentation for Prague, a compile-time-safe in-memory event cache for .NET fed by Kafka topics — tutorial, how-to guides, concepts, explanations, reference and API browser, with every code sample compiled and tested.
---

# Prague

**Prague** is a compile-time-safe, in-memory event cache for .NET (`net9.0`, `net10.0` and `net11.0`). You annotate a `partial` POCO with `[DataCache]`, `[DataCacheKey]`, `[DataCacheIndex]` and `[DataCacheForeignKey<T>]`; a Roslyn source generator emits a typed cache with secondary indices, a fluent query API that the compiler checks, foreign-key joins, and prepared queries that plan once and execute at zero allocations. `Prague.Kafka` keeps those caches hydrated from compacted Kafka topics inside your process. No network hop, no reflection, no eviction.

Every C# block on this site is pulled from the [compiled samples project](https://github.com/nanov/prague.net/tree/main/samples/Prague.Samples) and runs as a test, so what you read is what compiles against the current release.

## Start here

- [**Introduction**](articles/getting-started/introduction.md) — what Prague is and is not, and how data flows from a topic into a queryable cache.
- [**Installation**](articles/getting-started/installation.md) — packages, project setup, platform requirements.
- [**Quick Start**](articles/getting-started/quick-start.md) — a working cache in three steps.
- [**Tutorial: Build an Order Service**](articles/tutorial/build-an-order-service.md) — thirty minutes from annotated POCOs to an HTTP service that queries, joins and pages orders out of Kafka-fed caches, reacts to live shipments and reports health.

## Core concepts

- [**Index Types**](articles/core-concepts/index-types.md) — `Unique`, `Many`, `Range`, symmetric variants and predicate key-set indices, and what each costs.
- [**Query Engine**](articles/core-concepts/query-engine.md) — `WithXxx` lanes, intersection, `Or`, `Where`, pooled versus allocating execution.
- [**Sorted Paging**](articles/core-concepts/sorted-paging.md) — `Sort` versus `SortBounded`: which plan to pick for a page and why ties matter.
- [**Prepared and Frozen Queries**](articles/core-concepts/prepared-queries.md) — `Prepare()…Build()` and `BuildFrozen()`: describe once, plan once, execute at 0 B.
- [**Conditional Updates**](articles/core-concepts/conditional-updates.md) — how `CacheEquals` decides the `UpdateType` your handlers see.
- [**Joins**](articles/core-concepts/joins.md) — `JoinWith{Other}` and `InnerJoinWith{Other}` across foreign keys, 1:1 and 1:N, chained.

## How-to guides

Task-shaped pages: a prerequisite, the steps, the pitfalls.

- [**Own Your Model**](articles/how-to/own-your-model.md) — keep your own `[DataCache]` copy of a topic's contract, with your fields and your indexes.
- [**Page an API**](articles/how-to/page-an-api.md) — a sorted, paged list endpoint with offset or cursor pages.
- [**Incremental Sync**](articles/how-to/incremental-sync.md) — send only what changed since a client's watermark.
- [**Write a Filter**](articles/how-to/write-a-filter.md) — header, key and value filters; soft deletes; predicates with DI state.
- [**React to Changes**](articles/how-to/react-to-changes.md) — `ICacheAfterHandler` and every `UpdateType`.
- [**Host with DI**](articles/how-to/host-with-di.md) — registration, options binding, topic placeholders, several clusters, clean shutdown.
- [**Inspect over HTTP**](articles/how-to/inspect-over-http.md) — `Prague.Api` endpoints and query-string syntax.
- [**Map, Clone, Batch**](articles/how-to/map-clone-batch.md) — project results, copy rows, read many keys at once.
- [**Test without a Broker**](articles/how-to/test-without-a-broker.md) — unit-test code that reads caches.
- [**Health and Metrics**](articles/how-to/health-and-metrics.md) — liveness, readiness and the OpenTelemetry meter.
- [**Use with AI Agents**](articles/how-to/use-with-ai-agents.md) — install the agent skills that ship in the `Prague` package.

## Kafka and operations

- [**Kafka Integration**](articles/advanced/kafka-integration.md) — topic settings, wiring caches to topics, filters, after-handlers, producer, health, OpenTelemetry.
- [**Global Last Update Index**](articles/advanced/global-last-update-index.md) — cross-cache change tracking.
- [**Performance Tuning**](articles/advanced/performance-tuning.md) — hot-path guidance.

## Explanation

- [**How Prague Works**](articles/explanation/architecture.md) — attributes → generated code → store and indices → planning → the three frozen executors → pooled results, with the allocation table.
- [**The Concurrency Contract**](articles/explanation/concurrency.md) — one writer, lock-free readers: what a query can observe mid-write and what is safe to share.
- [**Ordering and Determinism**](articles/explanation/ordering-and-determinism.md) — when the row order is a promise and when it is only a mechanism.

## Reference

- [**Attributes and Generated Code**](articles/reference/attributes.md) — every attribute, its parameters and defaults, and exactly what the generator emits.
- [**Generator Diagnostics**](articles/reference/diagnostics.md) — every `CACHExxx` diagnostic with an example and a fix.
- [**Benchmarks**](articles/reference/benchmarks.md) — the latest measured numbers per benchmark family, traced to the log.
- [**Release Notes**](articles/reference/release-notes.md) — every release, newest first.
- [**API browser**](api/index.md) — generated from the XML doc comments of `Prague`, `Prague.Core`, `Prague.Kafka`, `Prague.Attributes` and `Prague.Codegen`.

## Elsewhere

- [GitHub repository](https://github.com/nanov/prague.net) — source, issues, contributing.
- [NuGet](https://www.nuget.org/packages/Prague/) — `Prague`, `Prague.Kafka`, `Prague.Api`, `Prague.Api.UI`.
- [llms.txt](https://nanov.github.io/prague.net/llms.txt) and [llms-full.txt](https://nanov.github.io/prague.net/llms-full.txt) — machine-readable summaries for coding assistants; the `Prague` package also ships [agent skills](articles/how-to/use-with-ai-agents.md).
