# MessagePack Isolation + DateTime Dual-Read Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Prague's MessagePack serialization immune to host-side `MessagePackSerializer.DefaultOptions` mutations while remaining wire-format byte-identical to today's production (which writes `NativeDateTime`/`NativeGuid`/`NativeDecimal`/typeless via a host config), and add dual-format `DateTime` read tolerance so existing topics encoded under either format stay readable.

**Architecture:** A single internal static `PragueMessagePack.Options` carries the serializer options used by every internal SerDe call site. Default composite is `PragueDateTimeResolver` (dual-format DateTime reader, writes legacy int64) + `TypelessContractlessStandardResolver` (matches current production wire format byte-for-byte). Hosts may extend the resolver chain via a new `WithMessagePackResolver(defaultResolver => …)` callback on a new `KafkaCachesGlobalOptionsBuilder`, surfaced as an optional `options` parameter on every `AddKafkaCaches` overload. `PragueMessagePack.Configure(...)` is idempotent on same reference and throws on conflicting re-config.

**Tech Stack:** .NET 9, C# `LangVersion=latest`, MessagePack-CSharp 3.1.4, NUnit 4 (Kafka tests), xUnit (other tests), Testcontainers (Kafka integration), Confluent.Kafka.

**Spec:** `docs/superpowers/specs/2026-05-17-messagepack-isolation-design.md`

**Branch:** `feature/messagepack-isolation` (already created, spec already committed in `d4fa83e` + `92e5e43`)

---

## File Structure

**New source files** (`src/Prague.Kafka/`):
- `SerDe/PragueMessagePack.cs` — `public static class PragueMessagePack` with `Options` get/private-set, `Configure`, `DefaultOptions`, and `internal static void ResetForTests()`. Single responsibility: own the options singleton + its lifecycle.
- `SerDe/PragueDateTimeFormatter.cs` — `IMessagePackFormatter<DateTime>` with native int64 write, dual-format (int / ext) read.
- `SerDe/PragueDateTimeResolver.cs` — `IFormatterResolver` returning the formatter only for `DateTime`; falls through for everything else.
- `Options/KafkaCachesGlobalOptionsBuilder.cs` — public builder with `WithMessagePackResolver(Func<IFormatterResolver, IFormatterResolver>)` and internal `Build()`.

**Modified source files:**
- `src/Prague.Kafka/SerDe/HeadersSerDe.cs` — thread `PragueMessagePack.Options` into 3 calls.
- `src/Prague.Kafka/Filters/HeaderFilters.cs` — thread `PragueMessagePack.Options` into 5 fallback `Deserialize<T>` calls.
- `src/Prague.Kafka/SerDe/RentedBytesDeserializer.cs` — thread `PragueMessagePack.Options` into 3 `CacheSerde<T>` calls.
- `src/Prague.Kafka/DependencyInjection.cs` — add `options` parameter to every `AddKafkaCaches` overload; invoke `PragueMessagePack.Configure(builder.Build())` in the root overload exactly once.
- `src/Prague.Codegen/CacheGenerator.cs:4414` — emit `Prague.Kafka.PragueMessagePack.Options` as second arg.
- `src/Prague.Kafka.TestAdaptor/DependencyInjection.cs:63` — thread `PragueMessagePack.Options` into dump-replay `Deserialize`.

**New test files:**
- `tests/Prague.Kafka.Tests/SerDe/PragueDateTimeFormatterTests.cs`
- `tests/Prague.Kafka.Tests/SerDe/PragueMessagePackConfigureTests.cs`
- `tests/Prague.Kafka.Tests/SerDe/IsolationFromDefaultOptionsTests.cs`
- `tests/Prague.Kafka.Tests/DependencyInjection/WithMessagePackResolverTests.cs`
- `tests/Prague.Kafka.IntegrationTests/Entities/EntityWithDateTime.cs`
- `tests/Prague.Kafka.IntegrationTests/MessagePackIsolationTests.cs`

**Test framework note:** `Prague.Kafka.Tests` uses **NUnit** (`[Test]`, `[TestCase]`, `Assert.That`) per CLAUDE.md. Do NOT use xUnit there.

---

## Task 1: PragueDateTimeFormatter (TDD - failing test first)

**Files:**
- Create: `src/Prague.Kafka/SerDe/PragueDateTimeFormatter.cs`
- Create: `tests/Prague.Kafka.Tests/SerDe/PragueDateTimeFormatterTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Prague.Kafka.Tests/SerDe/PragueDateTimeFormatterTests.cs`:

```csharp
namespace Prague.Kafka.Tests.SerDe;

using System;
using Prague.Kafka.SerDe;
using MessagePack;
using MessagePack.Resolvers;
using NUnit.Framework;

[TestFixture]
public class PragueDateTimeFormatterTests {
	private static MessagePackSerializerOptions Options =>
		MessagePackSerializerOptions.Standard.WithResolver(
			CompositeResolver.Create(PragueDateTimeResolver.Instance, StandardResolver.Instance));

	[Test]
	public void Serialize_ProducesLegacyInt64Encoding() {
		var value = new DateTime(2026, 5, 17, 12, 34, 56, DateTimeKind.Utc);
		var bytes = MessagePackSerializer.Serialize(value, Options);

		// MessagePack int64 prefix is 0xd3 followed by 8 big-endian bytes — 9 bytes total.
		Assert.That(bytes.Length, Is.EqualTo(9));
		Assert.That(bytes[0], Is.EqualTo(0xd3));
	}

	[TestCase(DateTimeKind.Utc)]
	[TestCase(DateTimeKind.Local)]
	[TestCase(DateTimeKind.Unspecified)]
	public void Serialize_Deserialize_RoundTripsTicksAndKind(DateTimeKind kind) {
		var value = DateTime.SpecifyKind(new DateTime(2026, 5, 17, 12, 34, 56, 789), kind);

		var bytes = MessagePackSerializer.Serialize(value, Options);
		var back = MessagePackSerializer.Deserialize<DateTime>(bytes, Options);

		Assert.That(back.Ticks, Is.EqualTo(value.Ticks));
		Assert.That(back.Kind, Is.EqualTo(kind));
	}

	[Test]
	public void Deserialize_FromStandardTimestampExt_ProducesEquivalentInstant() {
		// MessagePack timestamp ext writes seconds-since-epoch UTC.
		var instantUtc = new DateTime(2026, 5, 17, 12, 34, 56, DateTimeKind.Utc);

		// Encode via MessagePack's built-in DateTime writer (writes standard ext timestamp).
		// We use NativeDateTimeResolver-less options so DateTime goes through the default ext writer.
		var standardExtOptions = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);
		var extBytes = MessagePackSerializer.Serialize(instantUtc, standardExtOptions);

		// Sanity: first byte should be one of the ext type prefixes (0xd6, 0xd7, 0xc7).
		Assert.That(extBytes[0], Is.EqualTo(0xd6).Or.EqualTo(0xd7).Or.EqualTo(0xc7));

		// Now read via our dual-format formatter.
		var back = MessagePackSerializer.Deserialize<DateTime>(extBytes, Options);

		// Standard timestamp ext is UTC by spec; we accept that Kind comes back as Utc/Unspecified.
		Assert.That(back.ToUniversalTime(), Is.EqualTo(instantUtc));
	}

	[Test]
	public void Deserialize_UnexpectedToken_Throws() {
		// Encode a string where a DateTime is expected.
		var bytes = MessagePackSerializer.Serialize("not a datetime", Options);

		Assert.Throws<MessagePackSerializationException>(() =>
			MessagePackSerializer.Deserialize<DateTime>(bytes, Options));
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~PragueDateTimeFormatterTests"`

