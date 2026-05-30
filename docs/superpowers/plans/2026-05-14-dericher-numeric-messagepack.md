# Codegen Dericher/Enricher numeric MessagePack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Switch the codegen `Dericher` (produce side) and `Enricher` (consume side) to MessagePack for `int`/`int?`/`long`/`long?` header properties so the format is endianness-agnostic, length-tolerant, and self-describing. Preserve backward compatibility on the read path by falling back to the legacy raw `BitConverter` format when MessagePack decoding fails. Flip the existing `HeaderFilters` classes to MessagePack-first read so they match the new common case.

**Why:** Current state writes platform-native-endian raw bytes (`BitConverter.GetBytes`) and reads only when `bytes.Length` exactly matches (4 for `int`, 8 for `long`). That fails for non-.NET producers, fails when schema width changes, and is invisible to MessagePack-using consumers. MessagePack is self-describing, byte-order-canonical, and round-trips between `int` and `long` when the value fits.

**Architecture:** Three production files, two test files. Codegen emits new branches that call `HeadersSerDe.SerializeMessagePack<T>` on write and `HeadersSerDe.TryDeserializeMessagePack<T>` first on read (with `TryDeserializeInt`/`TryDeserializeLong` as fallback). `HeaderFilters` numeric branches reorder MessagePack first, raw fallback. `HeadersSerDe.SerializeInt`/`SerializeLong`/`TryDeserializeInt`/`TryDeserializeLong` are NOT removed — they remain available for tests, manual producers, and the legacy fallback read path.

**Tech stack:** .NET 9, C# `LangVersion=latest`, Roslyn source generator (`Prague.Codegen`), MessagePack-CSharp, NUnit 4, Confluent.Kafka. Codegen output lives at `*.generated.cs` under each consumer project's `obj/` (e.g., `Prague.Kafka.EnrichExtensions.g.cs`).

**House style:** Tabs (width 2), file-scoped namespaces with usings INSIDE the namespace, K&R braces, `_camelCase` private fields. Hot-path rules from `~/.claude/skills/high-performance-net/SKILL.md` apply on the filter `ShouldProcess` methods — no LINQ, minimal try/catch on hot paths. The codegen-emitted code follows the same style (we control the template).

**Reference:** existing patterns — `HeaderFilters.cs:241-258` (`KafkaHeaderPredicateFilter<T>`) shows the multi-strategy try-pattern we're standardising on.

---

## Background — exact format change

**Before (per `int` property `TenantId`):**

```csharp
// Dericher (produce)
headers.Add("TenantId", HeadersSerDe.SerializeInt(entity.TenantId));
// → BitConverter.GetBytes(int) → 4 bytes, native-endian

// Enricher (consume)
if (Ascii.Equals("TenantId", header.Key)) {
    if (HeadersSerDe.TryDeserializeInt(header.GetValueBytes(), out var intValue)) {
        entity.TenantId = intValue;
    }
}
// → only accepts bytes.Length == 4
```

**After:**

```csharp
// Dericher (produce)
headers.Add("TenantId", HeadersSerDe.SerializeMessagePack(entity.TenantId));
// → MessagePack int encoding: 1-5 bytes, format-prefix + value, big-endian

// Enricher (consume)
if (Ascii.Equals("TenantId", header.Key)) {
    if (HeadersSerDe.TryDeserializeMessagePack<int>(header.GetValueBytes(), out var intValue)) {
        entity.TenantId = intValue;
    }
    else if (HeadersSerDe.TryDeserializeInt(header.GetValueBytes(), out var intValueLegacy)) {
        entity.TenantId = intValueLegacy;
    }
}
// → MessagePack first (new common case); raw bytes fallback for legacy data
```

The `else if` reads `header.GetValueBytes()` a second time — that's fine, the call is just `byte[]` accessor on the `IHeader`.

Same shape for `long`/`long?` and the nullable variants (the nullable case wraps in an `if (entity.X.HasValue)` on write).

---

## File Structure

**Created:** none.

**Modified:**

- `src/Prague.Codegen/CacheGenerator.cs`
  - `GenerateConcreteDericher` (~lines 4237-4296): swap the four int/int?/long/long? branches from `SerializeInt`/`SerializeLong` to `SerializeMessagePack`.
  - `GenerateConcreteEnricher` (~lines 4162-4235): swap the int/int? and long/long? branches to emit a MessagePack-first read with a raw-fallback `else if`.

