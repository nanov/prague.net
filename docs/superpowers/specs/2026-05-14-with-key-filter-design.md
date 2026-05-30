# `WithKeyFilter` — key-level message filtering

Date: 2026-05-14
Status: Approved — ready for implementation

## Goal

Mirror the existing `WithHeaderFilter` feature for **keys**: let cache builders reject messages by inspecting the deserialized key (`TKey`) before the value is processed, with the same drop-it-everywhere semantics header filters already have (apply during initial load, apply to tombstones, fire `ExecuteAfterHandlersFilter` for observers).

## Non-goals

- No equals / not-equals / exists fast-path overloads in this iteration. The typed predicate covers all uses; specialized overloads can be added later if benchmarks justify them.
- No public class-based filter type (`IKafkaKeyFilter<TKey>`). Stay symmetric with the current public header-filter surface, which is delegate-only. Internal types are designed so a class-based overload can be added without breaking changes.
- No retrofit of header filters. Improvements to header-filter error handling are out of scope here.

## Public API

A single builder method on `KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>`:

```csharp
public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithKeyFilter(
    Func<TKey, bool> predicate);
```

- Returns `true` to keep the message; `false` to drop it.
- Multiple `WithKeyFilter` calls compose with **AND** (every predicate must return `true`).
- No `passOnNull` parameter — keys cannot be null on a compacted topic and the consumer never observes a null key.
- Order of registration does not affect correctness (AND is commutative); filters are invoked in registration order.

## Semantics

| Scenario | Behavior |
|---|---|
| Predicate returns `true` | Message proceeds normally (load buffer during initial load; `HandleUpdate` / `HandleDelete` afterwards). |
| Predicate returns `false`, initial load | Value bytes disposed; message dropped from the load buffer. Cache never sees the key. No after-handler notification (load phase suppresses `ExecuteAfterHandlersFilter`, matching existing `_isLoading` gate). |
| Predicate returns `false`, live phase | Value bytes disposed; `ExecuteAfterHandlersFilter` fires (`UpdateType.Filtered`). Same observable behavior as a header-filter rejection. |
| Predicate returns `false`, tombstone (null value) | Same as any other rejection — drop. No `HandleDelete` is invoked. |
| Predicate throws | Wrapped at the channel-loop call site: log via structured `KeyFilterError` log message, treat as **reject**. Rationale: if we cannot determine whether the key passes, the safer default for cache integrity is to not admit it. |

## Internal types

New file: `src/Prague.Kafka/Filters/KeyFilters.cs`.

```csharp
namespace Prague.Kafka.Filters;

using System.Runtime.CompilerServices;

internal sealed class KafkaKeyFilters<TKey> {
    private static readonly KafkaKeyFilters<TKey> _empty = new(Array.Empty<KafkaKeyFilter<TKey>>());

    private readonly KafkaKeyFilter<TKey>[] _filters;

    private KafkaKeyFilters(KafkaKeyFilter<TKey>[] filters) => _filters = filters;

    internal bool IsEmpty {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _filters.Length == 0;
    }

    internal static KafkaKeyFilters<TKey> Create(IReadOnlyList<KafkaKeyFilter<TKey>>? filters) {
        if (filters is null || filters.Count == 0)
            return _empty;
        var arr = new KafkaKeyFilter<TKey>[filters.Count];
        for (var i = 0; i < filters.Count; i++)
            arr[i] = filters[i];
        return new KafkaKeyFilters<TKey>(arr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ShouldProcess(TKey key) {
        foreach (var filter in _filters)
            if (!filter.ShouldProcess(key))
                return false;
        return true;
    }
}

internal abstract class KafkaKeyFilter<TKey> {
    public abstract bool ShouldProcess(TKey key);
}

internal sealed class KafkaKeyPredicateFilter<TKey> : KafkaKeyFilter<TKey> {
    private readonly Func<TKey, bool> _predicate;
    public KafkaKeyPredicateFilter(Func<TKey, bool> predicate) => _predicate = predicate;
    public override bool ShouldProcess(TKey key) => _predicate(key);
}
```

Design notes:
- Generic in `TKey` — unlike `KafkaHeaderFilters` which is byte-bound. Each closed generic instantiation specializes the array iteration and devirtualizes the sealed `KafkaKeyPredicateFilter<TKey>` call.
- `_empty` is one instance per closed generic, shared across handlers.
- `ShouldProcess` does **not** wrap individual filters in `try`/`catch`. The wrap lives at the call site so the exception-handling policy stays in the channel loop (one place, easy to evolve).

## Builder changes

In `KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>` (`src/Prague.Kafka/DependencyInjection.cs`):

