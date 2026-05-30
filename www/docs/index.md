---
title: Prague Documentation
---

# Prague

**Prague** (`Prague`) is a compile-time-safe, in-memory event cache for .NET 9, designed for event-sourced data streams from Kafka.

- **Zero-reflection, compile-time-safe** fluent query API powered by Roslyn source generators.
- **Three primary index types** — `Unique`, `Many`, `Range` — with automatic intersection and short-circuit planning.
- **Compile-time joins** (1:1 and 1:N) with pooled, zero-allocation result sets.
- **Kafka integration** with header/key filters, change detection via deep structural equality, and split liveness/readiness health checks.
- **Hot paths**: `Span<T>`, `stackalloc`, `ArrayPool<T>`, SIMD-friendly. ~9.7M reads/sec, <100ns indexed lookup on a single core.

## Start here

- [**Introduction**](articles/getting-started/introduction.md) — what Prague is and isn't.
- [**Installation**](articles/getting-started/installation.md) — packages and project setup.
- [**Quick Start**](articles/getting-started/quick-start.md) — a working cache in 30 lines.

## Core concepts

- [**Index Types**](articles/core-concepts/index-types.md) — the menu of secondary indices and predicate-based key-set indices.
- [**Query Engine**](articles/core-concepts/query-engine.md) — fluent API, intersection rules, pooled vs allocating execution.
- [**Conditional Updates**](articles/core-concepts/conditional-updates.md) — how `CacheEquals` drives the `UpdateType` reported to downstream handlers.
- [**Joins**](articles/core-concepts/joins.md) — `JoinWith{Other}` / `InnerJoinWith{Other}` across foreign keys.

## Advanced

- [**Kafka Integration**](articles/advanced/kafka-integration.md) — wiring caches to topics, filters, after-handlers, health checks.
- [**Global Last Update Index**](articles/advanced/global-last-update-index.md) — cross-cache change tracking for incremental sync.
- [**Performance Tuning**](articles/advanced/performance-tuning.md) — hot-path guidance.

## API Reference

The <a href="api/">API browser</a> is generated from XML doc comments on `Prague`, `Prague.Core`, `Prague.Kafka`, `Prague.Attributes`, and `Prague.Codegen`.