- `src/Prague.Kafka/Filters/HeaderFilters.cs`
  - `KafkaHeaderEqualsFilter<T>.ShouldProcess` (~line 108)
  - `KafkaHeaderNotEqualsFilter<T>.ShouldProcess` (~line 157)
  - `KafkaHeaderEqualsNumericFilter.ShouldProcess` (~line 195)
  - `KafkaHeaderNotEqualsNumericFilter.ShouldProcess` (~line 219)
  - `KafkaHeaderPredicateFilter<T>.ShouldProcess` (~line 241)

  All five: reorder so `TryDeserializeMessagePack` is tried first for int/long, with `TryDeserializeInt`/`TryDeserializeLong` as fallback. The Guid branch and the `passOnNull` logic on `KafkaHeaderPredicateFilter` stay unchanged. Update the existing "// Try int/long/Guid first" comments to reflect the new order.

- `tests/Prague.Kafka.TestAdaptor.Tests/Enrichment/KafkaEnrichExtensionTests.cs`
  - **Keep** the existing tests at lines 170–293 (they verify legacy raw-bytes fallback still works).
  - **Add** `Enrich_WithHeaders_IntProperty_MessagePack_ShouldParseCorrectly`, `Enrich_WithHeaders_LongProperty_MessagePack_ShouldParseCorrectly`, `Enrich_RoundTrip_IntProperty_MessagePack_ShouldRoundtrip`, `Enrich_RoundTrip_LongProperty_MessagePack_ShouldRoundtrip`, `Enrich_RoundTrip_NullableInt_WithValue_ShouldRoundtrip`, `Enrich_RoundTrip_NullableInt_NullValue_ShouldOmitHeader`. The round-trip tests call `EntityWithHeaders.Derich(entity, headers)` then `EntityWithHeaders.GetEnricher().Enrich(entity2, headers, ts)` and assert equality.

- `tests/Prague.Kafka.TestAdaptor.Tests/HeaderFilters/KafkaHeaderFiltersTests.cs`
  - **Keep** existing tests using `HeadersSerDe.SerializeLong`/`SerializeInt` (verify raw-fallback still works).
  - **Add** parallel test cases that build inputs via `MessagePackSerializer.Serialize<int>(...)` / `Serialize<long>(...)` for each of the five filter classes — small additions, not full rewrites.

`HeadersSerDe.SerializeInt`/`SerializeLong`/`TryDeserializeInt`/`TryDeserializeLong` are intentionally NOT deleted — they're still called by the codegen Enricher (as fallback) and by tests, and they're public API.

---

## Task 1: Codegen Dericher — write side uses MessagePack

**Files:**
- Modify: `src/Prague.Codegen/CacheGenerator.cs:4252-4267`
- Modify: `tests/Prague.Kafka.TestAdaptor.Tests/Enrichment/KafkaEnrichExtensionTests.cs` (append round-trip tests)

- [ ] **Step 1: Add a failing round-trip test for `int`**

Append to `tests/Prague.Kafka.TestAdaptor.Tests/Enrichment/KafkaEnrichExtensionTests.cs` (inside the class, before the closing brace):

```csharp
	[Test]
	public void Derich_IntProperty_ShouldWriteMessagePackFormat() {
		var entity = new EntityWithHeaders { Id = 1, TenantId = 12345 };
		var headers = new Headers();

		EntityWithHeaders.Derich(entity, headers);

		var tenantIdHeader = headers.FirstOrDefault(h => h.Key == "TenantId");
		Assert.That(tenantIdHeader, Is.Not.Null);

		// MessagePack int32 for 12345 (positive, > 127, > 255, fits in int16):
		// Format byte 0xcd (uint16) followed by big-endian 2 bytes -> 0x30 0x39
		// Length: 3 bytes
		var bytes = tenantIdHeader!.GetValueBytes();
		Assert.That(bytes, Is.Not.EqualTo(BitConverter.GetBytes(12345)),
			"Should not be raw BitConverter format (post-fix)");

		// Verify the bytes round-trip via MessagePack
		var roundTripped = MessagePack.MessagePackSerializer.Deserialize<int>(bytes);
		Assert.That(roundTripped, Is.EqualTo(12345));
	}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~Derich_IntProperty_ShouldWriteMessagePackFormat`
