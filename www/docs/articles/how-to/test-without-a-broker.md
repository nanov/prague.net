---
title: Test without a Broker
description: Unit-test code that reads caches by constructing them directly or through the registry, seeding with AddOrUpdate, driving after-handlers by hand and building the Kafka container without starting it.
---

# Test without a Broker

**Goal:** fast, deterministic tests for everything that reads a cache, with Kafka nowhere in the picture.

**What it covers:** your code. Queries, after-handler logic called by hand, the DI wiring and the wire format of a copied contract all run without Kafka. The pipeline between the topic and your code does not: filters, `treatAsDelete`, tombstones, which `UpdateType` a message produces, and the skipped handlers during the initial load are decided by the consumer, and a write through `AddOrUpdate` runs no filter and no after-handler. Test those against a broker ([below](#when-you-do-want-a-broker)).

**Prerequisite:** none beyond the generated caches. The code is in [`TestWithoutABroker.cs`](https://github.com/nanov/prague.net/blob/main/samples/Prague.Samples/HowTo/TestWithoutABroker.cs); every block runs as a test. All test projects in the repository use NUnit; the patterns are framework-neutral.

## The code under test

A read model that takes two caches and knows nothing about where their data comes from. That is the seam: in production `AddKafkaCaches` fills them, in a test you do.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/TestWithoutABroker.cs#test-subject)]

## 1. A stand-alone cache

`new OrderCache()` is a complete cache. `AddOrUpdate` is the same method the consumer calls per message; pass a timestamp when the last-update index matters.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/TestWithoutABroker.cs#test-standalone-cache)]

## 2. The registry, for joins and global indices

Joins and global last-update indices are wired when caches are built together. `DataCacheRegistryBuilder.Register<OrderCache>()` pulls in every cache the foreign keys reach.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/TestWithoutABroker.cs#test-registry)]

## 3. After-handlers

An `ICacheAfterHandler` has one method; call it with the `UpdateType` sequence a topic would produce (see [React to changes](react-to-changes.md) for the arguments per case).

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/TestWithoutABroker.cs#test-after-handler)]

## 4. Asserting on pooled results

`QueryResults<T>` is an `IReadOnlyList<T>`, so NUnit's collection constraints work on it directly. Assert the set, not the order, unless the query sorts.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/TestWithoutABroker.cs#test-pooled-assert)]

## 5. Frozen against eager

A frozen query is a plan; the eager query is its specification. Compare them over several argument values, as sets when unsorted.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/TestWithoutABroker.cs#test-frozen-vs-eager)]

## 6. The real container, not started

`AddKafkaCaches` creates no consumer until the hosted service is resolved, and contacts no broker until it starts. Build the provider, resolve the cache singletons and seed them: your services then run against the same instances the app would inject. The test proves the wiring: the handler the cache would call is the instance readers inject. It does not feed the handler; seeding goes around the consumer, so the projection stays empty.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/TestWithoutABroker.cs#test-di-container)]

## When you do want a broker

The repository's `Prague.Kafka.IntegrationTests` start Kafka with Testcontainers and exercise the consumer end to end: initial load, live updates, tombstones, filters, after-handlers, health. Without Docker they fail on the socket within seconds, which is the only reason they fail. Use that project as the template when a test has to cover the pipeline: filters, `treatAsDelete`, tombstones, the `UpdateType` a handler receives, or deserialization. Once the container is up, each test takes about a second. Everything above it belongs in a unit test.

## Pitfalls

- **Asserting order on an unsorted frozen result.** It carries no row-order guarantee. Use `Is.EquivalentTo`, or add a total sort.
- **Forgetting `using` on pooled results in tests.** The test passes and the buffer leaks until the pool refills; under a long test run that shows up as allocation noise. The `using` is free.
- **Resolving `IHostedService` in a unit test.** It creates the Kafka consumer, and starting it waits for a broker until its token fires. Build the provider and stop there.
- **Expecting a seeded cache to drive an after-handler.** Only the consumer calls handlers. Call `Handle` yourself (step 3), or use a broker.
- **Caches pulled in by foreign keys are not DI services.** Register a factory from the registry if the code under test injects them (step 6 does).
