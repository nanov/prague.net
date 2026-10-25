namespace Prague.Kafka.Tests.SerDe;

using System.Runtime.CompilerServices;
using System.Text;
using Prague.Kafka;
using Prague.Kafka.SerDe;
using MessagePack;
using NUnit.Framework;

[TestFixture]
public class StringInterningTests {
	[Test]
	public void Deserialize_RepeatedStrings_ShareSameInstance() {
		var content = new string(new[] { 'p', 'r', 'a', 'g', 'u', 'e', '-', 'r', 'e', 'p', 'e', 'a', 't' });
		var bytes1 = MessagePackSerializer.Serialize(content, PragueMessagePack.Options);
		var bytes2 = MessagePackSerializer.Serialize(content, PragueMessagePack.Options);

		var a = MessagePackSerializer.Deserialize<string>(bytes1, PragueMessagePack.Options);
		var b = MessagePackSerializer.Deserialize<string>(bytes2, PragueMessagePack.Options);

		Assert.That(ReferenceEquals(a, b), Is.True,
			"Repeated wire strings must dedupe to the same CLR instance via PragueStringInterningFormatter.");
	}

	[Test]
	public void Deserialize_ObjectMembers_ShareSameInstance() {
		var bytes = MessagePackSerializer.Serialize(new Named { Name = "member-repeat" }, PragueMessagePack.Options);

		var a = MessagePackSerializer.Deserialize<Named>(bytes, PragueMessagePack.Options);
		var b = MessagePackSerializer.Deserialize<Named>(bytes, PragueMessagePack.Options);

		Assert.That(ReferenceEquals(a.Name, b.Name), Is.True,
			"Generated formatters must resolve string members through Prague's interning formatter.");
	}

	[Test]
	public void Deserialize_LongString_IsNotInterned() {
		var bytes = MessagePackSerializer.Serialize(new string('x', 5000), PragueMessagePack.Options);

		var a = MessagePackSerializer.Deserialize<string>(bytes, PragueMessagePack.Options);
		var b = MessagePackSerializer.Deserialize<string>(bytes, PragueMessagePack.Options);

		Assert.That(a, Is.EqualTo(b));
		Assert.That(ReferenceEquals(a, b), Is.False);
	}

	[Test]
	public void Deserialize_NullString_ReturnsNull() {
		var bytes = MessagePackSerializer.Serialize<string?>(null, PragueMessagePack.Options);
		var back = MessagePackSerializer.Deserialize<string?>(bytes, PragueMessagePack.Options);
		Assert.That(back, Is.Null);
	}

	[Test]
	public void Deserialize_EmptyString_ReturnsEmpty() {
		var bytes = MessagePackSerializer.Serialize(string.Empty, PragueMessagePack.Options);
		Assert.That(MessagePackSerializer.Deserialize<string>(bytes, PragueMessagePack.Options), Is.SameAs(string.Empty));
	}

	[Test]
	public void Serialize_RoundTripsContent() {
		var s = "hello-prague-string-ünïcødé";
		var bytes = MessagePackSerializer.Serialize(s, PragueMessagePack.Options);
		var back = MessagePackSerializer.Deserialize<string>(bytes, PragueMessagePack.Options);
		Assert.That(back, Is.EqualTo(s));
	}

	[Test]
	public void Pool_UnrootedString_IsReleasedAndReinterned() {
		using var pool = new Utf8WeakInternPool();
		var utf8 = Encoding.UTF8.GetBytes("weakly-held");
		var weak = InternAndForget(pool, utf8);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.That(weak.TryGetTarget(out _), Is.False, "The pool must not keep an interned string alive.");
		Assert.That(pool.GetOrAdd(utf8), Is.EqualTo("weakly-held"));
	}

	[Test]
	public void Pool_DeadEntries_AreSwept() {
		using var pool = new Utf8WeakInternPool();
		AddUnrooted(pool, 0, 2000);
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		// The threshold doubles after each sweep, so grow past it again to trigger the next one.
		AddUnrooted(pool, 2000, 4200);

		Assert.That(pool.Count, Is.LessThan(3000), "Growing past the threshold must sweep the collected first batch.");
	}

	[Test]
	public void Pool_ConcurrentReaders_ConvergeOnOneInstance() {
		using var pool = new Utf8WeakInternPool();
		var utf8 = Encoding.UTF8.GetBytes("contended");
		var results = new string[64];

		Parallel.For(0, results.Length, i => results[i] = pool.GetOrAdd(utf8));

		// Misses re-check under the lock, so racing threads converge on one published instance.
		var winner = pool.GetOrAdd(utf8);
		Assert.That(results, Has.All.SameAs(winner));
	}

	[Test]
	public void Pool_NonAsciiContent_MatchesWithoutFalsePositives() {
		using var pool = new Utf8WeakInternPool();
		var a = pool.GetOrAdd(Encoding.UTF8.GetBytes("ünïcødé"));

		Assert.That(pool.GetOrAdd(Encoding.UTF8.GetBytes("ünïcødé")), Is.SameAs(a));
		Assert.That(pool.GetOrAdd(Encoding.UTF8.GetBytes("ünïcødë")), Is.EqualTo("ünïcødë"));
		Assert.That(pool.GetOrAdd(Encoding.UTF8.GetBytes("unicode")), Is.EqualTo("unicode"));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference<string> InternAndForget(Utf8WeakInternPool pool, byte[] utf8) => new(pool.GetOrAdd(utf8));

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddUnrooted(Utf8WeakInternPool pool, int from, int to) {
		for (var i = from; i < to; i++)
			pool.GetOrAdd(Encoding.UTF8.GetBytes("unrooted-" + i));
	}

	[MessagePackObject]
	public sealed class Named {
		[Key(0)] public string? Name { get; set; }
	}
}