Expected: fails — the bytes still equal `BitConverter.GetBytes(12345)` (the assertion `Is.Not.EqualTo` catches this).

- [ ] **Step 3: Update the Dericher codegen**

Open `src/Prague.Codegen/CacheGenerator.cs`. Locate `GenerateConcreteDericher` (~line 4237). Replace the four int/long branches with MessagePack:

Find (~lines 4252-4267):

```csharp
							// Generate serialization based on property type
							if (prop.PropertyType == "int") {
								w.Line($"headers.Add(\"{prop.HeaderName}\", Prague.Kafka.SerDe.HeadersSerDe.SerializeInt(entity.{prop.PropertyName}));");
							}
							else if (prop.PropertyType == "int?") {
								w.If($"entity.{prop.PropertyName}.HasValue", (ref CodeWriter w) => {
									w.Line($"headers.Add(\"{prop.HeaderName}\", Prague.Kafka.SerDe.HeadersSerDe.SerializeInt(entity.{prop.PropertyName}.Value));");
								});
							}
							else if (prop.PropertyType == "long") {
								w.Line($"headers.Add(\"{prop.HeaderName}\", Prague.Kafka.SerDe.HeadersSerDe.SerializeLong(entity.{prop.PropertyName}));");
							}
							else if (prop.PropertyType == "long?") {
								w.If($"entity.{prop.PropertyName}.HasValue", (ref CodeWriter w) => {
									w.Line($"headers.Add(\"{prop.HeaderName}\", Prague.Kafka.SerDe.HeadersSerDe.SerializeLong(entity.{prop.PropertyName}.Value));");
								});
							}
```

Replace with:

```csharp
							// Generate serialization based on property type
							if (prop.PropertyType == "int") {
								w.Line($"headers.Add(\"{prop.HeaderName}\", Prague.Kafka.SerDe.HeadersSerDe.SerializeMessagePack(entity.{prop.PropertyName}));");
							}
							else if (prop.PropertyType == "int?") {
								w.If($"entity.{prop.PropertyName}.HasValue", (ref CodeWriter w) => {
									w.Line($"headers.Add(\"{prop.HeaderName}\", Prague.Kafka.SerDe.HeadersSerDe.SerializeMessagePack(entity.{prop.PropertyName}.Value));");
								});
							}
							else if (prop.PropertyType == "long") {
								w.Line($"headers.Add(\"{prop.HeaderName}\", Prague.Kafka.SerDe.HeadersSerDe.SerializeMessagePack(entity.{prop.PropertyName}));");
							}
							else if (prop.PropertyType == "long?") {
								w.If($"entity.{prop.PropertyName}.HasValue", (ref CodeWriter w) => {
									w.Line($"headers.Add(\"{prop.HeaderName}\", Prague.Kafka.SerDe.HeadersSerDe.SerializeMessagePack(entity.{prop.PropertyName}.Value));");
								});
							}
```

- [ ] **Step 4: Rebuild + run the test**