Expected: **build failure** — `PragueDateTimeFormatter` and `PragueDateTimeResolver` don't exist yet.

- [ ] **Step 3: Create `PragueDateTimeFormatter`**

Create `src/Prague.Kafka/SerDe/PragueDateTimeFormatter.cs`:

```csharp
namespace Prague.Kafka.SerDe;

using MessagePack;
using MessagePack.Formatters;

/// <summary>
///   Prague's DateTime formatter: writes the legacy native int64 encoding
///   (DateTime.ToBinary) for byte-for-byte compatibility with topics produced
///   under the historical host-side NativeDateTimeResolver configuration.
///   Reads both that legacy int64 encoding AND the standard MessagePack
///   timestamp ext format, dispatched by inspecting the next token.
/// </summary>
public sealed class PragueDateTimeFormatter : IMessagePackFormatter<DateTime> {
	public static readonly PragueDateTimeFormatter Instance = new();

	private PragueDateTimeFormatter() { }

	public void Serialize(ref MessagePackWriter writer, DateTime value, MessagePackSerializerOptions options)
		=> writer.Write(value.ToBinary());

	public DateTime Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
		switch (reader.NextMessagePackType) {
			case MessagePackType.Integer:
				return DateTime.FromBinary(reader.ReadInt64());
			case MessagePackType.Extension:
				return reader.ReadDateTime();
			default:
				throw new MessagePackSerializationException(
					$"Unexpected MessagePack token while reading DateTime: {reader.NextMessagePackType}");
		}
	}
}
```

- [ ] **Step 4: Create `PragueDateTimeResolver`**

Create `src/Prague.Kafka/SerDe/PragueDateTimeResolver.cs`:

```csharp
namespace Prague.Kafka.SerDe;

using MessagePack;
using MessagePack.Formatters;

/// <summary>
///   Single-type resolver that returns <see cref="PragueDateTimeFormatter"/>
///   for <see cref="DateTime"/> and falls through (returns null) for everything
///   else so it can be chained ahead of the standard resolver in a composite.
/// </summary>
public sealed class PragueDateTimeResolver : IFormatterResolver {
	public static readonly PragueDateTimeResolver Instance = new();

	private PragueDateTimeResolver() { }

	public IMessagePackFormatter<T>? GetFormatter<T>() {
		if (typeof(T) == typeof(DateTime))
			return (IMessagePackFormatter<T>)(object)PragueDateTimeFormatter.Instance;
		return null;
	}
}
```

- [ ] **Step 5: Run tests — verify all pass**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~PragueDateTimeFormatterTests"`

Expected: 6 tests pass (1 + 3 from TestCase + 1 + 1).

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Kafka/SerDe/PragueDateTimeFormatter.cs \
        src/Prague.Kafka/SerDe/PragueDateTimeResolver.cs \
        tests/Prague.Kafka.Tests/SerDe/PragueDateTimeFormatterTests.cs
git commit -m "feat(serde): add PragueDateTimeFormatter with dual-format read

Writes legacy native int64 (DateTime.ToBinary) for wire compat with
existing production topics. Reads both legacy int64 and standard
MessagePack timestamp ext, dispatched by NextMessagePackType.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: PragueMessagePack static + Configure

**Files:**
- Create: `src/Prague.Kafka/SerDe/PragueMessagePack.cs`
- Create: `tests/Prague.Kafka.Tests/SerDe/PragueMessagePackConfigureTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Prague.Kafka.Tests/SerDe/PragueMessagePackConfigureTests.cs`:

```csharp
namespace Prague.Kafka.Tests.SerDe;

using System;
using Prague.Kafka.SerDe;
using MessagePack;
using MessagePack.Resolvers;
using NUnit.Framework;

[TestFixture]
public class PragueMessagePackConfigureTests {
	[SetUp]
	public void ResetBeforeTest() => PragueMessagePack.ResetForTests();

	[TearDown]
	public void ResetAfterTest() => PragueMessagePack.ResetForTests();

	[Test]
	public void DefaultOptions_UsesPragueDateTimeResolverAndTypelessContractless() {
		var opts = PragueMessagePack.Options;

		Assert.That(opts, Is.Not.Null);
		Assert.That(opts.Resolver, Is.Not.Null);
		// Sanity: DateTime via default options must round-trip through the native int64 path.
		var dt = new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc);
		var bytes = MessagePackSerializer.Serialize(dt, opts);
		Assert.That(bytes[0], Is.EqualTo(0xd3), "DateTime must be int64-encoded (0xd3 prefix)");
	}

	[Test]
	public void Configure_FirstCall_SetsOptions() {
		var custom = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);
		PragueMessagePack.Configure(custom);

		Assert.That(PragueMessagePack.Options, Is.SameAs(custom));
	}

	[Test]
	public void Configure_SameReferenceTwice_IsNoOp() {
		var custom = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);
		PragueMessagePack.Configure(custom);

		Assert.DoesNotThrow(() => PragueMessagePack.Configure(custom));
		Assert.That(PragueMessagePack.Options, Is.SameAs(custom));
	}

	[Test]
	public void Configure_ConflictingSecondCall_Throws() {
		var a = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);
		var b = MessagePackSerializerOptions.Standard.WithResolver(TypelessContractlessStandardResolver.Instance);
		PragueMessagePack.Configure(a);

		var ex = Assert.Throws<InvalidOperationException>(() => PragueMessagePack.Configure(b));
		Assert.That(ex!.Message, Does.Contain("conflicting"));
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~PragueMessagePackConfigureTests"`

