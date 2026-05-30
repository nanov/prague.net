# Kafka SerDe & MessagePack isolation

> **Read when:** touching header serialization, numeric header reads, `PragueMessagePack`, or any MessagePack call site.

## Numeric header SerDe (MessagePack-first)

`HeadersSerDe.SerializeMessagePack<T>` is the canonical **write** path for `int`/`int?`/`long`/`long?` headers. **Read path: always `TryDeserializeMessagePackExact<T>` FIRST** — the non-exact `TryDeserializeMessagePack<T>` silently misparses raw `BitConverter` bytes (reads the first byte as a positive fixint without consuming all bytes, returns a wrong value without throwing). The exact overload uses `ReadOnlyMemory<byte>` + `out int bytesRead` and returns `true` only when `bytesRead == bytes.Length`. After the exact check fails, fall back to `TryDeserializeInt` (4-byte raw) / `TryDeserializeLong` (8-byte raw) for back-compat.

This ordering lives in all 5 filter classes (`KafkaHeaderEqualsFilter<T>`, `KafkaHeaderNotEqualsFilter<T>`, `KafkaHeaderEqualsNumericFilter`, `KafkaHeaderNotEqualsNumericFilter`, `KafkaHeaderPredicateFilter<T>`) and the codegen Enricher. `SerializeInt`/`SerializeLong`/`TryDeserializeInt`/`TryDeserializeLong` are public API — **do not delete.**

## PragueMessagePack isolation

**Every internal MessagePack call site passes `PragueMessagePack.Options` explicitly — never `MessagePackSerializer.DefaultOptions`.** Host mutations of `DefaultOptions` must not affect Prague's wire format.

- `PragueMessagePack` (static, `Prague.Kafka` root). Sentinel pattern: `_defaultSentinel` from `CreateDefaultComposite()`; `_options` initialized to it.
- `CreateDefaultComposite()` builds a `CompositeResolver` with the built-in `MessagePack.Formatters.StringInterningFormatter` (uses `ConditionalWeakTable` — GC-reclaimable, no intern leak) + `PragueDateTimeResolver` + `TypelessContractlessStandardResolver`.
- `Configure(options)` overwrites only while still at sentinel; idempotent on same reference; throws `InvalidOperationException("conflicting")` on a different reference. **Called inline at DI-registration time** (inside `AddKafkaCaches`), so `PragueMessagePack.Options` is active immediately after `services.AddKafkaCaches(...)` returns. `ResetForTests()` restores the sentinel — call it in both `[SetUp]` and `[TearDown]`.
- `PragueDateTimeFormatter` (sealed) dispatches by `reader.NextMessagePackType`: `Integer → DateTime.FromBinary(ReadInt64())`, `Extension → reader.ReadDateTime()`, else throws. `PragueNullableDateTimeFormatter` is required because MsgPack-CSharp's standard nullable formatters bind the underlying formatter at construction and bypass `options.Resolver`.
- Host composition: `KafkaCachesGlobalOptionsBuilder.WithMessagePackResolver(Func<IFormatterResolver,IFormatterResolver>)` — `Build()` delegates to `CreateDefaultComposite()` so the lambda always sees all three layers.

**Compliance check (must return zero):**
```bash
grep -rnE "MessagePackSerializer\.(Serialize|Deserialize)" --include="*.cs" src/ | grep -v "PragueMessagePack.Options"
```