Run: `dotnet build Prague.sln`
Expected: build succeeds. The source generator regenerates `*.g.cs` for downstream projects on next build/test.

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~Derich_IntProperty_ShouldWriteMessagePackFormat`
Expected: passes.

- [ ] **Step 5: Run the full Enricher suite to catch any regressions in unchanged code**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~KafkaEnrichExtensionTests`
Expected: all pass. The legacy-format tests using `BitConverter.GetBytes` should keep passing because the Enricher hasn't been touched yet (still uses `TryDeserializeInt`/`TryDeserializeLong`, which accepts those bytes). Round-trip tests we'll add in Task 2 are still missing.

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Codegen/CacheGenerator.cs tests/Prague.Kafka.TestAdaptor.Tests/Enrichment/KafkaEnrichExtensionTests.cs
git commit -m "feat(codegen): Dericher emits MessagePack for int/long header properties"
```

No `Co-Authored-By` trailer.

---

## Task 2: Codegen Enricher — read side tries MessagePack first, raw fallback

**Files:**
- Modify: `src/Prague.Codegen/CacheGenerator.cs:4188-4197`
- Modify: `tests/Prague.Kafka.TestAdaptor.Tests/Enrichment/KafkaEnrichExtensionTests.cs` (append more tests)

After Task 1 the Dericher writes MessagePack. Existing tests that use `BitConverter.GetBytes(...)` to build header inputs still pass because the Enricher still calls `TryDeserializeInt`/`TryDeserializeLong` first. After Task 2 the Enricher tries MessagePack first; we keep the raw fallback so old data still works.

- [ ] **Step 1: Add failing tests covering MessagePack input + round-trip**

Append these test methods to `KafkaEnrichExtensionTests.cs` (inside the class, before the closing brace):

```csharp
	[Test]
	public void Enrich_WithHeaders_IntProperty_MessagePack_ShouldParseCorrectly() {
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "TenantId", MessagePack.MessagePackSerializer.Serialize(12345) }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		enricher.Enrich(entity, headers, kafkaTimestamp);

		Assert.That(entity.TenantId, Is.EqualTo(12345));
	}

	[Test]
	public void Enrich_WithHeaders_LongProperty_MessagePack_ShouldParseCorrectly() {
		var entity = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		var headers = new Headers {
			{ "Timestamp", MessagePack.MessagePackSerializer.Serialize(9876543210L) }
		};
		var kafkaTimestamp = new Timestamp(1234567890123, TimestampType.CreateTime);

		enricher.Enrich(entity, headers, kafkaTimestamp);

		Assert.That(entity.Timestamp, Is.EqualTo(9876543210L));
	}

	[Test]
	public void Enrich_RoundTrip_IntProperty_ShouldRoundtripViaDericher() {
		var sent = new EntityWithHeaders { Id = 1, TenantId = 42, EventType = "x", CustomValue = "y", Timestamp = 100L };
		var headers = new Headers();
		EntityWithHeaders.Derich(sent, headers);

		var received = new EntityWithHeaders { Id = 1 };
		var enricher = EntityWithHeaders.GetEnricher();
		enricher.Enrich(received, headers, new Timestamp(0, TimestampType.CreateTime));

		Assert.That(received.TenantId, Is.EqualTo(42));
		Assert.That(received.Timestamp, Is.EqualTo(100L));
		Assert.That(received.EventType, Is.EqualTo("x"));
		Assert.That(received.CustomValue, Is.EqualTo("y"));
	}

	[Test]
	public void Enrich_RoundTrip_LargeLong_ShouldRoundtripViaDericher() {
		// Verify MessagePack handles large longs correctly (was broken with native-endian raw bytes on big-endian hardware).
		var sent = new EntityWithHeaders { Id = 1, Timestamp = long.MaxValue };
		var headers = new Headers();
		EntityWithHeaders.Derich(sent, headers);

		var received = new EntityWithHeaders { Id = 1 };
		EntityWithHeaders.GetEnricher().Enrich(received, headers, new Timestamp(0, TimestampType.CreateTime));

		Assert.That(received.Timestamp, Is.EqualTo(long.MaxValue));
	}
```

- [ ] **Step 2: Run the new tests; verify they fail**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter "FullyQualifiedName~Enrich_WithHeaders_IntProperty_MessagePack | FullyQualifiedName~Enrich_RoundTrip"`
Expected: the four new tests fail. The MessagePack-format input tests fail because the Enricher's `TryDeserializeInt` checks `Length == 4` — but MessagePack-encoded 12345 is 3 bytes. The round-trip tests fail because the Dericher already writes MessagePack but the Enricher tries to interpret those bytes as raw BitConverter, which fails the length check, so the property is never set.

- [ ] **Step 3: Update the Enricher codegen**

In `src/Prague.Codegen/CacheGenerator.cs`, locate `GenerateConcreteEnricher` (~line 4162). Find this region (lines ~4188-4197):

```csharp
								// Generate conversion based on property type
								if (prop.PropertyType == "int" || prop.PropertyType == "int?") {
									w.If("Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeInt(header.GetValueBytes(), out var intValue)", (ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = intValue;");
									});
								}
								else if (prop.PropertyType == "long" || prop.PropertyType == "long?") {
									w.If("Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeLong(header.GetValueBytes(), out var longValue)", (ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = longValue;");
									});
								}
```

Replace with:

```csharp
								// Generate conversion based on property type
								if (prop.PropertyType == "int" || prop.PropertyType == "int?") {
									// MessagePack first (canonical post-fix format); raw BitConverter fallback for legacy headers.
									w.Line($"var headerBytes = header.GetValueBytes();");
									w.If($"Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeMessagePack<int>(headerBytes, out var intValue)", (ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = intValue;");
									});
									w.Line($"else if (Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeInt(headerBytes, out var intValueLegacy)) {{");
									w.Indent((ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = intValueLegacy;");
									});
									w.Line($"}}");
								}
								else if (prop.PropertyType == "long" || prop.PropertyType == "long?") {
									w.Line($"var headerBytes = header.GetValueBytes();");
									w.If($"Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeMessagePack<long>(headerBytes, out var longValue)", (ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = longValue;");
									});
									w.Line($"else if (Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeLong(headerBytes, out var longValueLegacy)) {{");
									w.Indent((ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = longValueLegacy;");
									});
									w.Line($"}}");
								}
```

Note on variable naming: the original `var intValue` / `var longValue` were declared inside the `If` block by the `out var` pattern. With the new `headerBytes` local, the `out var intValue` is still scoped inside the `if` body's expression context but C# `out var` declarations live in the enclosing scope. To avoid name collisions between multiple `[DataCacheHeader]` properties of the same type within one `Enrich` method (the foreach loops over headers, but inside that loop the if-else chain visits each property), we must ensure each property's `headerBytes`/`intValue`/`intValueLegacy` declarations are in a block scope that doesn't bleed across siblings.

**Verify the generated code compiles cleanly when an entity has multiple `int` `[DataCacheHeader]` properties** (e.g., `EntityWithHeaders.TenantId` + add a second `int` for testing). If the generator emits the `var headerBytes` declaration once per property branch outside a `{ }` block, two adjacent int branches will produce duplicate `headerBytes` locals.

If duplicate-local issues arise, wrap each branch in `w.OpenBrace()` / `w.CloseBrace()` to scope the locals. (The existing `If`-then-block already provides scoping for `intValue`; `headerBytes` needs explicit scoping.)

**Recommended safer shape** — use a unique suffix per property:

```csharp
								if (prop.PropertyType == "int" || prop.PropertyType == "int?") {
									w.OpenBrace();
									w.Line($"var headerBytes = header.GetValueBytes();");
									w.If($"Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeMessagePack<int>(headerBytes, out var intValue)", (ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = intValue;");
									});
									w.Line($"else if (Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeInt(headerBytes, out var intValueLegacy)) {{");
									w.Indent((ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = intValueLegacy;");
									});
									w.Line($"}}");
									w.CloseBrace();
								}
								else if (prop.PropertyType == "long" || prop.PropertyType == "long?") {
									w.OpenBrace();
									w.Line($"var headerBytes = header.GetValueBytes();");
									w.If($"Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeMessagePack<long>(headerBytes, out var longValue)", (ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = longValue;");
									});
									w.Line($"else if (Prague.Kafka.SerDe.HeadersSerDe.TryDeserializeLong(headerBytes, out var longValueLegacy)) {{");
									w.Indent((ref CodeWriter w) => {
										w.Line($"entity.{prop.PropertyName} = longValueLegacy;");
									});
									w.Line($"}}");
									w.CloseBrace();
								}
```

The outer `w.OpenBrace()` / `w.CloseBrace()` wraps the whole per-property block, scoping `headerBytes`, `intValue`, `intValueLegacy` to that block. Multiple int properties won't collide.

Verify `CodeWriter` exposes `OpenBrace`/`CloseBrace` — search the codebase: `grep -n "OpenBrace\|CloseBrace" src/Prague.Codegen/CodeWriter.cs`. The existing `GenerateConcreteDericher` uses `w.If(...)` and `w.OpenBrace()` (the latter appears in the Enricher's if-else chain at line ~4185). If not present under those names, use the existing brace-emitter (likely `w.OpenBrace`/`w.CloseBrace` since they're used a few lines above).

- [ ] **Step 4: Rebuild + run all enrichment tests**

