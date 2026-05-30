# Kafka message filters

> **Read when:** adding/changing header/key/value filters or the `FilterDecision` skip-vs-delete logic.

Filter types live under `src/Prague.Kafka/Filters/`. `KafkaCacheHandlerBuilder` builder methods, all **AND-composed** across calls:

- `WithHeaderFilter(...)` — evaluated **upstream** in the `HeadersFiltering…` deserializer (before key deserialization), returns `bool`. **No `treatAsDelete`.**
- `WithKeyFilter(Func<TKey,bool>, bool treatAsDelete = false)`
- `WithValueFilter(Func<TValue,bool>, bool treatAsDelete = false)`

No-filter path is zero-alloc (inline `IsEmpty` check). A thrown predicate is caught at the channel-loop call site, logged via `LoggerMessage`, and treated as **reject** (maps to `Skip`, never `Delete`).

## FilterDecision (key + value share it)

`FilterDecision { Accept, Skip, Delete }` (`Filters/FilterDecision.cs`). Aggregates `KafkaKeyFilters<TKey>.Evaluate(key)` / `KafkaValueFilters<TValue>.Evaluate(value)` return it; each concrete filter carries `internal abstract bool TreatAsDelete`. **First-reject-wins** — the first rejecting filter's flag picks `Delete` vs `Skip`:

- `Skip` → silent drop on load / `ExecuteAfterHandlersFilter(UpdateType.Filtered)` live.
- `Delete` → `HandleDelete` live (removes key, fires `UpdateType.Delete` with old value only if key was present) / `_cache.Remove(key,…)` on load (no after-handler).

**Caveat:** a key is immutable, so key-filter `treatAsDelete` only evicts when the predicate closes over mutable external state and a *new* message for that key arrives; for pure key predicates it is inert.

Eval sites: key filter `KafkaCacheConsumer.cs` ~314 (both phases); value filter in `ChannelLoop` ~349 (live) and `FlushBuffer` ~249 (load).
