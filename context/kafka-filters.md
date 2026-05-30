# Kafka message filters

> **Read when:** adding/changing header/key/value filters or the `FilterDecision` skip-vs-delete logic.

Filter types live under `src/Prague.Kafka/Filters/`. `KafkaCacheHandlerBuilder` builder methods, all **AND-composed** across calls:

- `WithHeaderFilter(...)` — evaluated **first**, in the raw consume loop via `KafkaCacheHandler.IsHeaderFiltered(in RawHeaders)` against UTF-8 name/value **spans** (before key deserialization), returns `bool`. **No `treatAsDelete`.** It also self-filters the producer-instance header (`KafkaCaches.ProducerInstanceIdHeaderName` == this instance's id) so a producer never re-consumes its own writes.
- `WithKeyFilter(Func<TKey,bool>, bool treatAsDelete = false)`
- `WithValueFilter(Func<TValue,bool>, bool treatAsDelete = false)`

No-filter path is zero-alloc (inline `IsEmpty` check). A thrown predicate is caught at the `DispatchRaw` call site, logged via `LoggerMessage`, and treated as **reject** (maps to `Skip`, never `Delete`).

## FilterDecision (key + value share it)

`FilterDecision { Accept, Skip, Delete }` (`Filters/FilterDecision.cs`). Aggregates `KafkaKeyFilters<TKey>.Evaluate(key)` / `KafkaValueFilters<TValue>.Evaluate(value)` return it; each concrete filter carries `internal abstract bool TreatAsDelete`. **First-reject-wins** — the first rejecting filter's flag picks `Delete` vs `Skip`:

- `Skip` → silent drop on load / live publishes `RAW_KIND_FILTERED` → after-handlers fire with `UpdateType.Filtered`.
- `Delete` → live publishes `RAW_KIND_DELETE` → `HandleRawLiveDelete` (removes key, fires `UpdateType.Delete` with old value only if key was present) / on load removes the key from the `ValueCompactingBuffer` (no after-handler).

**Caveat:** a key is immutable, so key-filter `treatAsDelete` only evicts when the predicate closes over mutable external state and a *new* message for that key arrives; for pure key predicates it is inert.

Eval site: key and value filters both run inside `KafkaCacheHandler.DispatchRaw` (`IO/KafkaCacheConsumer.cs`), branching on the `isLoading` flag; the header filter runs earlier in `ConsumeRawLoop` via `IsHeaderFiltered`.