Run: `dotnet build Prague.sln`
Expected: build succeeds.

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~KafkaEnrichExtensionTests`
Expected: all pass — both the new MessagePack/round-trip tests AND the existing legacy-format tests (`Enrich_WithHeaders_IntProperty_ShouldParseCorrectly` at line 170 etc., which use `BitConverter.GetBytes`).

If the legacy-format tests fail, the fallback branch is broken — check the generated `obj/Debug/net9.0/generated/.../EnrichExtensions.g.cs` to verify both branches emit.

- [ ] **Step 5: Run the Generated.Tests suite**

Run: `dotnet test tests/Prague.Generated.Tests`
Expected: all pass — the codegen output must still compile under that project's entity set.

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Codegen/CacheGenerator.cs tests/Prague.Kafka.TestAdaptor.Tests/Enrichment/KafkaEnrichExtensionTests.cs
git commit -m "feat(codegen): Enricher tries MessagePack first, falls back to raw for int/long"
```

---

## Task 3: HeaderFilters — flip raw-first to MessagePack-first

**Files:**
- Modify: `src/Prague.Kafka/Filters/HeaderFilters.cs` (5 classes)
- Modify: `tests/Prague.Kafka.TestAdaptor.Tests/HeaderFilters/KafkaHeaderFiltersTests.cs` (add MessagePack-input cases)

The filters already handle both formats — current order is raw-first, MessagePack-fallback. We flip to MessagePack-first so the common case (post-fix) is the fast branch. Behavior is identical; this is a perf reordering with documentation value.

- [ ] **Step 1: Add MessagePack-input test cases**

Append these tests to `tests/Prague.Kafka.TestAdaptor.Tests/HeaderFilters/KafkaHeaderFiltersTests.cs` (inside the existing `KafkaHeaderFiltersTests` outer class, after the last nested test fixture). Place each in an appropriately-named inner `[TestFixture]`:

```csharp
	[TestFixture]
	public class MessagePackInputCoverageTests {
		[Test]
		public void KafkaHeaderEqualsFilter_Int_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderEqualsFilter<int>(42);
			var bytes = MessagePack.MessagePackSerializer.Serialize(42);
			Assert.That(filter.ShouldProcess(bytes), Is.True);
		}

		[Test]
		public void KafkaHeaderEqualsFilter_Long_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderEqualsFilter<long>(9876543210L);
			var bytes = MessagePack.MessagePackSerializer.Serialize(9876543210L);
			Assert.That(filter.ShouldProcess(bytes), Is.True);
		}

		[Test]
		public void KafkaHeaderNotEqualsFilter_Int_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderNotEqualsFilter<int>(42);
			var bytes = MessagePack.MessagePackSerializer.Serialize(43);
			Assert.That(filter.ShouldProcess(bytes), Is.True);
		}

		[Test]
		public void KafkaHeaderEqualsNumericFilter_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderEqualsNumericFilter(42);
			Assert.That(filter.ShouldProcess(MessagePack.MessagePackSerializer.Serialize(42)), Is.True);
			Assert.That(filter.ShouldProcess(MessagePack.MessagePackSerializer.Serialize(43)), Is.False);
		}

		[Test]
		public void KafkaHeaderNotEqualsNumericFilter_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderNotEqualsNumericFilter(42L);
			Assert.That(filter.ShouldProcess(MessagePack.MessagePackSerializer.Serialize(43L)), Is.True);
		}

		[Test]
		public void KafkaHeaderPredicateFilter_Int_AcceptsMessagePackInput() {
			var filter = new KafkaHeaderPredicateFilter<int>(x => x > 100);
			Assert.That(filter.ShouldProcess(MessagePack.MessagePackSerializer.Serialize(150)), Is.True);
			Assert.That(filter.ShouldProcess(MessagePack.MessagePackSerializer.Serialize(50)), Is.False);
		}
	}
```

The tests rely on `MessagePack` being usable in the test file — verify there's a `using MessagePack;` near the top, or fully-qualify (the snippet above does). The file already uses `MessagePack` (per the existing `using` at the top of `KafkaHeaderFiltersTests.cs`).