Expected: build failure — `PragueMessagePack` doesn't exist.

- [ ] **Step 3: Create `PragueMessagePack`**

Create `src/Prague.Kafka/SerDe/PragueMessagePack.cs`:

```csharp
namespace Prague.Kafka;

using MessagePack;
using MessagePack.Resolvers;
using SerDe;

/// <summary>
///   Carries the MessagePack serializer options used by every internal
///   Prague SerDe call site. Designed to be set once at startup (via
///   AddKafkaCaches) and read on every hot path. Independent of
///   <see cref="MessagePackSerializer.DefaultOptions"/> so host mutations
///   of that static do not affect Prague's wire format.
/// </summary>
public static class PragueMessagePack {
	private static MessagePackSerializerOptions _options = DefaultOptions();

	public static MessagePackSerializerOptions Options => _options;

	/// <summary>
	///   Idempotent on same reference; throws if called twice with different
	///   options (host bug: two AddKafkaCaches invocations configured differently).
	/// </summary>
	internal static void Configure(MessagePackSerializerOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (ReferenceEquals(_options, options))
			return;
		// _options is allowed to be the default singleton — overwrite once with the configured value.
		if (!ReferenceEquals(_options, _defaultSentinel)) {
			throw new InvalidOperationException(
				"PragueMessagePack.Configure called twice with conflicting options. Configure once at startup.");
		}
		_options = options;
	}

	internal static MessagePackSerializerOptions DefaultOptions() {
		// Cache a sentinel so Configure can detect "still at default" vs "explicitly configured".
		return _defaultSentinel;
	}

	private static readonly MessagePackSerializerOptions _defaultSentinel =
		MessagePackSerializerOptions.Standard.WithResolver(
			CompositeResolver.Create(
				PragueDateTimeResolver.Instance,
				TypelessContractlessStandardResolver.Instance));

	/// <summary>Test-only: restore the default sentinel so each test runs from a clean baseline.</summary>
	internal static void ResetForTests() => _options = _defaultSentinel;
}
```

**Note on the sentinel pattern:** the static initializer order matters — `_defaultSentinel` must be initialized before `_options` reads from `DefaultOptions()`. Move `_defaultSentinel` above `_options` to be safe:

```csharp
public static class PragueMessagePack {
	private static readonly MessagePackSerializerOptions _defaultSentinel =
		MessagePackSerializerOptions.Standard.WithResolver(
			CompositeResolver.Create(
				PragueDateTimeResolver.Instance,
				TypelessContractlessStandardResolver.Instance));

	private static MessagePackSerializerOptions _options = _defaultSentinel;

	public static MessagePackSerializerOptions Options => _options;

	internal static void Configure(MessagePackSerializerOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (ReferenceEquals(_options, options))
			return;
		if (!ReferenceEquals(_options, _defaultSentinel)) {
			throw new InvalidOperationException(
				"PragueMessagePack.Configure called twice with conflicting options. Configure once at startup.");
		}
		_options = options;
	}

	internal static MessagePackSerializerOptions DefaultOptions() => _defaultSentinel;

	internal static void ResetForTests() => _options = _defaultSentinel;
}
```

Use this second version. Delete the first if you wrote it.

- [ ] **Step 4: Add `InternalsVisibleTo` for the Kafka.Tests project**

Verify `src/Prague.Kafka/Prague.Kafka.csproj` already exposes internals to `Prague.Kafka.Tests`. Check with:

```bash
grep "InternalsVisibleTo" src/Prague.Kafka/Prague.Kafka.csproj src/Prague.Kafka/AssemblyInfo.cs 2>/dev/null
```

If `Prague.Kafka.Tests` is not in the InternalsVisibleTo list, add it. If it already is, skip this step.

- [ ] **Step 5: Run tests — verify all pass**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~PragueMessagePackConfigureTests"`

Expected: 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Kafka/SerDe/PragueMessagePack.cs \
        tests/Prague.Kafka.Tests/SerDe/PragueMessagePackConfigureTests.cs
git commit -m "feat(serde): add PragueMessagePack options singleton

PragueMessagePack.Options is the Prague-owned MessagePackSerializerOptions,
independent of MessagePackSerializer.DefaultOptions. Default composite is
PragueDateTimeResolver + TypelessContractlessStandardResolver — matches
the wire format produced by hosts that mutated DefaultOptions in the past.
Configure is idempotent on same reference; throws on conflicting re-config.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: KafkaCachesGlobalOptionsBuilder + WithMessagePackResolver

**Files:**
- Create: `src/Prague.Kafka/Options/KafkaCachesGlobalOptionsBuilder.cs`
- Create: `tests/Prague.Kafka.Tests/DependencyInjection/WithMessagePackResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Prague.Kafka.Tests/DependencyInjection/WithMessagePackResolverTests.cs`:

```csharp
namespace Prague.Kafka.Tests.DependencyInjection;

using System;
using Prague.Kafka;
using Prague.Kafka.Options;
using Prague.Kafka.SerDe;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using NUnit.Framework;

[TestFixture]
public class WithMessagePackResolverTests {
	[SetUp]
	public void Reset() => PragueMessagePack.ResetForTests();

	[TearDown]
	public void ResetAfter() => PragueMessagePack.ResetForTests();

	[Test]
	public void Build_NoCallback_ReturnsDefaultOptions() {
		var builder = new KafkaCachesGlobalOptionsBuilder();
		var opts = builder.Build();
		Assert.That(opts, Is.SameAs(PragueMessagePack.DefaultOptions()));
	}

	[Test]
	public void WithMessagePackResolver_ReceivesPragueComposite() {
		IFormatterResolver? captured = null;
		var builder = new KafkaCachesGlobalOptionsBuilder();
		builder.WithMessagePackResolver(defaultResolver => {
			captured = defaultResolver;
			return defaultResolver;
		});
		_ = builder.Build();

		Assert.That(captured, Is.Not.Null);
		// Composite resolver wraps PragueDateTimeResolver first → resolver answers DateTime via our formatter.
		var dtFormatter = captured!.GetFormatter<DateTime>();
		Assert.That(dtFormatter, Is.SameAs(PragueDateTimeFormatter.Instance));
		// Falls through for everything else — Guid resolves via TypelessContractlessStandardResolver path.
		var guidFormatter = captured.GetFormatter<Guid>();
		Assert.That(guidFormatter, Is.Not.Null, "Guid formatter must resolve via Typeless composite fallback");
	}

	[Test]
	public void WithMessagePackResolver_ReturnValueBecomesActiveResolver() {
		var probe = new ProbeResolver();
		var builder = new KafkaCachesGlobalOptionsBuilder();
		builder.WithMessagePackResolver(_ => probe);
		var opts = builder.Build();

		Assert.That(opts.Resolver, Is.SameAs(probe).Or.Property("CompositeResolver").Or.Matches<IFormatterResolver>(r => RefersTo(r, probe)));
	}

	private static bool RefersTo(IFormatterResolver r, IFormatterResolver target)
		=> ReferenceEquals(r, target);

	/// <summary>Test resolver that records GetFormatter calls.</summary>
	private sealed class ProbeResolver : IFormatterResolver {
		public IMessagePackFormatter<T>? GetFormatter<T>() => null;
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~WithMessagePackResolverTests"`

