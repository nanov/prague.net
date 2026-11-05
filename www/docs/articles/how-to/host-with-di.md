---
title: Host with DI
description: Register caches in a generic host or ASP.NET Core, bind options from configuration, resolve topic placeholders, run several clusters, start work after the load, and shut down cleanly.
---

# Host with DI

**Goal:** a host that consumes the right topics with the right settings, in which your own services get the caches injected and start only once the data is there.

**Prerequisite:** `Prague.Kafka` installed ([Installation](../getting-started/installation.md)). The code is in [`HostWithDi.cs`](https://github.com/nanov/prague.net/blob/main/samples/Prague.Samples/HowTo/HostWithDi.cs). The `Program`-shaped blocks are compiled-only; options binding, placeholder resolution and the registry run as tests, because building the container contacts no broker.

## 1. Generic host

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/HostWithDi.cs#host-generic)]

`AddKafkaCaches` registers each `AddCache`d cache as a singleton, the `IDataCacheRegistry`, `KafkaCachesStatistics`, a keyed `KafkaCacheProducer` and one hosted `KafkaCachesBackgroundWorker`. Its `StartAsync` returns after the initial load, so hosted services registered *after* it start on full caches when the host starts services in order.

## 2. ASP.NET Core

The overload with an `IServiceProvider` lets the options read other services; `MapPragueEndpoints` is covered in [Inspect over HTTP](inspect-over-http.md).

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/HostWithDi.cs#host-aspnet)]

## 3. Options from configuration

The named `KafkaCachesOptions` are bound from the configuration section first (`"kafkaCaches"` unless you pass a name), then your delegate runs on top. `Vars` fills `[v:name]` placeholders in topic templates; `[e:NAME]` reads an environment variable.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/HostWithDi.cs#host-options-binding)]

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/HostWithDi.cs#host-options-code-override)]

An unresolved placeholder is left in the string: the test `AnUnresolvedPlaceholderIsLeftAsIs` gets a topic literally named `orders.[v:env]`. Check `cache.Topic` at startup if your templates carry placeholders.

## 4. Several clusters

Each `AddKafkaCaches(sectionName, …)` call is a cluster: its own options, consumer and keyed producer. One hosted worker drives all of them. `AddInternalCache` puts a cache in the registry (so joins resolve and it can be written locally) without consuming a topic.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/HostWithDi.cs#host-two-clusters)]

`AddOrUpdateAndProduce` writes locally and produces to the cache's topic only when the value actually changed; `RemoveAndProduce` removes locally and produces a tombstone.

## 5. Start work after the load

Await the registry's `LoadingCompletion` at the top of `ExecuteAsync`, with the stopping token. `BackgroundService.StartAsync` returns at that first `await`, so the worker can be registered before or after `AddKafkaCaches`, and shutdown cancels the wait.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/HostWithDi.cs#host-after-load-worker)]

Never wait for the load in a hosted service's `StartAsync`. The host starts services one at a time in registration order, so a `StartAsync` that waits, registered before `AddKafkaCaches`, holds back the Kafka worker that completes the load: the host never starts. `KafkaCachesBackgroundService` is obsolete: derive from `BackgroundService` as above, so the host logs a job that throws and applies `HostOptions.BackgroundServiceExceptionBehavior`. The host does neither for a plain `IHostedService`.

## 6. The registry

Only the caches you `AddCache` are DI services. Caches pulled in through foreign keys, and global indices, are reachable through `IDataCacheRegistry`; register a factory if you want them injected.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/HostWithDi.cs#host-registry)]

## 7. Shutdown

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/HostWithDi.cs#host-shutdown)]

## Pitfalls

- **`AddHostedService<MyWorker>()` registered before `AddKafkaCaches`.** It starts first, on empty caches. Await `LoadingCompletion.WaitAsync(stoppingToken)` at the top of `ExecuteAsync` (step 5) rather than relying on registration order.
- **No `IConfiguration` in a hand-built `ServiceCollection`.** The options binder needs one; add `services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())` as the tests do.
- **Expecting `GetRequiredService<CustomerCache>()` to work because `Order` references it.** It throws unless you `AddCache` it or register a factory; see step 6.
- **Resolving the producer unkeyed with several clusters.** It is a keyed singleton: `[FromKeyedServices("sectionName")]`.
- **A `group.id` shared across replicas.** Each replica must replay the whole topic; give each its own group or none. [Kafka Integration](../advanced/kafka-integration.md) explains the consumer model.
