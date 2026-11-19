# Trigger check

Which skill should load for a prompt, judged from the descriptions alone, since the descriptions are all an agent sees before it opens a skill. The set assumes an agent that also has a KafkaLib skill set (`kafkalib-setup`, `kafkalib-consumers`, `kafkalib-producers`, `kafkalib-testing`), an org `kafka-contracts` skill and a general `high-performance-net` skill installed, because those are the neighbours the Prague descriptions compete with. "none" means no `prague-*` skill should load.

Graded by the author of the descriptions, reading each description against the prompt, not by running agent sessions: an independent grader or real sessions may disagree. A row passes when exactly the expected skill is the best match. "tie" means two descriptions claim the prompt about equally; "risk" means a wrong skill claims it as strongly as the right one.

| # | Prompt | Expected | Before | After |
|---|---|---|---|---|
| 1 | Add a Prague cache for the `payments.refund.v1` topic; I only need RefundId, Amount and Status. | prague-modeling | pass | pass |
| 2 | My `[DataCache]` class fails to build with CACHE041. | prague-modeling | pass | pass |
| 3 | What index do I put on `CreatedAtUnixMs` to fetch the last 24 hours? | prague-modeling | pass | pass |
| 4 | `DeliveredAtUnixMs` is a `long?` and I need a range query over it. | prague-modeling | pass | pass |
| 5 | I set `order.Status` on a row from `TryGet` and called `AddOrUpdate`, but `WithStatus` doesn't find it. | prague-modeling | tie: modeling / queries | pass |
| 6 | Bump a `Revision` field every time a write to the cache changes the row. | prague-modeling | pass | pass |
| 7 | The other team ships a `Caches` NuGet with their `[DataCache]` types; can I add an index to it? | prague-modeling | pass | pass |
| 8 | Endpoint: the 20 newest orders of a customer in one status, called 10k times a second. | prague-queries | pass | pass |
| 9 | Do I have to dispose what `ExecutePooled()` returns? | prague-queries | pass | pass |
| 10 | Page 2 sometimes repeats a row from page 1. | prague-queries | pass | pass |
| 11 | Show each order with its customer's name. | prague-queries | pass | pass |
| 12 | Give me everything that changed since the last sync watermark. | prague-queries | pass | pass |
| 13 | `BuildFrozen()` returns rows in a different order than the same `Query()`. | prague-queries | pass | pass |
| 14 | Use the caches in a console tool without Kafka, joins included. | prague-queries | tie: queries / testing | pass |
| 15 | Register `OrderCache` and `CustomerCache` in `Program.cs`. | prague-kafka | pass | pass |
| 16 | The cache topic name has to include the environment from config. | prague-kafka | pass | pass |
| 17 | A background job reads the caches; it must not run before they are loaded. | prague-kafka | tie: kafka / handlers | pass |
| 18 | This service owns the orders topic; write a changed order back to it. | prague-kafka | pass | pass |
| 19 | Kubernetes keeps restarting the pod while Prague loads. | prague-kafka | pass | pass |
| 20 | Our cache topic has 6 partitions; is that supported? | prague-kafka | pass | pass |
| 21 | Only accept messages whose `tenant` header is `acme`, in the Prague order cache. | prague-handlers | pass | pass |
| 22 | Cancelled orders must disappear from the cache; the producer never sends tombstones. | prague-handlers | pass | pass |
| 23 | Keep a per-customer open total up to date as orders arrive, including orders already in the topic at startup. | prague-handlers | pass | pass |
| 24 | My after-handler throws a NullReferenceException on `key` sometimes. | prague-handlers | pass | pass |
| 25 | Unit-test my order query service without Kafka. | prague-testing | pass | pass |
| 26 | Test that our copy of the Shipment type reads the producer's bytes. | prague-testing | tie: testing / modeling | pass |
| 27 | Integration test that a `treatAsDelete` filter removes the row. | prague-testing | tie: testing / handlers | pass |
| 28 | Prove the after-handler the cache calls is the same `OpenTotals` instance the endpoint injects. | prague-testing | pass | pass |
| 29 | Write an `IKafkaAsyncHandler` for `wallet.ledger.v1`. | none (kafkalib-consumers) | pass | pass |
| 30 | Skip messages without a `tenant` header in my KafkaLib consumer. | none (kafkalib-consumers) | risk: prague-handlers ("filter Kafka messages") | pass |
| 31 | Publish events and act on delivery reports. | none (kafkalib-producers) | pass | pass |
| 32 | Add the `kafka:` config section and `AddKafkaCluster`. | none (kafkalib-setup) | pass | pass |
| 33 | Offsets stopped committing in our consumer. | none (kafkalib-consumers) | pass | pass |
| 34 | Name a new topic and add it to kafka-ns. | none (kafka-contracts) | pass | pass |
| 35 | Add a field to the `BonusGranted` message without breaking consumers. | none (kafka-contracts) | risk: prague-modeling ("copying a message contract") | pass |
| 36 | Test a KafkaLib handler with `default(Context)`. | none (kafkalib-testing) | pass | pass |
| 37 | Cut allocations in this MessagePack parser. | none (high-performance-net) | pass | pass |
| 38 | `JoinWithCustomer()` returns only one order per customer. | prague-queries (points to prague-modeling for the cause) | pass | pass |
| 39 | Plug our own MessagePack resolver into Prague. | prague-kafka | pass | pass |
| 40 | Seed a `DataCacheRegistryBuilder` registry in my unit tests. | prague-testing | tie: testing / queries | pass |
| 41 | The open-totals projection double-counts orders after a restart. | prague-handlers | pass | pass |
| 42 | My unit test fails: `AddOrUpdateAndProduce` throws "Producer not configured". | prague-testing | tie: testing / kafka | pass |

Totals, as graded by the descriptions' author: the descriptions before this round (470 to 699 characters each, over the 300 limit) pass 33 of 42, with 7 ties and 2 risks; the current ones pass 42 of 42.

## How the ties were removed

- One owner per trigger. "Work that must start after the initial load" was in both `prague-kafka` and `prague-handlers`; it is now only `prague-kafka` ("waiting for the initial load"), and `prague-handlers` keeps the projection case ("projections over loaded data") and the symptom ("a handler misses or double-counts"). `DataCacheRegistryBuilder` is named only by `prague-queries`, whose `Not for` sends tests to `prague-testing`; `prague-testing` covers seeding the registry in tests.
- Every description leads with "Prague" or a Prague type name, so a generic Kafka prompt (filter a header, add a field to a message) matches a KafkaLib or contracts description first.
- `Not for` names the sibling that owns the neighbouring case.

Rows 5, 26, 27 and 42 still depend on the agent reading "Use when" over the type names: a prompt about a test that mentions `treatAsDelete` names a `prague-handlers` trigger. When a new row fails, fix the wording of the description that over-claims, not the prompt.