Expected: build failure — `KafkaCachesGlobalOptionsBuilder` doesn't exist.

- [ ] **Step 3: Create `KafkaCachesGlobalOptionsBuilder`**

Create `src/Prague.Kafka/Options/KafkaCachesGlobalOptionsBuilder.cs`:

```csharp
namespace Prague.Kafka.Options;

using MessagePack;
using MessagePack.Resolvers;
using SerDe;

/// <summary>
///   Builder for library-wide options. Today carries one option
///   (<see cref="WithMessagePackResolver"/>); future global knobs land here
///   without growing the AddKafkaCaches signature.
/// </summary>
public sealed class KafkaCachesGlobalOptionsBuilder {
	private System.Func<IFormatterResolver, IFormatterResolver>? _resolverCompose;

	/// <summary>
	///   Compose a custom resolver on top of Prague's default composite
	///   (<see cref="PragueDateTimeResolver"/> + <see cref="TypelessContractlessStandardResolver"/>).
	///   The lambda receives the Prague composite as <c>defaultResolver</c> and
	///   returns the final resolver to use.
	/// </summary>
	public KafkaCachesGlobalOptionsBuilder WithMessagePackResolver(
		System.Func<IFormatterResolver, IFormatterResolver> compose) {
		ArgumentNullException.ThrowIfNull(compose);
		_resolverCompose = compose;
		return this;
	}

	internal MessagePackSerializerOptions Build() {
		if (_resolverCompose is null)
			return PragueMessagePack.DefaultOptions();
		var composed = _resolverCompose(CompositeResolver.Create(
			PragueDateTimeResolver.Instance,
			TypelessContractlessStandardResolver.Instance));
		return MessagePackSerializerOptions.Standard.WithResolver(composed);
	}
}
```

- [ ] **Step 4: Run tests — verify all pass**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~WithMessagePackResolverTests"`

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Kafka/Options/KafkaCachesGlobalOptionsBuilder.cs \
        tests/Prague.Kafka.Tests/DependencyInjection/WithMessagePackResolverTests.cs
git commit -m "feat(options): add KafkaCachesGlobalOptionsBuilder

Public builder exposing WithMessagePackResolver(defaultResolver => ...).
The defaultResolver passed in is Prague's composite (PragueDateTimeResolver
+ TypelessContractlessStandardResolver), so user composition keeps
DateTime back-compat + native Guid/decimal/typeless behavior for free.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Thread PragueMessagePack.Options through HeadersSerDe

**Files:**
- Modify: `src/Prague.Kafka/SerDe/HeadersSerDe.cs`

This task has no new test code — `PragueDateTimeFormatterTests` (Task 1) already exercises the full Options-aware path. We're just changing the call sites so they pass `PragueMessagePack.Options` explicitly.

- [ ] **Step 1: Modify `HeadersSerDe.SerializeMessagePack<T>`**

Open `src/Prague.Kafka/SerDe/HeadersSerDe.cs`. Replace the body of `SerializeMessagePack<T>`:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static byte[] SerializeMessagePack<T>(T value) {
	return MessagePackSerializer.Serialize(value, PragueMessagePack.Options);
}
```

- [ ] **Step 2: Modify `HeadersSerDe.TryDeserializeMessagePack<T>`**

Replace the body:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool TryDeserializeMessagePack<T>(byte[] bytes, out T? value) {
	try {
		value = MessagePackSerializer.Deserialize<T>(bytes, PragueMessagePack.Options);
		return true;
	} catch {
		value = default;
		return false;
	}
}
```

- [ ] **Step 3: Modify `HeadersSerDe.TryDeserializeMessagePackExact<T>`**

Replace the body:

```csharp
public static bool TryDeserializeMessagePackExact<T>(byte[] bytes, out T? value) {
	try {
		value = MessagePackSerializer.Deserialize<T>(bytes.AsMemory(), PragueMessagePack.Options, out var bytesRead);
		if (bytesRead == bytes.Length) {
			return true;
		}
		value = default;
		return false;
	} catch {
		value = default;
		return false;
	}
}
```

- [ ] **Step 4: Add the using for `PragueMessagePack`**

The `HeadersSerDe` namespace is `Prague.Kafka.SerDe`. `PragueMessagePack` is in `Prague.Kafka` — already in scope via the parent namespace, no using needed. Verify the file compiles.

- [ ] **Step 5: Build the Kafka project**

Run: `dotnet build src/Prague.Kafka/Prague.Kafka.csproj`

Expected: build succeeds.

- [ ] **Step 6: Run full Kafka tests project**

Run: `dotnet test tests/Prague.Kafka.Tests`

Expected: all pre-existing tests still pass, plus the new PragueDateTimeFormatter and PragueMessagePackConfigure and WithMessagePackResolver tests.

- [ ] **Step 7: Commit**

