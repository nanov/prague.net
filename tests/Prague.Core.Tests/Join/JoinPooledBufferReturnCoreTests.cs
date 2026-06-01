namespace Prague.Core.Tests.Join;

using System;
using System.Linq;
using Prague.Core;
using NUnit.Framework;

// ── Domain ──────────────────────────────────────────────────────────────────
// Verifies that ExecutePooled() on every JoinMany path RETURNS the rented child
// buffer to the pool on Dispose() (so repeated calls reuse it instead of
// re-allocating ~child-count refs per query). Covers:
//   • JoinMany / InnerJoinMany over a right list index (RightList resolver)
//   • JoinMany / InnerJoinMany over a left symmetric index (LeftSym resolver)
//   • JoinOne / InnerJoinOne (no child buffer — regression only)
//   • mixed multi-level chain, and an empty inner-join result
//
//   PbParent (PK Id, string Region) — sym list index on Region
//   PbChild  (PK Id, int ParentId, string Region) — list indexes on ParentId + Region
//   PbInfo   (PK Id == ParentId)    — 1:1 by PK

internal sealed class PbParent : ICacheEquatable<PbParent>, ICacheClonable<PbParent> {
	public int Id { get; init; }
	public string Region { get; init; } = "";

	public bool CacheEquals(PbParent? other) => other is not null && other.Id == Id && other.Region == Region;
	public int CacheGetHashCode() => HashCode.Combine(Id, Region);
	public PbParent Clone() => new() { Id = Id, Region = Region };
}

internal sealed class PbChild : ICacheEquatable<PbChild>, ICacheClonable<PbChild> {
	public int Id { get; init; }
	public int ParentId { get; init; }
	public string Region { get; init; } = "";

	public bool CacheEquals(PbChild? other) =>
		other is not null && other.Id == Id && other.ParentId == ParentId && other.Region == Region;
	public int CacheGetHashCode() => HashCode.Combine(Id, ParentId, Region);
	public PbChild Clone() => new() { Id = Id, ParentId = ParentId, Region = Region };
}

internal sealed class PbInfo : ICacheEquatable<PbInfo>, ICacheClonable<PbInfo> {
	public int Id { get; init; }
	public string Data { get; init; } = "";

	public bool CacheEquals(PbInfo? other) => other is not null && other.Id == Id && other.Data == Data;
	public int CacheGetHashCode() => HashCode.Combine(Id, Data);
	public PbInfo Clone() => new() { Id = Id, Data = Data };
}

[TestFixture]
public class JoinPooledBufferReturnCoreTests {
	private const int BigChildCount = 2000; // big enough that the child buffer dominates allocation
	private const int Iterations = 100;

	private InMemoryDataCache<int, PbParent> _parents = null!;
	private InMemoryDataCache<int, PbChild> _children = null!;
	private InMemoryDataCache<int, PbInfo> _infos = null!;
	private CacheKeyValueListIndex<int, PbChild, int> _childByParentId = null!;
	private CacheKeyValueListIndex<int, PbChild, string> _childByRegion = null!;
	private CacheSymmetricKeyValueListIndex<int, PbParent, string> _parentRegionSym = null!;

	[SetUp]
	public void SetUp() {
		_parents = new InMemoryDataCache<int, PbParent>();
		_children = new InMemoryDataCache<int, PbChild>();
		_infos = new InMemoryDataCache<int, PbInfo>();

		_childByParentId = _children.CacheKeyValueListIndex<int>((_, v) => v.ParentId);
		_childByRegion = _children.CacheKeyValueListIndex<string>((_, v) => v.Region);
		_parentRegionSym = _parents.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Region);

		// Parent 1 (R1) → BigChildCount children. Parent 2 (R2) → 5 children. Parent 3 (R3) → none.
		_parents.AddOrUpdate(1, new PbParent { Id = 1, Region = "R1" });
		_parents.AddOrUpdate(2, new PbParent { Id = 2, Region = "R2" });
		_parents.AddOrUpdate(3, new PbParent { Id = 3, Region = "R3" });

		_infos.AddOrUpdate(1, new PbInfo { Id = 1, Data = "info-1" });
		_infos.AddOrUpdate(2, new PbInfo { Id = 2, Data = "info-2" });

