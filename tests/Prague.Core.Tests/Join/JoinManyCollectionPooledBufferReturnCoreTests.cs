namespace Prague.Core.Tests.Join;

using System;
using System.Linq;
using Prague.Core;
using NUnit.Framework;

// Verifies that ExecutePooled() on the M:N collection-join paths RENTS the per-left child buffer from
// the pool and RETURNS it on Dispose() (so repeated calls reuse it instead of re-allocating ~fan-out
// refs per query), for both directions and outer/inner:
//   • JoinManyCollection / InnerJoinManyCollection                 (reverse: element → owners)
//   • JoinManyCollectionForward / InnerJoinManyCollectionForward   (forward: owner → referenced)
// The ReusesChildBuffer tests drive a SINGLE left with a large fan-out so the (pooled) child buffer —
// not the per-left outer result array — dominates. Reuses MnTag / MnTaggedBook from
// JoinManyCollectionIndexCoreTests (same namespace).

[TestFixture]
public class JoinManyCollectionPooledBufferReturnCoreTests {
	private const int BigCount = 2000;  // big enough that the child buffer dominates allocation
	private const int Iterations = 100;

	private InMemoryDataCache<int, MnTag> _tags = null!;
	private InMemoryDataCache<int, MnTaggedBook> _books = null!;
	private CacheCollectionSymmetricKeyValueListIndex<int, MnTaggedBook, int> _index = null!;

	[SetUp]
	public void SetUp() {
		_tags = new InMemoryDataCache<int, MnTag>();
		_books = new InMemoryDataCache<int, MnTaggedBook>();
		_index = _books.CacheCollectionSymmetricKeyValueListIndex<int>((_, b) => b.TagIds);

		// Tags 1..BigCount.
		for (var i = 1; i <= BigCount; i++)
			_tags.AddOrUpdate(i, new MnTag { Id = i, Name = "t" + i });

		// Hub book 1 carries ALL tags → forward fan-out of BigCount tags for one (driving) book.
		_books.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Hub", TagIds = Enumerable.Range(1, BigCount).ToList() });

		// Leaf books all carry tag 1 → reverse fan-out of ~BigCount books for one (driving) tag.
		for (var i = 0; i < BigCount; i++)
			_books.AddOrUpdate(1001 + i, new MnTaggedBook { Id = 1001 + i, Title = "L" + i, TagIds = new List<int> { 1 } });
	}

	// ── Allocation regression: pooled path must reuse the child buffer (single big-fan-out left) ──

	[Test]
	public void JoinManyCollection_Reverse_Outer_Pooled_ReusesChildBuffer() =>
		AssertPooledReusesBuffer(
			() => _tags.Query().Where(t => t.Id == 1).JoinManyCollection(_books, _index).ExecutePooled(),
			() => _tags.Query().Where(t => t.Id == 1).JoinManyCollection(_books, _index).Execute());

	[Test]
	public void InnerJoinManyCollection_Reverse_Inner_Pooled_ReusesChildBuffer() =>
		AssertPooledReusesBuffer(
			() => _tags.Query().Where(t => t.Id == 1).InnerJoinManyCollection(_books, _index).ExecutePooled(),
			() => _tags.Query().Where(t => t.Id == 1).InnerJoinManyCollection(_books, _index).Execute());

	[Test]
	public void JoinManyCollectionForward_Outer_Pooled_ReusesChildBuffer() =>
		AssertPooledReusesBuffer(
			() => _books.Query().Where(b => b.Id == 1).JoinManyCollectionForward(_tags, _index).ExecutePooled(),
			() => _books.Query().Where(b => b.Id == 1).JoinManyCollectionForward(_tags, _index).Execute());

	[Test]
	public void InnerJoinManyCollectionForward_Inner_Pooled_ReusesChildBuffer() =>
		AssertPooledReusesBuffer(
			() => _books.Query().Where(b => b.Id == 1).InnerJoinManyCollectionForward(_tags, _index).ExecutePooled(),
			() => _books.Query().Where(b => b.Id == 1).InnerJoinManyCollectionForward(_tags, _index).Execute());