```bash
git add src/Prague.Kafka/SerDe/HeadersSerDe.cs
git commit -m "refactor(serde): thread PragueMessagePack.Options into HeadersSerDe

SerializeMessagePack / TryDeserializeMessagePack / TryDeserializeMessagePackExact
now pass PragueMessagePack.Options explicitly, decoupling header SerDe
from MessagePackSerializer.DefaultOptions.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Thread PragueMessagePack.Options through HeaderFilters

**Files:**
- Modify: `src/Prague.Kafka/Filters/HeaderFilters.cs`

Five filter classes contain fallback `MessagePackSerializer.Deserialize<T>(headersBytes)` calls. All five must take `PragueMessagePack.Options` as the second arg.

- [ ] **Step 1: Modify `KafkaHeaderEqualsFilter<T>` (around line 128)**

In `KafkaHeaderEqualsFilter<T>.ShouldProcess`, replace:

```csharp
var val = MessagePackSerializer.Deserialize<T?>(headersBytes);
```

with:

```csharp
var val = MessagePackSerializer.Deserialize<T?>(headersBytes, PragueMessagePack.Options);
```

- [ ] **Step 2: Modify `KafkaHeaderNotEqualsFilter<T>` (around line 186)**

In `KafkaHeaderNotEqualsFilter<T>.ShouldProcess`, replace:

```csharp
var val = MessagePackSerializer.Deserialize<T>(headersBytes);
```

with:

```csharp
var val = MessagePackSerializer.Deserialize<T>(headersBytes, PragueMessagePack.Options);
```

- [ ] **Step 3: Modify the remaining 3 sites in HeaderFilters.cs**

Grep for any remaining `MessagePackSerializer.Deserialize` calls in this file that pass no options arg:

```bash
grep -n "MessagePackSerializer.Deserialize" src/Prague.Kafka/Filters/HeaderFilters.cs
```

For each result that does NOT already pass `PragueMessagePack.Options`, append `, PragueMessagePack.Options` as the second argument. There are 5 total in the file (the two above plus three more — typically in `KafkaHeaderEqualsNumericFilter`, `KafkaHeaderNotEqualsNumericFilter`, and `KafkaHeaderPredicateFilter<T>`).

Re-run the grep — every match should now end with `PragueMessagePack.Options)`.

- [ ] **Step 4: Add `using Prague.Kafka;` if needed**

The `PragueMessagePack` class is in the parent namespace `Prague.Kafka`. The file `Filters/HeaderFilters.cs` is in `Prague.Kafka.Filters` — parent is in scope, no using needed.

- [ ] **Step 5: Build the Kafka project**

Run: `dotnet build src/Prague.Kafka/Prague.Kafka.csproj`

Expected: build succeeds.

- [ ] **Step 6: Run header filter tests**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~HeaderFilter"`

Expected: all existing header filter tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Prague.Kafka/Filters/HeaderFilters.cs
git commit -m "refactor(filters): thread PragueMessagePack.Options into HeaderFilters

All 5 fallback Deserialize<T> calls in HeaderFilters.cs now pass
PragueMessagePack.Options explicitly.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Thread PragueMessagePack.Options through CacheSerde<T>

**Files:**
- Modify: `src/Prague.Kafka/SerDe/RentedBytesDeserializer.cs`

`CacheSerde<T>` is the entity-value SerDe path — hottest of all the call sites.

- [ ] **Step 1: Modify `CacheSerde<T>` methods**

In `src/Prague.Kafka/SerDe/RentedBytesDeserializer.cs`, replace the `CacheSerde<T>` class body:

```csharp
public static class CacheSerde<T> {
	public static T Deserialize(RentedBytes bytes)
		=> MessagePackSerializer.Deserialize<T>(bytes.AsMemory(), PragueMessagePack.Options);

	internal static T Deserialize(RentedBytesWithHandler bytes)
		=> MessagePackSerializer.Deserialize<T>(bytes.AsMemory(), PragueMessagePack.Options);

	public static byte[] Serialize(T value)
		=> MessagePackSerializer.Serialize<T>(value, PragueMessagePack.Options);
}
```

- [ ] **Step 2: Build the Kafka project**

Run: `dotnet build src/Prague.Kafka/Prague.Kafka.csproj`

Expected: build succeeds.

- [ ] **Step 3: Run all Kafka.Tests**

Run: `dotnet test tests/Prague.Kafka.Tests`

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Prague.Kafka/SerDe/RentedBytesDeserializer.cs
git commit -m "refactor(serde): thread PragueMessagePack.Options into CacheSerde<T>

Entity value Serialize/Deserialize now pass PragueMessagePack.Options
explicitly — the entity value path is the hottest MessagePack call site
in the library.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Wire `options` parameter through AddKafkaCaches overloads

**Files:**
- Modify: `src/Prague.Kafka/DependencyInjection.cs`

`AddKafkaCaches` has 8+ overloads that eventually funnel into one root method. We add an `options` callback parameter to every overload, threaded down to the root, where it's invoked exactly once.

- [ ] **Step 1: Add `options` parameter to the root overload**

Open `src/Prague.Kafka/DependencyInjection.cs`. Find the root `AddKafkaCaches(this IServiceCollection services, string configsSectionName, Action<KafkaCachesOptions, IServiceProvider>? configsFactory, Action<KafkaCacheHandlersBuilder> configure)` method (around line 392).

Change the signature to add an `Action<KafkaCachesGlobalOptionsBuilder>? options = null` parameter at the end:

```csharp
public static IServiceCollection AddKafkaCaches(this IServiceCollection services, string configsSectionName,
	Action<KafkaCachesOptions, IServiceProvider>? configsFactory, Action<KafkaCacheHandlersBuilder> configure,
	Action<KafkaCachesGlobalOptionsBuilder>? options = null) {
	// existing body
}
```

Inside the body, **before** any handler/consumer service registration (i.e., right after the early `services.AddOptions<KafkaCachesOptions>(...)` block), add:

```csharp
if (options is not null) {
	var optsBuilder = new KafkaCachesGlobalOptionsBuilder();
	options(optsBuilder);
	PragueMessagePack.Configure(optsBuilder.Build());
}
```

Place this block AFTER `services.AddOptions<KafkaCachesOptions>(...)` (which already exists at the top of the method) and BEFORE `services.Configure<KafkaCachesGlobalOptions>(...)`.

- [ ] **Step 2: Add usings if needed**

At the top of `DependencyInjection.cs`, ensure the file has access to `KafkaCachesGlobalOptionsBuilder` (in `Prague.Kafka.Options`) and `PragueMessagePack` (in `Prague.Kafka`). Check the existing `using` block; if `Options;` is not there, add `using Options;`. The file is in `Prague.Kafka` so `PragueMessagePack` is already in scope.

- [ ] **Step 3: Add `options` parameter to all other overloads**

There are multiple convenience overloads above the root method. For each one, add the same `Action<KafkaCachesGlobalOptionsBuilder>? options = null` parameter at the end and pass it through to the next overload / the root.

Specifically the overloads at lines ~370, ~376, ~381, ~387 (per the existing file shape). Pattern for each:

Before:
```csharp
public static IServiceCollection AddKafkaCaches(this IServiceCollection services,
	Action<KafkaCacheHandlersBuilder> configure) {
	return AddKafkaCaches(services, DefaultConfigsSectionName, (Action<KafkaCachesOptions, IServiceProvider>?)null,
		configure);
}
```

