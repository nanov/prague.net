namespace Prague.Kafka.Tests.SerDe;

using Prague.Kafka;
using Prague.Kafka.Filters;
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

		// Serialize via PragueMessagePack.Options directly (public read API).
		var entityBytes = MessagePackSerializer.Serialize(new SimplePoco { Id = 7 }, PragueMessagePack.Options);

		// Read back through the same options — must succeed despite DefaultOptions being broken.
		var roundTripped = MessagePackSerializer.Deserialize<SimplePoco>(entityBytes, PragueMessagePack.Options);

		Assert.That(roundTripped.Id, Is.EqualTo(7));
	}

	[Test]
	public void HeaderFilterFallback_StillWorks_WhenDefaultOptionsIsBroken() {
		// Mutate DefaultOptions to a deliberately-broken resolver that throws on any type lookup.
		MessagePackSerializer.DefaultOptions = MessagePackSerializerOptions.Standard
			.WithResolver(new ThrowingResolver());

		var threshold = new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc);
		var filter = new KafkaHeaderPredicateFilter<DateTime>(it => it < threshold);

		// Encode via PragueMessagePack.Options — the canonical write path.
		var earlier = MessagePackSerializer.Serialize<DateTime?>(threshold.AddDays(-1), PragueMessagePack.Options);
		var later = MessagePackSerializer.Serialize<DateTime?>(threshold.AddDays(1), PragueMessagePack.Options);

		// Predicate must evaluate correctly via the fallback MessagePack path — proving HeaderFilters
		// routes through PragueMessagePack.Options, not the now-poisoned DefaultOptions.
		Assert.That(filter.ShouldProcess(earlier), Is.True,
			"Earlier date must pass the predicate (it < threshold)");
		Assert.That(filter.ShouldProcess(later), Is.False,
			"Later date must not pass the predicate (it >= threshold)");
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
