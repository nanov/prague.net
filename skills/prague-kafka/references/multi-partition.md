# Multi-partition cache topics (since 0.10.0)

A cache topic with more than one partition loads and produces correctly as long as every write for a key lands on one partition. Prague's keyed partitioner and the rules below keep it there.

## Creating and loading

- Fix the partition count when the topic is created. Adding partitions to a live topic is unsupported: keys re-map to other partitions, and a partition first assigned after the load is logged as a warning. To change the count, recreate the topic and restart every consumer.
- The load waits for every partition of the topic (the count is read from metadata at startup), not only the assigned ones. With a shared `group.id`, an instance that holds part of the topic never finishes loading.
- Readiness reports `NoPartitionAssigned` while fewer partitions are assigned than the topic has. `KafkaDataCacheStatistics.PartitionCountUnsafe` holds the count; the generated cache exposes it as `PartitionCount` (0 until the consumer starts).

## Producing

Each consumed record keeps its partition (`__PragueMetadata__.Partition`). Without an explicit partition:
- an update or tombstone for a resident key goes to the key's stored partition;
- a new key goes where the partitioner sends it;
- `ForceProduceAll()` re-produces each record to its stored partition.

Every produce method has a partition-first overload:

```csharp
orders.AddOrUpdateAndProduce(2, order);               // produce to partition 2
orders.AddOrUpdateAndProduce(2, order, force: true);  // the force overload takes one too
orders.RemoveAndProduce(2, order.OrderId);            // tombstone to partition 2
```

Both checks run before the local write, so a rejected call changes nothing:
- the partition must exist (`ArgumentOutOfRangeException`);
- it must equal a resident key's stored partition, because a key's partition is immutable (`InvalidOperationException`). To move a key, delete it on its old partition first.

A key created in this process without a partition has no stored partition until a restart reads it back (the process never consumes its own writes), so an explicit partition on such a key is not checked.
