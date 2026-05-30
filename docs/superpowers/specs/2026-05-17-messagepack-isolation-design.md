# MessagePack isolation + `DateTime` back-compat formatter

Date: 2026-05-17
Status: Drafted — pending user review

## Goal

Make Prague immune to host-side mutations of `MessagePack.MessagePackSerializer.DefaultOptions`. Today, **every** internal `Serialize` / `Deserialize` call in Prague reads `DefaultOptions` at call time, so a host like

```csharp
MessagePackSerializer.DefaultOptions = MessagePackSerializerOptions.Standard
    .WithResolver(CompositeResolver.Create(
        TypelessContractlessStandardResolver.Instance,
        StandardResolver.Instance));
```

silently reshapes the wire format Prague reads and writes. Goal: route every Prague MessagePack call through Prague-owned options, and expose a focused builder method for hosts that need to add resolvers on top of ours.

Secondary goal: ship a `DateTime` formatter that **reads both** the legacy `NativeDateTimeResolver` int64 encoding currently produced in production AND the canonical MessagePack timestamp ext, while continuing to **write the legacy int64** so existing topics keep round-tripping byte-for-byte.

## Non-goals

- No per-cache resolver override. Strict additive extension if we ever need it.
- No on-the-fly migration from native → standard ext write format. v1 always writes native; a `WithDateTimeWriteFormat(...)` knob can land later if cross-language interop needs it.
- No `DateTimeOffset` formatter in v1. Same dual-format pattern applies; defer until a real entity needs it.
- No automatic detection / warning when a host mutates `MessagePackSerializer.DefaultOptions` post-init. Out of scope.
- No retrofit of `HeadersSerDe.SerializeInt` / `SerializeLong` / `TryDeserializeInt` / `TryDeserializeLong` (the legacy raw `BitConverter` helpers). They remain public API and untouched.

## Where `DefaultOptions` leaks today

Every site below passes no options → reads `DefaultOptions`:

| File | Line(s) | Purpose |
|---|---|---|
| `src/Prague.Kafka/SerDe/HeadersSerDe.cs` | 94, 103, 119 | `SerializeMessagePack`, `TryDeserializeMessagePack`, `TryDeserializeMessagePackExact` |
| `src/Prague.Kafka/Filters/HeaderFilters.cs` | 128, 186, + 3 more | Fallback `Deserialize<T>` in 5 filter classes |
| `src/Prague.Kafka/SerDe/RentedBytesDeserializer.cs` | 84, 87, 90 | `CacheSerde<T>` — entity-value Serialize/Deserialize |
| `src/Prague.Codegen/CacheGenerator.cs` | 4414 | Dump (`.pkd`) writer: `MessagePackSerializer.Serialize(results)` |
| `src/Prague.Kafka.TestAdaptor/DependencyInjection.cs` | 63 | Dump (`.pkd`) reader: `MessagePackSerializer.Deserialize<List<PragueConsumeResult>>` |

All five sites must route through `PragueMessagePack.Options`.

The Enricher/Dericher numeric header paths emitted by codegen already call `HeadersSerDe.SerializeMessagePack` / `TryDeserializeMessagePackExact`, so once those helpers route through `PragueMessagePack.Options`, codegen needs no change beyond the single Dump line.

## Public API

### Where the options live

```csharp
namespace Prague.Kafka;

public static class PragueMessagePack {
    public static MessagePackSerializerOptions Options { get; private set; } = DefaultOptions();

    internal static void Configure(MessagePackSerializerOptions options) { /* see below */ }

    internal static MessagePackSerializerOptions DefaultOptions() =>
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                PragueDateTimeResolver.Instance,                       // first → wins for DateTime (dual-read)
                TypelessContractlessStandardResolver.Instance));       // matches the snippet historically applied
                                                                       // by hosts via DefaultOptions mutation
}
```

**Why `TypelessContractlessStandardResolver`, not vanilla `StandardResolver`:** the snippet hosts have been mutating `DefaultOptions` with also enables `NativeGuidResolver` (Guid as raw 16 bytes vs ext), `NativeDecimalResolver` (decimal as raw 16 bytes vs string), contractless POCO support, and typeless `object` round-trip. Existing production topics already contain data encoded under those rules. Mirroring them as Prague's default makes the migration **byte-identical on the write side** for every entity, header, and dump file currently in flight. The dual-read `DateTime` formatter sits in front so back-compat reads remain a property of the default, not an opt-in.

