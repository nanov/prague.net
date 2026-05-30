---
title: Introduction
---

# Introduction

Prague (`Prague`) is a compile-time-safe, in-memory event cache for .NET 9. It is built for one workload: turning event-sourced data streams (typically Kafka topics) into typed, queryable, low-latency views inside your process.

## What Prague is

- **A library, not a server.** Caches live inside your application's address space. No network hop, no out-of-process IPC.
- **Code-generated.** Roslyn source generators emit storage, indices, joins, and query builders from a handful of attributes on POCOs. No reflection on the read path.
- **Index-first.** Every query is planned over typed indices. The runtime intersects index hits and short-circuits empty sets — there is no linear scan unless you explicitly request `Where(...)` on a non-indexed field.
- **Pooled.** Hot-path result sets use `ArrayPool<T>` via the disposable `QueryResults<T>` struct. Per-iteration allocations are zero in the steady state.

## What Prague is not

- **Not a distributed cache.** A single process owns its caches. Cross-process replication is delegated to whatever produces the upstream events (Kafka, in our case).
- **Not a relational database.** Queries are fluent expressions over typed indices, not SQL. There is no JIT plan optimizer; the plan is chosen at C# compile time by your call shape.
- **Not eventually consistent across processes.** Within one process, all writes are observed by all readers without coordination.

## The data flow

![Prague data flow](../../images/data-flow.png)

A Kafka consumer per cache reads a compacted topic, deserializes each message into your POCO, applies any header/key filters, and feeds it into `InMemoryDataCache<TKey, TValue>` via `AddOrUpdate(value, timestampMs)`. Secondary indices are rebuilt incrementally. The fluent `Query()` API runs over the resulting structure and returns a `QueryResults<T>` you enumerate and dispose.

The live phase only begins after the consumer reaches partition EOF — at that point `ICacheAfterHandler` starts firing for every write, with an `UpdateType` distinguishing `Add` / `Update` / `Same` / `Delete` / `Filtered`. See [Conditional Updates](../core-concepts/conditional-updates.md).

## Where to go next

- [Installation](installation.md) — package layout and platform requirements.
- [Quick Start](quick-start.md) — a working cache in 30 lines.
- [Index Types](../core-concepts/index-types.md) — the menu of secondary indices.
- [Query Engine](../core-concepts/query-engine.md) — how the planner intersects them.
