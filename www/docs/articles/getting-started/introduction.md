---
title: Introduction
description: What Prague is, what it is not, and how data flows from a compacted Kafka topic into an indexed, queryable in-process cache.
---

# Introduction

Prague (`Prague`) is a compile-time-safe, in-memory event cache for .NET (`net9.0`, `net10.0` and `net11.0`). It is built for one workload: turning event-sourced data streams (typically compacted Kafka topics) into typed, queryable, low-latency views inside your process.

## What Prague is

- **A library, not a server.** Caches live inside your application's address space. No network hop, no out-of-process IPC.
- **Code-generated.** A Roslyn source generator emits storage, indices, joins, and query builders from a handful of attributes on `partial` POCOs. No reflection on the read path.
- **Index-first.** Every `WithXxx(...)` call is a candidate-narrowing lane over a typed index. The runtime intersects the lanes on a stackalloc bitmap and short-circuits on the first empty one; `Where(...)` predicates run only on the survivors.
- **Pooled.** Hot-path result sets rent from `ArrayPool<T>` through the disposable `QueryResults<T>` struct. Per-execution allocations are zero on the pooled and frozen paths.
- **Two query flavors.** The eager `Query()…Execute()` builder plans and runs in one call; the prepared `Prepare()…Build()` / `BuildFrozen()` form describes a query once, optionally with a parameter struct, and executes it on demand from any thread.

## What Prague is not

- **Not a distributed cache.** A single process owns its caches. Cross-process replication is the upstream log's job (Kafka, in our case); every instance loads every topic in full.
- **Not a relational database.** Queries are fluent expressions over typed indices, not SQL. On the eager path your call order is the plan; a frozen query plans once at build time.
- **Not an eviction cache.** There is no TTL and no LRU; the topic's compaction is the retention policy.
- **Not snapshot-isolated.** Caches are written by one consumer thread while queries read them lock-free. A query never fails because of a concurrent write, but a row written mid-query may be included or missed, and two legs of one join may disagree. The next query sees the write.

## The data flow

[![Data flow: a compacted Kafka topic is consumed per cache, each message is deserialized and filtered, then fed into InMemoryDataCache, which maintains the secondary indices the fluent query API reads from.](../../images/data-flow.svg)](../../images/data-flow.svg)

A Kafka consumer per cache reads a compacted topic, deserializes each message into your POCO, applies any header/key/value filters, and feeds it into `InMemoryDataCache<TKey, TValue>` via `AddOrUpdate(value, timestampMs)`. Secondary indices are updated incrementally. The fluent `Query()` API runs over the resulting structure and returns a `QueryResults<T>` you enumerate and dispose.

The live phase only begins after the consumer reaches partition EOF — at that point `ICacheAfterHandler` starts firing for every write, with an `UpdateType` distinguishing `Add` / `Update` / `Same` / `Delete` / `Filtered`. Writes applied during the initial load do not reach after-handlers. See [Conditional Updates](../core-concepts/conditional-updates.md).

## Where to go next

- [Installation](installation.md) — package layout and platform requirements.
- [Quick Start](quick-start.md) — a working cache in three steps.
- [Index Types](../core-concepts/index-types.md) — the menu of secondary indices.
- [Query Engine](../core-concepts/query-engine.md) — how the planner intersects them.