	// ── Correctness: pooled results equal non-pooled results (full driving set) ──

	[Test]
	public void JoinManyCollection_Reverse_PooledEqualsExecute() {
		var expected = _tags.Query().JoinManyCollection(_books, _index).Execute();
		using var pooled = _tags.Query().JoinManyCollection(_books, _index).ExecutePooled();

		Assert.That(pooled.Count, Is.EqualTo(expected.Count));
		var e = expected.ToDictionary(r => r.Left.Id, r => r.Right.Count);
		var a = pooled.ToDictionary(r => r.Left.Id, r => r.Right.Count);
		Assert.That(a.Keys, Is.EquivalentTo(e.Keys));
		foreach (var id in e.Keys)
			Assert.That(a[id], Is.EqualTo(e[id]), $"Right count mismatch for tag {id}");
		// Tag 1 fans out to the hub + all leaves.
		Assert.That(a[1], Is.EqualTo(BigCount + 1));
	}

	[Test]
	public void JoinManyCollectionForward_PooledEqualsExecute() {
		var expected = _books.Query().JoinManyCollectionForward(_tags, _index).Execute();
		using var pooled = _books.Query().JoinManyCollectionForward(_tags, _index).ExecutePooled();

		Assert.That(pooled.Count, Is.EqualTo(expected.Count));
		var e = expected.ToDictionary(r => r.Left.Id, r => r.Right.Count);
		var a = pooled.ToDictionary(r => r.Left.Id, r => r.Right.Count);
		Assert.That(a.Keys, Is.EquivalentTo(e.Keys));
		foreach (var id in e.Keys)
			Assert.That(a[id], Is.EqualTo(e[id]), $"Right count mismatch for book {id}");
		// Hub book fans out to all BigCount tags.
		Assert.That(a[1], Is.EqualTo(BigCount));
	}

	// ── Hammer the pooled path: a double ArrayPool.Return would corrupt counts or throw ──

	[Test]
	public void Pooled_RepeatedRentAndDispose_NoCorruption() {
		for (var i = 0; i < 50; i++) {
			using var rev = _tags.Query().JoinManyCollection(_books, _index).ExecutePooled();
			Assert.That(rev.First(r => r.Left.Id == 1).Right.Count, Is.EqualTo(BigCount + 1));

			using var fwd = _books.Query().JoinManyCollectionForward(_tags, _index).ExecutePooled();
			Assert.That(fwd.First(r => r.Left.Id == 1).Right.Count, Is.EqualTo(BigCount));
		}
	}

	// ── Helper ──────────────────────────────────────────────────────────────────

	private static void AssertPooledReusesBuffer<TResult>(
		Func<QueryResults<TResult>> pooled, Func<QueryResults<TResult>> execute)
		where TResult : struct {

		// Warm up: JIT the closed-generic path and prime the pool.
		for (var i = 0; i < 5; i++) {
			var w = pooled();
			w.Dispose();
		}

		var sink = 0L;

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < Iterations; i++) {
			var r = pooled();
			sink += r.Count;
			r.Dispose();
		}
		var pooledAlloc = GC.GetAllocatedBytesForCurrentThread() - before;

		before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < Iterations; i++) {
			var r = execute();
			sink += r.Count;
		}
		var executeAlloc = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(sink, Is.GreaterThan(0), "queries did not run");
		Assert.That(pooledAlloc, Is.LessThan(executeAlloc / 2),
			$"pooled should reuse the child buffer: pooled={pooledAlloc} bytes, execute={executeAlloc} bytes over {Iterations} iters");
		Assert.That(pooledAlloc / Iterations, Is.LessThan((long)BigCount * IntPtr.Size),
			$"pooled per-iteration allocation ({pooledAlloc / Iterations} bytes) should be below one child buffer");
	}
}
