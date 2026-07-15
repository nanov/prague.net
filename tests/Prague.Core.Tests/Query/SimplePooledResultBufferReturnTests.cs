namespace Prague.Core.Tests.Query;

using System;
using System.Buffers;
using Prague.Core;
using NUnit.Framework;

// ── Domain ──────────────────────────────────────────────────────────────────
// Verifies that ExecutePooled() on a plain (non-join) query RETURNS the rented
// result buffer to ArrayPool<TValue>.Shared even when the query yields no rows:
//   • a Where predicate that drops every candidate (Init rents BEFORE filtering)
//   • skip beyond the total count (BuildResults empty short-circuit)
//
// Detection: prime the pool's thread-local slot with a probe array of the same
// bucket, run the query (it rents the probe), then rent again — getting the
// probe back proves the query returned what it rented. Single-threaded, so the
// TLS-slot round-trip is deterministic.

internal sealed class SqItem : ICacheEquatable<SqItem>, ICacheClonable<SqItem> {
	public int Id { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(SqItem? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public SqItem Clone() => new() { Id = Id, Name = Name };
}

[TestFixture]
public class SimplePooledResultBufferReturnTests {
	private const int ItemCount = 10; // ≤ 16 so every Rent in play lands in the same (minimum) pool bucket

	private InMemoryDataCache<int, SqItem> _cache = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, SqItem>();
		for (var i = 1; i <= ItemCount; i++)
			_cache.AddOrUpdate(i, new SqItem { Id = i, Name = $"n{i}" });
	}

	[Test]
	public void ExecutePooled_WhereMatchesNothing_ReturnsRentedBuffer() =>
		AssertQueryReturnsValueBuffer(() => {
			using var r = _cache.Query().Where(_ => false).ExecutePooled();
			Assert.That(r.Count, Is.EqualTo(0));
		});

	[Test]
	public void ExecutePooled_SkipBeyondTotalCount_ReturnsRentedBuffer() =>
		AssertQueryReturnsValueBuffer(() => {
			using var r = _cache.Query().ExecutePooled(skip: ItemCount + 5);
			Assert.That(r.Count, Is.EqualTo(0));
		});

	private static void AssertQueryReturnsValueBuffer(Action runQuery) {
		var pool = ArrayPool<SqItem>.Shared;
		var probe = pool.Rent(ItemCount);
		pool.Return(probe, clearArray: true);

		runQuery();

		var after = pool.Rent(ItemCount);
		pool.Return(after, clearArray: true);
		Assert.That(ReferenceEquals(after, probe), Is.True,
			"the pooled result buffer rented by the query was not returned to the pool");
	}
}