**Caveats** (documented for the day someone asks):
- `TypelessContractlessStandardResolver` uses **reflection + `IL.Emit`** to generate formatters for non-`[MessagePackObject]` types. Not NativeAOT-safe. Hosts shipping AOT consumers should opt out via `WithMessagePackResolver(_ => StandardResolver.Instance)`.
- `TypelessFormatter` is the canonical MessagePack "deserialization gadget" surface — it embeds .NET type names in `object`-typed properties and instantiates them on read. This is already the security posture of every current Prague consumer (because the host mutation forced it). Hosts that want a tighter posture opt out via the same builder method.

`Options` is a `get; private set;` static. Reads are a single field load — no `Volatile.Read` because options are set exactly once at app init, well before any consumer thread starts (DI registration is single-threaded). Adopting a static here is intentional: `MessagePackSerializerOptions` is itself designed to be a long-lived shared singleton.

**`Configure` semantics:**
- First call: sets `Options`.
- Subsequent call with the same reference: no-op.
- Subsequent call with a different reference: `throw new InvalidOperationException("PragueMessagePack.Configure called twice with conflicting options. Configure once at startup.")`.

This catches the bug where two `AddKafkaCaches` invocations install different resolvers.

### The builder method on `AddKafkaCaches`

A new optional callback parameter, added to all `AddKafkaCaches` overloads:

```csharp
public static IServiceCollection AddKafkaCaches(
    this IServiceCollection services,
    string configsSectionName,
    Action<KafkaCachesOptions, IServiceProvider>? configsFactory,
    Action<KafkaCacheHandlersBuilder> configure,
    Action<KafkaCachesGlobalOptionsBuilder>? options = null);
```

The `options` callback receives a `KafkaCachesGlobalOptionsBuilder` (new builder type). Today it hosts one method; future lib-wide knobs land there without growing the signature again.

```csharp
public sealed class KafkaCachesGlobalOptionsBuilder {
    public KafkaCachesGlobalOptionsBuilder WithMessagePackResolver(
        Func<IFormatterResolver, IFormatterResolver> compose);
}
```

Usage:

```csharp
services.AddKafkaCaches(
    configsSectionName: "kafkaCaches",
    configsFactory: o => { /* ... */ },
    configure: b => { b.AddCache<MyCache, int, MyEntity>("my-topic"); },
    options: o => o.WithMessagePackResolver(defaultResolver =>
        CompositeResolver.Create(
            MyTypelessResolver.Instance,
            defaultResolver))                  // defaultResolver = Prague composite (PragueDateTimeResolver + StandardResolver)
);
```

**Composition contract:** the `defaultResolver` argument passed to the lambda is **Prague's composite** (`PragueDateTimeResolver.Instance` + `TypelessContractlessStandardResolver.Instance`). This guarantees both the `DateTime` back-compat formatter AND the native Guid/decimal/contractless/typeless behavior survive any user composition that "extends" the default rather than replaces it. Hosts that want a tighter posture (e.g., NativeAOT-safe or no typeless gadget surface) can call `WithMessagePackResolver(_ => StandardResolver.Instance)` and explicitly drop the back-compat layer.

**Wiring:** the `AddKafkaCaches` method instantiates the builder, invokes the action, then calls `PragueMessagePack.Configure(...)` exactly once.

### Resolver / formatter

```csharp
namespace Prague.Kafka.SerDe;

public sealed class PragueDateTimeResolver : IFormatterResolver {
    public static readonly PragueDateTimeResolver Instance = new();
    private PragueDateTimeResolver() { }

    public IMessagePackFormatter<T>? GetFormatter<T>() {
        if (typeof(T) == typeof(DateTime))
            return (IMessagePackFormatter<T>)(object)PragueDateTimeFormatter.Instance;
        return null; // fall through to next resolver in the composite
    }
}

public sealed class PragueDateTimeFormatter : IMessagePackFormatter<DateTime> {
    public static readonly PragueDateTimeFormatter Instance = new();
    private PragueDateTimeFormatter() { }

    public void Serialize(ref MessagePackWriter writer, DateTime value, MessagePackSerializerOptions options)
        => writer.Write(value.ToBinary());                  // legacy int64 — Kind-preserving, .NET-fast

    public DateTime Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
        switch (reader.NextMessagePackType) {
            case MessagePackType.Integer:                   // legacy native binary
                return DateTime.FromBinary(reader.ReadInt64());
            case MessagePackType.Extension:                 // canonical timestamp ext
                return NativeDateTimeStandardRead(ref reader, options);
            default:
                throw new MessagePackSerializationException(
                    $"Unexpected MessagePack token while reading DateTime: {reader.NextMessagePackType}");
        }
    }

    private static DateTime NativeDateTimeStandardRead(ref MessagePackReader reader, MessagePackSerializerOptions options) {
        // Delegate to MessagePack's standard DateTimeFormatter (reads ext type -1 in all length variants).
        return MessagePack.Formatters.NativeDateTimeFormatter.Instance.Deserialize(ref reader, options);
        // NOTE: NativeDateTimeFormatter.Deserialize handles both ext-encoded timestamps AND int64-encoded
        // values — but we still dispatch by token to keep our intent explicit and to avoid relying on a
        // bug-class in MessagePack-CSharp's accept-anything reader. Alternative: MessagePack.Formatters.DateTimeFormatter
        // (the canonical ext-only reader) — pick whichever exists in the pinned MessagePack-CSharp version.
    }
}
```