```csharp
private List<KafkaKeyFilter<TKey>>? _keyFilters;

public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithKeyFilter(Func<TKey, bool> predicate) {
    _keyFilters ??= new List<KafkaKeyFilter<TKey>>();
    _keyFilters.Add(new KafkaKeyPredicateFilter<TKey>(predicate));
    return this;
}
```

`Build(...)` passes `KafkaKeyFilters<TKey>.Create(_keyFilters)` to the handler constructor.

## Handler changes

`KafkaCacheHandler<TCacheEntity, TKey, TValue>` (`src/Prague.Kafka/IO/KafkaCacheConsumer.cs`):

- New ctor parameter `KafkaKeyFilters<TKey> keyFilters`, stored in a `readonly` field `_keyFilters`.
- The non-generic base `KafkaCacheHandler` is untouched — key filters are generic in `TKey` and must live on the generic subclass.

## Channel-loop integration

Insert immediately after the existing key dispose (current line 290 in `KafkaCacheHandler<...>.ChannelLoop`):

```csharp
if (!_keyFilters.IsEmpty) {
    bool shouldProcess;
    try {
        shouldProcess = _keyFilters.ShouldProcess(key);
    } catch (Exception e) {
        _logger.KeyFilterError(e, Name, result.Offset);
        shouldProcess = false;
    }
    if (!shouldProcess) {
        result.Message.Value.Dispose();
        if (!isLoading)
            await ExecuteAfterHandlersFilter();
        continue;
    }
}
```

Performance properties:
- **No-filter handlers**: one branch on `_keyFilters.IsEmpty` (inlined field-length read). Zero allocations, no virtual calls, no try entered.
- **Filter-configured handlers**: one virtual `ShouldProcess` call per filter (sealed leaf — JIT devirtualizes), one delegate invocation per predicate. No boxing for value-type keys.
- `try` body is minimal (single call), satisfying the high-performance-net rule that try/catch must not engulf surrounding code on hot paths.

## Logging

Add a `LoggerMessage`-generated method to `KafkaCacheConsumerLog` (the existing partial in `IO/KafkaCacheConsumer.cs`):

```csharp
[LoggerMessage(Level = LogLevel.Error,
    Message = "[Prague] Key filter predicate threw for {CacheName} - {Offset}")]
public static partial void KeyFilterError(this ILogger logger, Exception exception, string cacheName, long offset);
```

No string interpolation; structured args are templated by the source generator.

## Testing

Unit tests — `tests/Prague.Kafka.Tests/Filters/KafkaKeyFiltersTests.cs`:

- `IsEmpty` is `true` for `Create(null)` and `Create(empty list)`.
- `IsEmpty` is `false` once any filter is added.
- Single predicate: accepts when predicate returns `true`; rejects when `false`.
- Multiple predicates AND: both must return `true`; if any returns `false`, `ShouldProcess` returns `false`.
- Order of registration does not affect AND result.
- Predicate exception propagates from `ShouldProcess` (call-site try/catch is verified separately).

Integration tests — `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersIntegrationTests.cs` (parallel to `KafkaHeaderFiltersIntegrationTests`):

- Produce messages with assorted keys. Configure `WithKeyFilter(k => …)`. Assert only matching keys appear in the cache after initial load.
- Produce additional messages live. Assert filtered keys never enter cache; assert `ExecuteAfterHandlersFilter` is observed via a test after-handler that counts `UpdateType.Filtered`.
- Produce tombstones (null value) with filtered keys. Assert the cache is unchanged and no `HandleDelete` fires.
- Configure two predicates via two `WithKeyFilter` calls. Assert AND composition: only keys that pass both end up in the cache.
- Configure a predicate that throws. Assert the channel loop logs `KeyFilterError`, drops the message, and continues processing subsequent messages.

## Files touched

| File | Change |
|---|---|
| `src/Prague.Kafka/Filters/KeyFilters.cs` | **NEW** — types described above. |
| `src/Prague.Kafka/DependencyInjection.cs` | Add `_keyFilters` field, `WithKeyFilter` builder method, pass `KafkaKeyFilters<TKey>.Create(_keyFilters)` to handler ctor. |
| `src/Prague.Kafka/IO/KafkaCacheConsumer.cs` | Add `_keyFilters` field + ctor parameter on `KafkaCacheHandler<TCacheEntity, TKey, TValue>`. Insert filter block in `ChannelLoop`. Add `KeyFilterError` `LoggerMessage`. |
| `tests/Prague.Kafka.Tests/Filters/KafkaKeyFiltersTests.cs` | **NEW** — unit tests. |
| `tests/Prague.Kafka.TestAdaptor.Tests/KeyFilters/KafkaKeyFiltersIntegrationTests.cs` | **NEW** — integration tests modeled on the header equivalent. |