After:
```csharp
public static IServiceCollection AddKafkaCaches(this IServiceCollection services,
	Action<KafkaCacheHandlersBuilder> configure,
	Action<KafkaCachesGlobalOptionsBuilder>? options = null) {
	return AddKafkaCaches(services, DefaultConfigsSectionName, (Action<KafkaCachesOptions, IServiceProvider>?)null,
		configure, options);
}
```

Repeat for every overload. Use grep to find all of them:

```bash
grep -n "public static IServiceCollection AddKafkaCaches" src/Prague.Kafka/DependencyInjection.cs
```

- [ ] **Step 4: Build the Kafka project**

Run: `dotnet build src/Prague.Kafka/Prague.Kafka.csproj`

Expected: build succeeds.

- [ ] **Step 5: Build the full solution**

Run: `dotnet build Prague.sln`

Expected: build succeeds (all downstream tests/examples/showcase still compile).

- [ ] **Step 6: Run all Kafka.Tests**

Run: `dotnet test tests/Prague.Kafka.Tests`

Expected: all pre-existing tests pass; new tests from Tasks 1–3 still pass.

- [ ] **Step 7: Commit**

```bash
git add src/Prague.Kafka/DependencyInjection.cs
git commit -m "feat(di): add optional options callback to AddKafkaCaches overloads

Every AddKafkaCaches overload gains an optional
Action<KafkaCachesGlobalOptionsBuilder>? options parameter. The root
overload instantiates the builder, runs the action, and configures
PragueMessagePack once before any handler/consumer is registered.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Thread PragueMessagePack.Options through codegen dump emit

**Files:**
- Modify: `src/Prague.Codegen/CacheGenerator.cs:4414`

- [ ] **Step 1: Modify the codegen emit line**

In `src/Prague.Codegen/CacheGenerator.cs`, find line 4414:

```csharp
w.Line("var bytes = MessagePack.MessagePackSerializer.Serialize(results);");
```

Replace with:

```csharp
w.Line("var bytes = MessagePack.MessagePackSerializer.Serialize(results, Prague.Kafka.PragueMessagePack.Options);");
```

- [ ] **Step 2: Build the codegen project**

Run: `dotnet build src/Prague.Codegen/Prague.Codegen.csproj`

Expected: build succeeds.

- [ ] **Step 3: Build the full solution to regenerate codegen output**

Run: `dotnet build Prague.sln`

Expected: build succeeds. Source generator runs against consumer projects; emitted dump-writer code now passes `PragueMessagePack.Options`.

- [ ] **Step 4: Run generated tests**

Run: `dotnet test tests/Prague.Generated.Tests`

Expected: tests pass (the generated dump-writer's options arg compiles and links).

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Codegen/CacheGenerator.cs
git commit -m "feat(codegen): emit PragueMessagePack.Options on dump writer

The generator-emitted dump (.pkd) writer now passes
Prague.Kafka.PragueMessagePack.Options to MessagePackSerializer.Serialize.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Thread PragueMessagePack.Options through TestAdaptor dump reader

**Files:**
- Modify: `src/Prague.Kafka.TestAdaptor/DependencyInjection.cs:63`

- [ ] **Step 1: Modify the dump-reader line**

In `src/Prague.Kafka.TestAdaptor/DependencyInjection.cs`, find line 63:

```csharp
var records = MessagePack.MessagePackSerializer.Deserialize<List<Dump.PragueConsumeResult>>(bytes);
```

Replace with:

```csharp
var records = MessagePack.MessagePackSerializer.Deserialize<List<Dump.PragueConsumeResult>>(bytes, Prague.Kafka.PragueMessagePack.Options);
```

(Fully-qualified to avoid adding a using statement that may break the file's existing namespace conventions.)

- [ ] **Step 2: Build the test-adaptor project**

Run: `dotnet build src/Prague.Kafka.TestAdaptor/Prague.Kafka.TestAdaptor.csproj`

Expected: build succeeds.

- [ ] **Step 3: Run test-adaptor tests**

Run: `dotnet test tests/Prague.Kafka.TestAdaptor.Tests`

Expected: all pre-existing tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Prague.Kafka.TestAdaptor/DependencyInjection.cs
git commit -m "refactor(testadaptor): use PragueMessagePack.Options for dump replay

Dump (.pkd) reader now uses Prague-owned options for consistency with
the codegen-emitted dump writer.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: IsolationFromDefaultOptionsTests — proves the structural guarantee

**Files:**
- Create: `tests/Prague.Kafka.Tests/SerDe/IsolationFromDefaultOptionsTests.cs`

- [ ] **Step 1: Write the test**

Create `tests/Prague.Kafka.Tests/SerDe/IsolationFromDefaultOptionsTests.cs`:

```csharp
namespace Prague.Kafka.Tests.SerDe;

using System;
using Prague.Kafka;
using Prague.Kafka.SerDe;
using MessagePack;
using MessagePack.Formatters;
using NUnit.Framework;

[TestFixture]
public class IsolationFromDefaultOptionsTests {
	private MessagePackSerializerOptions _originalDefaultOptions = null!;

	[SetUp]
	public void CaptureOriginal() {
		_originalDefaultOptions = MessagePackSerializer.DefaultOptions;
		PragueMessagePack.ResetForTests();
	}

	[TearDown]
	public void Restore() {
		MessagePackSerializer.DefaultOptions = _originalDefaultOptions;
		PragueMessagePack.ResetForTests();
	}

	[Test]
	public void HeadersSerDe_StillWorks_WhenDefaultOptionsIsBroken() {
		// Mutate DefaultOptions to a deliberately-broken resolver that throws on any type lookup.
		MessagePackSerializer.DefaultOptions = MessagePackSerializerOptions.Standard
			.WithResolver(new ThrowingResolver());

		// HeadersSerDe must NOT consult DefaultOptions — it routes through PragueMessagePack.Options.
		var bytes = HeadersSerDe.SerializeMessagePack(42);
		Assert.That(HeadersSerDe.TryDeserializeMessagePackExact<int>(bytes, out var v), Is.True);
		Assert.That(v, Is.EqualTo(42));
	}

	[Test]
	public void CacheSerde_StillWorks_WhenDefaultOptionsIsBroken() {
		MessagePackSerializer.DefaultOptions = MessagePackSerializerOptions.Standard
			.WithResolver(new ThrowingResolver());

		// Construct entity bytes via PragueMessagePack.Options (the public Options getter is intentional).
		var entityBytes = MessagePackSerializer.Serialize(new SimplePoco { Id = 7 }, PragueMessagePack.Options);

		// And read them back via the public surface that internally uses CacheSerde — go direct here.
		var roundTripped = MessagePackSerializer.Deserialize<SimplePoco>(entityBytes, PragueMessagePack.Options);

		Assert.That(roundTripped.Id, Is.EqualTo(7));
	}