- [ ] **Step 2: Run the new tests**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~MessagePackInputCoverageTests`
Expected: all 6 pass (the existing filter implementations already fall back to MessagePack). This confirms the current behavior before we reorder.

- [ ] **Step 3: Reorder the filter implementations**

Open `src/Prague.Kafka/Filters/HeaderFilters.cs`.

**3a. `KafkaHeaderEqualsFilter<T>.ShouldProcess` (~line 108):**

Find:

```csharp
	public override bool ShouldProcess(byte[] headersBytes) {
		// Try int/long/Guid first, messagePack is used for everything else
		if (typeof(T) == typeof(int) && HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
			return Unsafe.As<int, T>(ref i)!.Equals(_value);
		if (typeof(T) == typeof(long) && HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
			return Unsafe.As<long, T>(ref l)!.Equals(_value);
		if (typeof(T) == typeof(Guid) && HeadersSerDe.TryDeserializeGuid(headersBytes, out var g))
			return Unsafe.As<Guid, T>(ref g)!.Equals(_value);

		var val = MessagePackSerializer.Deserialize<T?>(headersBytes);
		return val?.Equals(_value) ?? true;
	}
```

Replace with:

```csharp
	public override bool ShouldProcess(byte[] headersBytes) {
		// MessagePack first (canonical post-codegen-flip format) for int/long; raw fallback for legacy headers.
		// Guid stays raw-only (codegen still emits raw 16-byte Guid).
		if (typeof(T) == typeof(int)) {
			if (HeadersSerDe.TryDeserializeMessagePack<int>(headersBytes, out var mi))
				return Unsafe.As<int, T>(ref mi)!.Equals(_value);
			if (HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
				return Unsafe.As<int, T>(ref i)!.Equals(_value);
		}
		if (typeof(T) == typeof(long)) {
			if (HeadersSerDe.TryDeserializeMessagePack<long>(headersBytes, out var ml))
				return Unsafe.As<long, T>(ref ml)!.Equals(_value);
			if (HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
				return Unsafe.As<long, T>(ref l)!.Equals(_value);
		}
		if (typeof(T) == typeof(Guid) && HeadersSerDe.TryDeserializeGuid(headersBytes, out var g))
			return Unsafe.As<Guid, T>(ref g)!.Equals(_value);

		var val = MessagePackSerializer.Deserialize<T?>(headersBytes);
		return val?.Equals(_value) ?? true;
	}
```

**3b. `KafkaHeaderNotEqualsFilter<T>.ShouldProcess` (~line 157):** Apply the same pattern — MessagePack-first then raw — for both int and long branches. Negate as the original did (`return !val.Equals(_value)`).

**3c. `KafkaHeaderEqualsNumericFilter.ShouldProcess` (~line 195):**

Find:

```csharp
	public override bool ShouldProcess(byte[] headersBytes) {
		if (HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
			return i == _value;
		if (HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
			return l == _value;
		if (HeadersSerDe.TryDeserializeMessagePack<long>(headersBytes, out var j))
			return j == _value;

		return false;
	}
```

Replace with:

```csharp
	public override bool ShouldProcess(byte[] headersBytes) {
		// MessagePack first (canonical), raw int/long fallback for legacy headers.
		if (HeadersSerDe.TryDeserializeMessagePack<long>(headersBytes, out var j))
			return j == _value;
		if (HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
			return i == _value;
		if (HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
			return l == _value;

		return false;
	}
```

**3d. `KafkaHeaderNotEqualsNumericFilter.ShouldProcess` (~line 219):** Same reorder; negate the comparisons. The default-return on all-three-fail flips from `true` to `true` (no change — when we cannot identify the header value, we still pass the "not equals" check).

**3e. `KafkaHeaderPredicateFilter<T>.ShouldProcess` (~line 241):**

Find:

```csharp
	public override bool ShouldProcess(byte[] headersBytes) {
		// Try int/long/Guid first; fall through to MessagePack for everything else.
		if (typeof(T) == typeof(int) && HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
			return _predicate(Unsafe.As<int, T>(ref i));
		if (typeof(T) == typeof(long) && HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
			return _predicate(Unsafe.As<long, T>(ref l));
		if (typeof(T) == typeof(Guid) && HeadersSerDe.TryDeserializeGuid(headersBytes, out var g))
			return _predicate(Unsafe.As<Guid, T>(ref g));

		// Fast-path for MessagePack nil (0xC0): no deserialize needed.
		if (headersBytes is { Length: 1 } && headersBytes[0] == 0xC0) {
			return _passOnNull;
		}

		if (HeadersSerDe.TryDeserializeMessagePack<T?>(headersBytes, out var j) && j != null)
			return _predicate(j.Value);

		return _passOnNull;
	}
```

Replace with:

```csharp
	public override bool ShouldProcess(byte[] headersBytes) {
		// MessagePack first for int/long (canonical); raw fallback for legacy headers.
		if (typeof(T) == typeof(int)) {
			if (HeadersSerDe.TryDeserializeMessagePack<int>(headersBytes, out var mi))
				return _predicate(Unsafe.As<int, T>(ref mi));
			if (HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
				return _predicate(Unsafe.As<int, T>(ref i));
		}
		if (typeof(T) == typeof(long)) {
			if (HeadersSerDe.TryDeserializeMessagePack<long>(headersBytes, out var ml))
				return _predicate(Unsafe.As<long, T>(ref ml));
			if (HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
				return _predicate(Unsafe.As<long, T>(ref l));
		}
		if (typeof(T) == typeof(Guid) && HeadersSerDe.TryDeserializeGuid(headersBytes, out var g))
			return _predicate(Unsafe.As<Guid, T>(ref g));

		// Fast-path for MessagePack nil (0xC0): no deserialize needed.
		if (headersBytes is { Length: 1 } && headersBytes[0] == 0xC0) {
			return _passOnNull;
		}

		if (HeadersSerDe.TryDeserializeMessagePack<T?>(headersBytes, out var j) && j != null)
			return _predicate(j.Value);

		return _passOnNull;
	}
```

Important: `TryDeserializeMessagePack<int>` will fail on raw 4-byte BitConverter bytes (they aren't valid MessagePack int encodings — valid int encodings are 1, 2, 3, 5, or 9 bytes). So legacy raw bytes still reach the `TryDeserializeInt` fallback correctly. Same for long.

- [ ] **Step 4: Run the existing + new filter tests**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests --filter FullyQualifiedName~KafkaHeaderFiltersTests`
Expected: all pass (including the legacy `HeadersSerDe.SerializeInt/Long` input tests at lines 724, 740, 766-794 — they still work via the raw fallback).

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Kafka/Filters/HeaderFilters.cs tests/Prague.Kafka.TestAdaptor.Tests/HeaderFilters/KafkaHeaderFiltersTests.cs
git commit -m "perf(filters): try MessagePack first on int/long header reads, raw as fallback"
```

---

## Task 4: Whole-solution build + test

- [ ] **Step 1: Solution build**

Run: `dotnet build Prague.sln`
Expected: 0 errors. Warning count should match pre-change baseline (138 warnings) — any new warning indicates a generator issue.

- [ ] **Step 2: Full test slice**

Run: `dotnet test Prague.Tests.slnf`
Expected: every project green. Cores roughly: Core 426, Generated 820, Kafka 40, TestAdaptor.Tests ~192 + new tests, IntegrationTests 3.

- [ ] **Step 3: No commit if step 1 & 2 produced no changes; otherwise commit fixups**

---

## Self-review (plan author)

**Spec coverage:** All three answered design questions are translated to tasks:
- Q1 (multi-format read) → Task 2 emits MessagePack-first + raw fallback in the Enricher.
- Q2 (scope = int/long only) → Tasks 1 and 2 touch only the int/int?/long/long? branches; Guid and other types stay as-is.
- Q3 (filters MessagePack-first) → Task 3 reorders all five filter classes.

**Placeholder scan:** No TBD/TODO/"implement later". Every code block is exact text. Note: Task 2 Step 3 includes a "Recommended safer shape" with brace-scoping — that IS the implementation, not a suggestion to figure out later.

**Type consistency:** Method/field names align across tasks (`SerializeMessagePack`, `TryDeserializeMessagePack<T>`, `TryDeserializeInt`, `TryDeserializeLong`). `headerBytes` local is consistently named in the Enricher codegen. Test class `MessagePackInputCoverageTests` is referenced once.

**Risk note:** Task 2 Step 3 includes a guard against C# local-variable collisions when an entity has multiple int/long `[DataCacheHeader]` properties. The brace-scoping pattern is mandatory there. If the implementer skips it, the generated code may fail to compile on entities with multiple int headers. Surface this risk in the implementer's prompt.
