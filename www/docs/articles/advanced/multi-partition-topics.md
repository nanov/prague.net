# Multi-Partition Topics

Most caches read a topic with one partition and never need this page. Read it when a cache topic has, or will have, more than one partition: how the initial load decides it is finished, what readiness reports, and which partition each generated produce method writes to.

Every code block on this page compiles in `samples/Prague.Samples/Advanced/KafkaIntegration.cs`.

## Rules for the topic

- **Keep each key on one partition.** Kafka orders records within a partition only, and Prague applies records in the order it consumes them, without comparing timestamps. Per-key order holds only when every write for a key goes to the same partition. Kafka's keyed partitioner does that; so do Prague's produce methods (below).
- **Fix the partition count when you create the topic.** Adding partitions later re-maps keys, so a key can end up with live values on two partitions. Adding partitions while consumers run is unsupported: a partition first assigned after the initial load is logged as a warning and read through the live path.
- **Leave `group.id` unset.** Each process then consumes under its own group and is assigned every partition. With a shared `group.id` the instances split the partitions, and an instance that holds only part of a topic never finishes its initial load (see below).

## Initial load

When the consumer starts, it reads each topic's partition count from topic metadata. A cache's initial load ends when every partition of its topic has reached the high watermark it had when it was assigned, including partitions that are empty and partitions not assigned yet. Until then, records go through the load path: no after-handlers fire, and `WaitForInitialLoadAsync` keeps waiting.

If the metadata lookup fails, the load waits for the partitions that were actually assigned.

## Health

The count is published as `KafkaDataCacheStatistics.PartitionCountUnsafe`, next to `AssignedPartitionCountUnsafe`. Readiness reports `NoPartitionAssigned` whenever a cache has fewer partitions assigned than its topic has. That covers a cache that never got all of them (a shared `group.id`) and one that lost some in a rebalance after the load. When the count is unknown, the check falls back to "at least one partition".

## Producing

Each consumed record keeps the partition it was read from (`__PragueMetadata__.Partition`), and the generated cache exposes the topic's count as `PartitionCount` (0 until the consumer starts). The generated produce methods route by them:

| Call | Key not resident | Key resident on partition P |
| --- | --- | --- |
| `AddOrUpdateAndProduce(doc)`, `AddOrUpdateAndProduce(doc, force)` | the producer's partitioner chooses | P |
| `AddOrUpdateAndProduce(partition, doc)`, `AddOrUpdateAndProduce(partition, doc, force)` | `partition` | P, if `partition` is P; otherwise `InvalidOperationException` |
| `RemoveAndProduce(key)` | the producer's partitioner chooses | P |
| `RemoveAndProduce(partition, key)` | `partition` | P, if `partition` is P; otherwise `InvalidOperationException` |
| `ForceProduceAll()` | | each record to its stored partition |

A partition that does not exist on the topic throws `ArgumentOutOfRangeException`. Both checks run before the local cache is written, so a rejected call changes nothing. A key's partition is immutable because a write elsewhere would not replace the old value: a tombstone on the wrong partition deletes nothing, and the next load brings the key back.

[!code-csharp[](../../../../samples/Prague.Samples/Advanced/KafkaIntegration.cs#produce-to-partition)]

The written partition is stored on the document, so later writes of the key find it.

### Keys created without a partition

A process does not consume its own writes, so a key it created without a partition has no stored partition until a restart reads it back. Its later writes still go to the same partition, because they also go through the keyed partitioner, which always maps a key to the same partition. But an explicit partition on such a key is not checked against the partition the record is on.