	[MessagePackObject]
	public sealed class SimplePoco {
		[Key(0)] public int Id { get; set; }
	}

	private sealed class ThrowingResolver : IFormatterResolver {
		public IMessagePackFormatter<T>? GetFormatter<T>() =>
			throw new InvalidOperationException("DefaultOptions was consulted — isolation broken.");
	}
}
```

- [ ] **Step 2: Run the test — verify all pass**

Run: `dotnet test tests/Prague.Kafka.Tests --filter "FullyQualifiedName~IsolationFromDefaultOptionsTests"`

Expected: 2 tests pass.

- [ ] **Step 3: Commit**

```bash
git add tests/Prague.Kafka.Tests/SerDe/IsolationFromDefaultOptionsTests.cs
git commit -m "test(serde): prove Prague is isolated from DefaultOptions mutations

Mutates MessagePackSerializer.DefaultOptions to a throwing resolver and
asserts both HeadersSerDe and CacheSerde paths still work — the
structural guarantee of the isolation refactor.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: Integration tests — wire compat for DateTime + isolation E2E

**Files:**
- Create: `tests/Prague.Kafka.IntegrationTests/Entities/EntityWithDateTime.cs`
- Create: `tests/Prague.Kafka.IntegrationTests/MessagePackIsolationTests.cs`

These exercise the round-trip via real Kafka (Testcontainers). Skip if Docker isn't available locally — these run in CI.

- [ ] **Step 1: Create the entity**

Create `tests/Prague.Kafka.IntegrationTests/Entities/EntityWithDateTime.cs`:

```csharp
namespace Prague.Kafka.IntegrationTests.Entities;

using Core;
using MessagePack;

[DataCache]
[DataCacheTopic("integration-tests-datetime")]
[MessagePackObject]
public partial class EntityWithDateTime : IDataCacheItem<int, EntityWithDateTime> {
	[Key(0)] [DataCacheKey] public int Id { get; set; }
	[Key(1)] public string Name { get; set; } = "";
	[Key(2)] public DateTime CreatedAt { get; set; }
	[Key(3)] public DateTime? UpdatedAt { get; set; }
	[Key(4)] public Guid CorrelationId { get; set; }

	public int GetCacheKey() => Id;
}
```

- [ ] **Step 2: Create the integration tests**

Create `tests/Prague.Kafka.IntegrationTests/MessagePackIsolationTests.cs`:

```csharp
namespace Prague.Kafka.IntegrationTests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prague.Kafka;
using Prague.Kafka.SerDe;
using Confluent.Kafka;
using Entities;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

[TestFixture]
public class MessagePackIsolationTests {
	private const string TopicPrefix = "integration-tests-mp-isolation";
	private string _topic = "";

	[SetUp]
	public async Task Setup() {
		_topic = $"{TopicPrefix}-{Guid.NewGuid():N}".Substring(0, 50);
		await DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _topic);
		PragueMessagePack.ResetForTests();
	}

	[TearDown]
	public void ResetAfter() => PragueMessagePack.ResetForTests();

	[Test]
	public async Task EntityWithDateTime_RoundTrips_NativeIntFormat() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		var sent = new EntityWithDateTime {
			Id = 1,
			Name = "native",
			CreatedAt = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc),
			UpdatedAt = new DateTime(2026, 5, 17, 12, 30, 0, DateTimeKind.Utc),
			CorrelationId = Guid.NewGuid()
		};

		var bytes = MessagePackSerializer.Serialize(sent, PragueMessagePack.Options);

		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id, PragueMessagePack.Options),
			Value = bytes
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, hosted) = BuildServiceProvider(useCustomResolver: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<EntityWithDateTimeCache>();
		Assert.That(cache.Cache.TryGet(1, out var received), Is.True);
		Assert.That(received!.CreatedAt, Is.EqualTo(sent.CreatedAt));
		Assert.That(received.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
		Assert.That(received.UpdatedAt, Is.EqualTo(sent.UpdatedAt));
		Assert.That(received.CorrelationId, Is.EqualTo(sent.CorrelationId));

		await hosted.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task HostMutatedDefaultOptions_DoesNotBreakProduction() {
		// Simulate the host snippet that historically broke things.
		var original = MessagePackSerializer.DefaultOptions;
		MessagePackSerializer.DefaultOptions = MessagePackSerializerOptions.Standard
			.WithResolver(CompositeResolver.Create(
				TypelessContractlessStandardResolver.Instance,
				StandardResolver.Instance));

		try {
			using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
			var sent = new EntityWithDateTime {
				Id = 2,
				Name = "mutated-default",
				CreatedAt = DateTime.UtcNow,
				CorrelationId = Guid.NewGuid()
			};

			producer.Produce(_topic, new Message<byte[], byte[]> {
				Key = MessagePackSerializer.Serialize(sent.Id, PragueMessagePack.Options),
				Value = MessagePackSerializer.Serialize(sent, PragueMessagePack.Options)
			});
			producer.Flush(TimeSpan.FromSeconds(10));

			var (sp, hosted) = BuildServiceProvider(useCustomResolver: false);
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			await hosted.StartAsync(cts.Token);
			var loader = sp.GetRequiredService<KafkaCachesLoader>();
			await loader.StartAsync(cts.Token);

			var cache = sp.GetRequiredService<EntityWithDateTimeCache>();
			Assert.That(cache.Cache.TryGet(2, out var received), Is.True,
				"Cache must work even when DefaultOptions has been mutated by host");
			Assert.That(received!.CreatedAt, Is.EqualTo(sent.CreatedAt).Within(TimeSpan.FromMilliseconds(1)));

			await hosted.StopAsync(CancellationToken.None);
		} finally {
			MessagePackSerializer.DefaultOptions = original;
		}
	}

	[Test]
	public async Task LegacyTopicData_StandardTimestampExt_StillDecodes() {
		using var producer = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);

		var instantUtc = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);

		// Hand-encode the entity with the standard-ext DateTime by using
		// an options instance that does NOT include PragueDateTimeResolver — so
		// MessagePack uses its built-in ext-timestamp writer for DateTime.
		var standardExtOptions = MessagePackSerializerOptions.Standard
			.WithResolver(TypelessContractlessStandardResolver.Instance);
		// Note: TypelessContractlessStandardResolver also includes NativeDateTimeResolver, so this
		// emits int64 too. To force ext, use plain StandardResolver:
		var pureStandard = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);

		var sent = new EntityWithDateTime {
			Id = 3,
			Name = "legacy-ext",
			CreatedAt = instantUtc,
			CorrelationId = Guid.NewGuid()
		};
		var bytesWithExtDate = MessagePackSerializer.Serialize(sent, pureStandard);

		producer.Produce(_topic, new Message<byte[], byte[]> {
			Key = MessagePackSerializer.Serialize(sent.Id, PragueMessagePack.Options),
			Value = bytesWithExtDate
		});
		producer.Flush(TimeSpan.FromSeconds(10));

		var (sp, hosted) = BuildServiceProvider(useCustomResolver: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await hosted.StartAsync(cts.Token);
		var loader = sp.GetRequiredService<KafkaCachesLoader>();
		await loader.StartAsync(cts.Token);

		var cache = sp.GetRequiredService<EntityWithDateTimeCache>();
		Assert.That(cache.Cache.TryGet(3, out var received), Is.True);
		Assert.That(received!.CreatedAt.ToUniversalTime(), Is.EqualTo(instantUtc));

		await hosted.StopAsync(CancellationToken.None);
	}

	private (IServiceProvider sp, IHostedService hosted) BuildServiceProvider(bool useCustomResolver) {
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KafkaConfig:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA }
			}).Build();

		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddKafkaCaches("KafkaConfig", b => {
			b.AddCache<EntityWithDateTimeCache, int, EntityWithDateTime>(_topic);
		});

		var sp = services.BuildServiceProvider();
		var hosted = sp.GetRequiredService<IHostedService>();
		return (sp, hosted);
	}
}
```

