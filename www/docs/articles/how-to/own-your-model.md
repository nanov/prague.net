---
title: Own Your Model
description: Keep your own [DataCache] copy of a topic's contract in the consuming service, with only the properties and indexes that service needs, and know what the wire format allows you to change.
---

# Own Your Model

**Goal:** a cache for a topic another team produces, declared in your service with only the fields and indexes your queries need, without a shared contracts package between you.

**Prerequisite:** a topic written with Prague's serializer (`KafkaCacheProducer` or `AddOrUpdateAndProduce`) or any MessagePack map keyed by property name. The code is in [`OwnYourModel.cs`](https://github.com/nanov/prague.net/blob/main/samples/Prague.Samples/HowTo/OwnYourModel.cs); every block runs as a test.

## Why a copy

Indexes are a consumer concern. The producer does not know that one service looks shipments up by carrier and another by status, and it should not have to ship a release when either changes. Two constraints make a copy the default rather than a preference:

- A `[DataCache]` type must be `partial` and compiled in your project; the generator completes it with `CacheEquals`, `Clone` and the index plumbing. A class from a referenced contracts assembly cannot be cached, not even through [`[DataCache<T>]`](../core-concepts/index-types.md#external-types).
- Every property you hold costs memory per row, takes part in equality, and is one more thing a producer change can break. Hold what you read.

## 1. Declare the copy

Same property names as the producer's type. Everything else is yours: the namespace, the type name, the enum type, which properties exist, and which of them are indexed.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/OwnYourModel.cs#own-model-copy)]

## 2. What the wire allows

Prague serializes a value as a MessagePack map keyed by property name, with no type or namespace name in it. So the copy reads the producer's bytes as they are:

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/OwnYourModel.cs#own-model-wire)]

| Change in your copy | Effect |
|---|---|
| Different type name or namespace | none |
| Property left out | skipped on read |
| Property the producer does not send | default value |
| Your own enum type | read by numeric value; keep the numbers aligned |
| Wider number (`int` → `long`) | reads fine |
| Renamed property | **reads as default, silently** |

## 3. Equality follows your copy

`AddOrUpdate` compares with the generated `CacheEquals`, which only sees the properties you declared. A producer update that only touches fields you do not hold arrives as `Same`: no index write, no after-handler `Update`.

[!code-csharp[](../../../../samples/Prague.Samples/HowTo/OwnYourModel.cs#own-model-equality)]

## Pitfalls

- **A renamed or misspelled property.** The read succeeds and the value is the default. Copy names exactly, and cover each copied property in a test that reads a producer-shaped message, like the sample does.

  [!code-csharp[](../../../../samples/Prague.Samples/HowTo/OwnYourModel.cs#own-model-rename-pitfall)]

- **Producing from a partial copy.** `AddOrUpdateAndProduce` serializes *your* type, so the message lacks every property you dropped, and on a compacted topic it replaces the full record for every consumer. Only the owner of a topic produces to it, from the complete contract.
- **Renumbered enums.** Enums travel as numbers. If the producer inserts a member in the middle, your copy reads the wrong member with no error. Ask producers to append members only.
- **Producer renames.** A rename on their side is a removal plus an addition on the wire. Your copy keeps reading the default until you follow the rename.

## Next

- [Index Types](../core-concepts/index-types.md): what to index for which query.
- [Conditional Updates](../core-concepts/conditional-updates.md): equality and `[DataCacheIgnoreEquality]`.
- [Kafka Integration](../advanced/kafka-integration.md): wiring the cache to its topic.