		var childId = 1;
		for (var i = 0; i < BigChildCount; i++)
			_children.AddOrUpdate(childId, new PbChild { Id = childId++, ParentId = 1, Region = "R1" });
		for (var i = 0; i < 5; i++)
			_children.AddOrUpdate(childId, new PbChild { Id = childId++, ParentId = 2, Region = "R2" });
	}

	// ── Allocation regression: pooled path must reuse the child buffer ──────────
	//
	// Compares total allocation of N pooled (rent+Dispose) iterations against N
	// allocating Execute() iterations. With the buffer correctly returned, the
	// pooled path allocates a small constant per call; without the fix it
	// re-rents the ~BigChildCount-ref buffer every call and the assertion fails.

	[Test]
	public void JoinMany_RightList_Outer_Pooled_ReusesChildBuffer() =>
		AssertPooledReusesBuffer(
			() => _parents.Query().JoinMany(_children, _childByParentId).ExecutePooled(),
			() => _parents.Query().JoinMany(_children, _childByParentId).Execute());

	[Test]
	public void InnerJoinMany_RightList_Inner_Pooled_ReusesChildBuffer() =>
		AssertPooledReusesBuffer(
			() => _parents.Query().InnerJoinMany(_children, _childByParentId).ExecutePooled(),
			() => _parents.Query().InnerJoinMany(_children, _childByParentId).Execute());

	[Test]
	public void JoinMany_LeftSym_Outer_Pooled_ReusesChildBuffer() =>
		AssertPooledReusesBuffer(
			() => _parents.Query().JoinMany(_parentRegionSym, _children, _childByRegion).ExecutePooled(),
			() => _parents.Query().JoinMany(_parentRegionSym, _children, _childByRegion).Execute());

	[Test]
	public void InnerJoinMany_LeftSym_Inner_Pooled_ReusesChildBuffer() =>
		AssertPooledReusesBuffer(
			() => _parents.Query().InnerJoinMany(_parentRegionSym, _children, _childByRegion).ExecutePooled(),
			() => _parents.Query().InnerJoinMany(_parentRegionSym, _children, _childByRegion).Execute());

	// ── Correctness: pooled results equal non-pooled results ────────────────────

	[Test]
	public void JoinMany_RightList_Outer_PooledEqualsExecute() {
		var expected = _parents.Query().JoinMany(_children, _childByParentId).Execute();
		using var pooled = _parents.Query().JoinMany(_children, _childByParentId).ExecutePooled();
		AssertSameManyResults(expected, pooled);
	}

	[Test]
	public void InnerJoinMany_RightList_Inner_PooledEqualsExecute() {
		var expected = _parents.Query().InnerJoinMany(_children, _childByParentId).Execute();
		using var pooled = _parents.Query().InnerJoinMany(_children, _childByParentId).ExecutePooled();
		// Inner drops parent 3 (no children).
		Assert.That(pooled.Count, Is.EqualTo(2));
		AssertSameManyResults(expected, pooled);
	}

	[Test]
	public void JoinMany_LeftSym_Outer_PooledEqualsExecute() {
		var expected = _parents.Query().JoinMany(_parentRegionSym, _children, _childByRegion).Execute();
		using var pooled = _parents.Query().JoinMany(_parentRegionSym, _children, _childByRegion).ExecutePooled();
		AssertSameManyResults(expected, pooled);
	}

	[Test]
	public void InnerJoinMany_LeftSym_Inner_PooledEqualsExecute() {
		var expected = _parents.Query().InnerJoinMany(_parentRegionSym, _children, _childByRegion).Execute();
		using var pooled = _parents.Query().InnerJoinMany(_parentRegionSym, _children, _childByRegion).ExecutePooled();
		Assert.That(pooled.Count, Is.EqualTo(2)); // R3 parent dropped
		AssertSameManyResults(expected, pooled);
	}

	[Test]
	public void JoinOne_Pooled_DoesNotThrowAndMatchesExecute() {
		var expected = _parents.Query().JoinOne(_infos).Execute();
		using var pooled = _parents.Query().JoinOne(_infos).ExecutePooled();
		Assert.That(pooled.Count, Is.EqualTo(expected.Count));
		var byId = pooled.ToDictionary(r => r.Left.Id, r => r.Right?.Data);
		Assert.That(byId[1], Is.EqualTo("info-1"));
		Assert.That(byId[2], Is.EqualTo("info-2"));
		Assert.That(byId[3], Is.Null); // no info for parent 3 (outer)
	}

	[Test]
	public void InnerJoinOne_Pooled_DropsUnmatchedLeft() {
		using var pooled = _parents.Query().InnerJoinOne(_infos).ExecutePooled();
		var ids = pooled.Select(r => r.Left.Id).OrderBy(i => i).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 })); // parent 3 has no info
	}

	[Test]
	public void Chain_JoinOne_Then_JoinMany_PooledEqualsExecute() {
		var expected = _parents.Query()
			.JoinOne(_infos)
			.JoinMany(_children, _childByParentId)
			.Execute();
		using var pooled = _parents.Query()
			.JoinOne(_infos)
			.JoinMany(_children, _childByParentId)
			.ExecutePooled();

		Assert.That(pooled.Count, Is.EqualTo(expected.Count));
		var pById = pooled.ToDictionary(r => r.Left.Id);
		var eById = expected.ToDictionary(r => r.Left.Id);
		foreach (var id in eById.Keys) {
			Assert.That(pById[id].Right?.Data, Is.EqualTo(eById[id].Right?.Data));
			Assert.That(pById[id].Right2.Count, Is.EqualTo(eById[id].Right2.Count));
		}
	}

	[Test]
	public void InnerJoinMany_EmptyResult_Pooled_DoesNotThrow() {
		// Restrict to parent 3 (no children) so the inner join narrows to empty —
		// exercises the BuildResults empty short-circuit with a live disposer.
		using var pooled = _parents.Query()
			.Where(p => p.Id == 3)
			.InnerJoinMany(_children, _childByParentId)
			.ExecutePooled();
		Assert.That(pooled.Count, Is.EqualTo(0));
	}

	// ── Helpers ─────────────────────────────────────────────────────────────────

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
		Assert.That(pooledAlloc / Iterations, Is.LessThan((long)BigChildCount * IntPtr.Size),
			$"pooled per-iteration allocation ({pooledAlloc / Iterations} bytes) should be below one child buffer");
	}

	private static void AssertSameManyResults<TRight>(
		QueryResults<JoinResult<PbParent, QueryResults<TRight>>> expected,
		QueryResults<JoinResult<PbParent, QueryResults<TRight>>> actual)
		where TRight : ICacheEquatable<TRight> {

		Assert.That(actual.Count, Is.EqualTo(expected.Count));
		var eById = expected.ToDictionary(r => r.Left.Id);
		var aById = actual.ToDictionary(r => r.Left.Id);
		Assert.That(aById.Keys, Is.EquivalentTo(eById.Keys));
		foreach (var id in eById.Keys)
			Assert.That(aById[id].Right.Count, Is.EqualTo(eById[id].Right.Count), $"Right count mismatch for {id}");
	}
}