**Implementation note:** the exact name of the "canonical timestamp ext only" reader in MessagePack-CSharp is `DateTimeFormatter.Instance` (in `MessagePack.Formatters`). The plan verifies the assembly's symbol name before locking the call; both readers exist and either suffices for the `Extension` branch.

`DateTime?` is handled by MessagePack's built-in `NullableFormatter<DateTime>` wrapper — no explicit registration. Arrays/lists/properties of `DateTime` flow through their generic formatters which call ours per element.

## Internal changes — call-site rewrites

Every `MessagePackSerializer.Serialize`/`Deserialize` call inside Prague gets an explicit `PragueMessagePack.Options` argument.

**`HeadersSerDe.cs`:**

```csharp
public static byte[] SerializeMessagePack<T>(T value)
    => MessagePackSerializer.Serialize(value, PragueMessagePack.Options);

public static bool TryDeserializeMessagePack<T>(byte[] bytes, out T? value) {
    try {
        value = MessagePackSerializer.Deserialize<T>(bytes, PragueMessagePack.Options);
        return true;
    } catch { value = default; return false; }
}

public static bool TryDeserializeMessagePackExact<T>(byte[] bytes, out T? value) {
    try {
        value = MessagePackSerializer.Deserialize<T>(bytes.AsMemory(), PragueMessagePack.Options, out var bytesRead);
        if (bytesRead == bytes.Length) return true;
        value = default; return false;
    } catch { value = default; return false; }
}
```

**`HeaderFilters.cs`** — five filter classes' fallback `Deserialize<T>` paths:

```csharp
var val = MessagePackSerializer.Deserialize<T?>(headersBytes, PragueMessagePack.Options);
```

**`RentedBytesDeserializer.cs` / `CacheSerde<T>`:**

```csharp
public static T Deserialize(RentedBytes bytes)
    => MessagePackSerializer.Deserialize<T>(bytes.AsMemory(), PragueMessagePack.Options);
internal static T Deserialize(RentedBytesWithHandler bytes)
    => MessagePackSerializer.Deserialize<T>(bytes.AsMemory(), PragueMessagePack.Options);
public static byte[] Serialize(T value)
    => MessagePackSerializer.Serialize<T>(value, PragueMessagePack.Options);
```

**`CacheGenerator.cs`** (codegen, line 4414):

```csharp
w.Line("var bytes = MessagePack.MessagePackSerializer.Serialize(results, Prague.Kafka.PragueMessagePack.Options);");
```

**`TestAdaptor/DependencyInjection.cs`** (line 63):

```csharp
var records = MessagePackSerializer.Deserialize<List<Dump.PragueConsumeResult>>(bytes, PragueMessagePack.Options);
```

## Wiring `Configure` into `AddKafkaCaches`

In `DependencyInjection.cs`, modify the root `AddKafkaCaches(services, configsSectionName, configsFactory, configure, options)` overload to invoke the builder and configure the static **before** any handler/consumer is constructed:

```csharp
if (options is not null) {
    var optsBuilder = new KafkaCachesGlobalOptionsBuilder();
    options(optsBuilder);
    PragueMessagePack.Configure(optsBuilder.Build());
}
// ... existing wiring below
```

Existing overloads that don't take `options` pass `null` through. `PragueMessagePack.Options` remains at its default if no overload supplies one.

`KafkaCachesGlobalOptionsBuilder.Build()` returns the final `MessagePackSerializerOptions`:

```csharp
internal MessagePackSerializerOptions Build() {
    if (_resolverCompose is null)
        return PragueMessagePack.DefaultOptions();
    var composed = _resolverCompose(CompositeResolver.Create(
        PragueDateTimeResolver.Instance,
        TypelessContractlessStandardResolver.Instance));
    return MessagePackSerializerOptions.Standard.WithResolver(composed);
}
```

