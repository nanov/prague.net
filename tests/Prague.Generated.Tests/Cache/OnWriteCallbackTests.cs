namespace Prague.Generated.Tests.Cache;

using NUnit.Framework;
using Prague.Core;
using Prague.Generated.Tests.TestEntities;
using Prague.Kafka;

[DataCache]
public partial class OnWriteEntity {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]
	public string Category { get; set; } = "";

	public string Name { get; set; } = "";

	[DataCacheIgnoreEquality] public int Version { get; set; }

	[DataCacheIgnoreEquality] public string? ModifiedBy { get; set; }
}

/// <summary>
///   The on-write <c>AddOrUpdate</c>: the callback runs once, under the store lock, after the equality check decided
///   to write, and before the value is stored; indexes and the produce step see the mutated document.
/// </summary>
[TestFixture]
public class OnWriteCallbackTests {
	[Test]
	public void Add_RunsOnce_WithNullResident() {
		var cache = new OnWriteEntityCache();
		var calls = 0;
		OnWriteEntity? seen = new();

		var written = cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "a" }, (next, resident) => {
			calls++;
			seen = resident;
			next.Version = 1;
		});

		Assert.That(written, Is.True);
		Assert.That(calls, Is.EqualTo(1));
		Assert.That(seen, Is.Null);
		Assert.That(cache.TryGet(1, out var stored) && stored.Version == 1);
	}

	[Test]
	public void Update_RunsOnce_WithResident() {
		var cache = new OnWriteEntityCache();
		var first = new OnWriteEntity { Id = 1, Name = "a", Version = 1 };
		cache.AddOrUpdate(first);
		var calls = 0;
		OnWriteEntity? seen = null;

		var written = cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "b" }, (next, resident) => {
			calls++;
			seen = resident;
			next.Version = resident!.Version + 1;
		});

		Assert.That(written, Is.True);
		Assert.That(calls, Is.EqualTo(1));
		Assert.That(seen, Is.SameAs(first));
		Assert.That(cache.TryGet(1, out var stored) && stored is { Name: "b", Version: 2 });
	}

	[Test]
	public void Same_DoesNotRun_AndKeepsResident() {
		var cache = new OnWriteEntityCache();
		var first = new OnWriteEntity { Id = 1, Name = "a" };
		cache.AddOrUpdate(first);
		var calls = 0;

		var written = cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "a", Version = 9 }, (_, _) => calls++);

		Assert.That(written, Is.False);
		Assert.That(calls, Is.Zero, "the equality check came first and found nothing to write");
		Assert.That(cache.TryGet(1, out var stored) && ReferenceEquals(stored, first));
	}

	[Test]
	public void State_IsPassedToStaticCallback() {
		var cache = new OnWriteEntityCache();

		cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "a" }, "alice", static (next, _, user) => next.ModifiedBy = user);

		Assert.That(cache.TryGet(1, out var stored) && stored.ModifiedBy == "alice");
	}

	[Test]
	public void OldDocument_IsReturned() {
		var cache = new OnWriteEntityCache();
		var first = new OnWriteEntity { Id = 1, Name = "a" };
		cache.AddOrUpdate(first);

		cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "b" }, 123L, 0, static (_, _, _) => { }, out var old);

		Assert.That(old, Is.SameAs(first));
	}

	[Test]
	public void CoreOverload_WithoutState_RunsOnWrite() {
		var cache = new OnWriteEntityCache();
		var first = new OnWriteEntity { Id = 1, Name = "a" };
		cache.Cache.AddOrUpdate(1, first, 0, static (next, _) => next.Version = 1, out var none);
		var written = cache.Cache.AddOrUpdate(1, new OnWriteEntity { Id = 1, Name = "b" }, 0,
			static (next, resident) => next.Version = resident!.Version + 1, out var old);

		Assert.That(none, Is.Null);
		Assert.That(written, Is.True);
		Assert.That(old, Is.SameAs(first));
		Assert.That(cache.TryGet(1, out var stored) && stored.Version == 2);
	}

	[Test]
	public void Indexes_SeeTheMutatedDocument() {
		var cache = new OnWriteEntityCache();
		cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "a", Category = "old" });

		cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "b", Category = "old" }, static (next, _) => next.Category = "new");

		Assert.That(cache.Query().WithCategory("old").Execute(), Is.Empty);
		Assert.That(cache.Query().WithCategory("new").Execute().Select(e => e.Id), Is.EqualTo(new[] { 1 }));
	}

	[Test]
	public void Throwing_LeavesCacheUnchanged() {
		var cache = new OnWriteEntityCache();
		var first = new OnWriteEntity { Id = 1, Name = "a", Category = "x" };
		cache.AddOrUpdate(first);

		Assert.Throws<InvalidTimeZoneException>(() => cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "b", Category = "y" },
			static (_, _) => throw new InvalidTimeZoneException()));
		Assert.Throws<InvalidTimeZoneException>(() => cache.AddOrUpdate(new OnWriteEntity { Id = 2, Name = "c", Category = "y" },
			static (_, _) => throw new InvalidTimeZoneException()));

		Assert.That(cache.TryGet(1, out var stored) && ReferenceEquals(stored, first));
		Assert.That(cache.TryGet(2, out _), Is.False);
		Assert.That(cache.Query().WithCategory("y").Execute(), Is.Empty);
		Assert.That(cache.Query().WithCategory("x").Execute().Select(e => e.Id), Is.EqualTo(new[] { 1 }));
	}

	[Test]
	public void ChangingTheKey_Throws_AndWritesNothing([Values] bool resident) {
		var cache = new OnWriteEntityCache();
		if (resident)
			cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "a", Category = "x" });

		var ex = Assert.Throws<InvalidOperationException>(() => cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = "b", Category = "y" },
			static (next, _) => next.Id = 2));

		Assert.That(ex!.Message, Does.Contain("changed the document's key"));
		Assert.That(cache.TryGet(2, out _), Is.False);
		Assert.That(cache.Query().WithCategory("y").Execute(), Is.Empty);
		Assert.That(cache.TryGet(1, out var stored), Is.EqualTo(resident));
		if (resident)
			Assert.That(stored!.Name, Is.EqualTo("a"));
	}

	[Test]
	public void ChangingTheKey_Throws_ForGetKeyEntities() {
		var cache = new EntityWithHeadersCache();

		Assert.Throws<InvalidOperationException>(() => cache.AddOrUpdate(new EntityWithHeaders { Id = 1 }, 0, static (next, _, _) => next.Id = 2));
		Assert.That(cache.TryGet(1, out _) || cache.TryGet(2, out _), Is.False);
	}

	[Test]
	public void ConcurrentWriters_LoseNoIncrements() {
		const int threads = 8;
		const int perThread = 2_000;
		var cache = new OnWriteEntityCache();
		var name = 0;

		Parallel.For(0, threads, _ => {
			for (var i = 0; i < perThread; i++)
				cache.AddOrUpdate(new OnWriteEntity { Id = 1, Name = Interlocked.Increment(ref name).ToString() },
					static (next, resident) => next.Version = (resident?.Version ?? 0) + 1);
		});

		Assert.That(cache.TryGet(1, out var stored) && stored.Version == threads * perThread);
	}

	[Test]
	public void AddOrUpdateAndProduce_ProducesOnlyWhenWritten_AfterMutation() {
		var cache = new OnWriteEntityCache();
		var first = new OnWriteEntity { Id = 1, Name = "a" };
		cache.AddOrUpdate(first);
		var calls = 0;

		Assert.DoesNotThrow(() => cache.AddOrUpdateAndProduce(new OnWriteEntity { Id = 1, Name = "a" }, (_, _) => calls++),
			"no change, so the missing producer is never reached");
		Assert.That(calls, Is.Zero);

		var changed = new OnWriteEntity { Id = 1, Name = "b" };
		var ex = Assert.Throws<InvalidOperationException>(() =>
			cache.AddOrUpdateAndProduce(changed, "bob", static (next, _, user) => next.ModifiedBy = user));
		Assert.That(ex!.Message, Does.Contain("Producer not configured"), "the produce step was reached after the write");
		Assert.That(cache.TryGet(1, out var stored) && ReferenceEquals(stored, changed) && stored.ModifiedBy == "bob");
	}
}