- [ ] **Step 3: Run integration tests**

If Docker is available locally:

Run: `dotnet test tests/Prague.Kafka.IntegrationTests --filter "FullyQualifiedName~MessagePackIsolationTests"`

Expected: 3 tests pass.

If Docker is not available, skip and let CI exercise these.

- [ ] **Step 4: Commit**

```bash
git add tests/Prague.Kafka.IntegrationTests/Entities/EntityWithDateTime.cs \
        tests/Prague.Kafka.IntegrationTests/MessagePackIsolationTests.cs
git commit -m "test(integration): wire-compat and isolation E2E for MessagePack

Three integration tests via real Kafka (Testcontainers):
1. EntityWithDateTime round-trips with native int64 encoding.
2. Cache works even when host has mutated MessagePackSerializer.DefaultOptions.
3. Legacy standard-ext-encoded DateTime values still decode via dual-read.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 12: Final verification — run everything

- [ ] **Step 1: Build the publishable solution filter**

Run: `dotnet build Prague.Publish.slnf`

Expected: builds clean.

- [ ] **Step 2: Run all tests**

Run: `dotnet test Prague.Tests.slnf`

Expected: all pre-existing tests pass; new tests from Tasks 1, 2, 3, 10 pass; Task 11 integration tests pass if Docker is available.

- [ ] **Step 3: Sanity grep**

Run:

```bash
grep -rn "MessagePackSerializer\.Serialize\b\|MessagePackSerializer\.Deserialize\b" --include="*.cs" src/ | grep -v "PragueMessagePack.Options" | grep -v "// "
```

Expected: zero results. Every src/ call site now passes `PragueMessagePack.Options` (or is in a comment).

The test projects intentionally use both `MessagePackSerializer.Serialize(..., PragueMessagePack.Options)` AND raw `MessagePackSerializer.Serialize(...)` (e.g., for legacy-format probing in `LegacyTopicData_StandardTimestampExt_StillDecodes`) — so don't extend the grep to tests/.

---

## Spec coverage check

- **Public API — `PragueMessagePack` static + `Configure`** → Task 2.
- **Public API — `WithMessagePackResolver` on `KafkaCachesGlobalOptionsBuilder`** → Task 3.
- **Public API — `options` parameter on `AddKafkaCaches`** → Task 7.
- **`PragueDateTimeResolver` + `PragueDateTimeFormatter`** → Task 1.
- **Default composite = `PragueDateTimeResolver` + `TypelessContractlessStandardResolver`** → Tasks 2 (in `_defaultSentinel`), 3 (in `Build()`).
- **5 src call sites rewired** → Tasks 4 (HeadersSerDe), 5 (HeaderFilters), 6 (CacheSerde), 8 (codegen), 9 (TestAdaptor dump).
- **Configure idempotent on same ref / throws on conflict** → Task 2 (tests + implementation).
- **`ResetForTests()` internal helper** → Task 2 (implementation), used by Tasks 2/3/10/11.
- **Dual-format DateTime read** → Task 1 (tests + implementation).
- **Wire format unchanged for default path** → Task 11 (integration tests assert native int64 prefix on the wire).
- **Isolation from `DefaultOptions` mutations** → Task 10 (unit) + Task 11 (integration `HostMutatedDefaultOptions_DoesNotBreakProduction`).
- **No breaking changes to `SerializeInt`/`SerializeLong`/`TryDeserializeInt`/`TryDeserializeLong`** → none of Tasks 4–9 touch those methods.
- **Caveats documented (AOT, gadget surface)** — these are spec-level, not code-level. No task needed.

---

## Type/method/symbol consistency

- `PragueMessagePack.Options` — defined Task 2; referenced in Tasks 4, 5, 6, 7, 8, 9, 10, 11. Consistent.
- `PragueMessagePack.Configure(MessagePackSerializerOptions)` — defined Task 2; called in Task 7.
- `PragueMessagePack.DefaultOptions()` — defined Task 2; referenced in Task 3 (`Build()` returns it when no compose callback).
- `PragueMessagePack.ResetForTests()` — defined Task 2; called in Tasks 2, 3, 10, 11.
- `PragueDateTimeFormatter.Instance` — defined Task 1; referenced in Task 3 test.
- `PragueDateTimeResolver.Instance` — defined Task 1; referenced in Tasks 2, 3.
- `KafkaCachesGlobalOptionsBuilder.WithMessagePackResolver(Func<IFormatterResolver, IFormatterResolver>)` — defined Task 3; called in Task 7 (DI wiring) and Task 11 (integration tests via `AddKafkaCaches`).
- `KafkaCachesGlobalOptionsBuilder.Build()` — defined Task 3; called in Task 7.
- `EntityWithDateTime` / `EntityWithDateTimeCache` — defined Task 11. `EntityWithDateTimeCache` is the codegen-emitted type from the `[DataCache]` attribute (existing pattern).

All consistent.