## Performance properties

- **Default path (no `options` callback):** byte-identical wire format to today (Prague's composite is `PragueDateTimeResolver` + `TypelessContractlessStandardResolver` — same Guid/decimal/DateTime/typeless behavior the legacy host mutation produced). One extra static field load per call, no allocation. Per-type formatter resolution is cached inside MessagePack-CSharp's resolver — first read of each type pays a reflection cost, all subsequent reads hit the cache.
- **Configured path:** the user-composed resolver runs once at startup. Per-call cost is unchanged — `MessagePackSerializerOptions` caches per-type formatters.
- **Hot path:** no virtual dispatch added. `PragueDateTimeFormatter` is `sealed`, JIT devirtualizes calls through `IMessagePackFormatter<DateTime>` because MessagePack-CSharp's generated code dispatches via the resolver's cached delegate. Single-byte token peek (`reader.NextMessagePackType`) is what we already pay for in MessagePack reads.
- **Codegen change:** one emitted line gains a fully-qualified options reference. No new allocations in codegen output.

## Testing

### Unit — `tests/Prague.Kafka.Tests/SerDe/`

1. `PragueDateTimeFormatterTests`:
   - `Serialize_ProducesLegacyInt64Encoding` — round-trip a `DateTime`, assert wire bytes are 9 bytes starting with `0xd3` (MessagePack int64 prefix).
   - `Deserialize_FromLegacyInt64_RoundTripsTicksAndKind` — feed `value.ToBinary()` int64 bytes for `Utc`, `Local`, `Unspecified` Kinds; assert ticks AND `Kind` are preserved.
   - `Deserialize_FromStandardTimestampExt_RoundTripsTicks` — feed bytes produced by MessagePack's `DateTimeFormatter` (FixExt4 and FixExt8 variants); assert ticks. `Kind` is `Utc` per ext-spec semantics.
   - `Deserialize_UnexpectedToken_Throws` — feed a fixed-string token, assert `MessagePackSerializationException`.

2. `PragueMessagePackConfigureTests`:
   - First call sets `Options`.
   - Same-reference re-call is no-op.
   - Conflicting re-call throws `InvalidOperationException`.
   - Test order independence: this test class manipulates a static — must reset via a test-only `internal static void ResetForTests()` helper guarded with `[Conditional("DEBUG")]` or via `InternalsVisibleTo`. We'll expose `ResetForTests()` as `internal` (already in the `InternalsVisibleTo` list per CLAUDE.md).

3. `IsolationFromDefaultOptionsTests`:
   - Test setup: mutate `MessagePackSerializer.DefaultOptions` to a deliberately-broken resolver (e.g., one that throws on any type lookup). Assert `HeadersSerDe.SerializeMessagePack<int>(42)` still succeeds.
   - Test teardown: restore `MessagePackSerializer.DefaultOptions = MessagePackSerializerOptions.Standard`.

### Integration — `tests/Prague.Kafka.IntegrationTests/`

`MessagePackIsolationTests` (NUnit, real Kafka):

1. `EntityWithDateTime_RoundTrips_NativeFormat` — produce an entity containing `DateTime.UtcNow`, consume via Prague cache, assert exact ticks + `Kind=Utc`. Also assert the value bytes on the wire contain a `0xd3` int64 prefix where the DateTime sits (sanity: we're writing native).
2. `EntityWithDateTime_PreservesKind_Local_Unspecified` — round-trip `DateTimeKind.Local` and `DateTimeKind.Unspecified` values; assert `Kind` preserved.
3. `LegacyTopicData_StandardTimestampExt_StillDecodes` — produce a message whose value is hand-crafted MessagePack bytes containing a standard-ext-encoded DateTime; assert Prague reads it via the dual-format path.
4. `HostMutatedDefaultOptions_DoesNotBreakProduction` — set `MessagePackSerializer.DefaultOptions` to a `CompositeResolver` matching the user's reported broken config; perform a full Prague produce→consume; assert it still works because Prague is using its own options.
5. `DefaultResolver_PreservesNativeGuidWireFormat` — entity with a `Guid` property; assert wire encoding is the raw 16-byte form (matches what `TypelessContractlessStandardResolver` emits), not the MessagePack ext form. Confirms zero-diff vs legacy host config.
6. `DefaultResolver_PreservesNativeDecimalWireFormat` — entity with a `decimal` property; assert raw-16-bytes encoding.
7. `DefaultResolver_RoundTripsContractlessPoco` — entity property of a type WITHOUT `[MessagePackObject]`; round-trip must succeed.

### Spec example for the `WithMessagePackResolver` callback — `tests/Prague.Kafka.Tests/DependencyInjection/`

`WithMessagePackResolverTests`:
1. Callback receives the Prague composite as `defaultResolver` (assert by `ReferenceEquals` to the expected composite).
2. Callback's return value becomes the active resolver (assert via a custom resolver that records calls).
3. Calling `AddKafkaCaches` twice with the same options compose function is allowed.
4. Calling `AddKafkaCaches` twice with conflicting options throws `InvalidOperationException`.

## Files touched

| File | Change |
|---|---|
| `src/Prague.Kafka/SerDe/PragueMessagePack.cs` | **NEW** — `PragueMessagePack` static class + `Configure` + `DefaultOptions`. |
| `src/Prague.Kafka/SerDe/PragueDateTimeFormatter.cs` | **NEW** — formatter (Serialize native, Deserialize dual). |
| `src/Prague.Kafka/SerDe/PragueDateTimeResolver.cs` | **NEW** — single-type resolver returning the formatter. |
| `src/Prague.Kafka/Options/KafkaCachesGlobalOptionsBuilder.cs` | **NEW** — builder with `WithMessagePackResolver`. |
| `src/Prague.Kafka/DependencyInjection.cs` | Add `options` parameter to every `AddKafkaCaches` overload; invoke `PragueMessagePack.Configure(builder.Build())` in the root overload. |
| `src/Prague.Kafka/SerDe/HeadersSerDe.cs` | Thread `PragueMessagePack.Options` into the 3 MessagePack calls. |
| `src/Prague.Kafka/Filters/HeaderFilters.cs` | Thread `PragueMessagePack.Options` into the 5 fallback `Deserialize<T>` calls. |
| `src/Prague.Kafka/SerDe/RentedBytesDeserializer.cs` | Thread `PragueMessagePack.Options` into the 3 `CacheSerde<T>` calls. |
| `src/Prague.Codegen/CacheGenerator.cs` | Emit `PragueMessagePack.Options` as second arg on line 4414 dump-writer. |
| `src/Prague.Kafka.TestAdaptor/DependencyInjection.cs` | Thread `PragueMessagePack.Options` into the dump-replay `Deserialize` call. |
| `tests/Prague.Kafka.Tests/SerDe/PragueDateTimeFormatterTests.cs` | **NEW**. |
| `tests/Prague.Kafka.Tests/SerDe/PragueMessagePackConfigureTests.cs` | **NEW**. |
| `tests/Prague.Kafka.Tests/SerDe/IsolationFromDefaultOptionsTests.cs` | **NEW**. |
| `tests/Prague.Kafka.Tests/DependencyInjection/WithMessagePackResolverTests.cs` | **NEW**. |
| `tests/Prague.Kafka.IntegrationTests/MessagePackIsolationTests.cs` | **NEW**. |
| `tests/Prague.Kafka.IntegrationTests/Entities/EntityWithDateTime.cs` | **NEW** — `[DataCache]` entity with `DateTime` properties for integration tests. |

## Open questions / explicit deferrals

- **`DateTimeOffset` formatter** — deferred until an entity actually uses it. Same dual-format pattern would apply.
- **`WithDateTimeWriteFormat(...)` knob** to switch the write side to canonical ext for cross-language interop — deferred. Strict additive extension when needed.
- **Per-cache resolver override** — deferred. Additive on the `AddCache<...>` builder.
- **Whether to expose `PragueMessagePack.Options` as public read** — yes. Allows host code that produces messages outside Prague's `KafkaCacheProducer` to encode with the same options when desired. The setter stays internal.

## Backward compatibility

- **Wire format unchanged** for any host that does not call `WithMessagePackResolver(...)`. DateTime continues to be int64 native bytes. Guid continues to be raw 16 bytes. Decimal continues to be raw 16 bytes. Contractless POCOs continue to round-trip. Typeless `object` properties continue to round-trip. Numeric headers continue to be MessagePack-encoded (per PR #38). The default mirrors `TypelessContractlessStandardResolver` exactly on the write side, so existing production topics — produced under the host's `DefaultOptions` mutation — keep their encoding byte-identical.
- **Read compatibility** for existing topics is strictly improved: Prague now reads both native and ext DateTime encodings, regardless of how the message was produced (legacy Prague, legacy host with native resolver, future Prague with canonical opt-in, or any other MessagePack producer).
- **No public API removal.** `HeadersSerDe.SerializeInt`/`SerializeLong`/`TryDeserializeInt`/`TryDeserializeLong` stay (legacy fallback + public API per CLAUDE.md).
