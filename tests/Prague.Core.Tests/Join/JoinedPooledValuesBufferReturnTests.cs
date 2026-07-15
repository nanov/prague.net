namespace Prague.Core.Tests.Join;

using System;
using System.Buffers;
using System.Collections.Generic;
using Prague.Core;
using NUnit.Framework;

// ── Domain ──────────────────────────────────────────────────────────────────
// Leak/corruption regressions for the joined-query result container:
//   • an inner join whose pooled result ends EMPTY must return the rented
//     JoinResult values array (BuildResults empty branch)
//   • Count() over an inner-join chain must return the values array its
//     PrepareIndexedInner rented (HardDispose path)
//   • ToList() inside a using must not double-return the Many child buffer
//     (QueryResults.Dispose must be idempotent for the child-buffer disposer)
//
// Detection uses ArrayPool round-trips on a single thread: prime the pool with
// a probe of the same bucket, run the query, rent again — identity proves the
// return happened (or, for double-return, two rents yielding the SAME array
// prove the pool was poisoned).

internal sealed class VbParent : ICacheEquatable<VbParent>, ICacheClonable<VbParent> {
	public int Id { get; init; }

	public bool CacheEquals(VbParent? other) => other is not null && other.Id == Id;
	public int CacheGetHashCode() => Id;
	public VbParent Clone() => new() { Id = Id };
}

internal sealed class VbChild : ICacheEquatable<VbChild>, ICacheClonable<VbChild> {
	public int Id { get; init; }
	public int ParentId { get; init; }

	public bool CacheEquals(VbChild? other) => other is not null && other.Id == Id && other.ParentId == ParentId;
	public int CacheGetHashCode() => HashCode.Combine(Id, ParentId);
	public VbChild Clone() => new() { Id = Id, ParentId = ParentId };
}

internal sealed class VbInfo : ICacheEquatable<VbInfo>, ICacheClonable<VbInfo> {
	public int Id { get; init; }

	public bool CacheEquals(VbInfo? other) => other is not null && other.Id == Id;
	public int CacheGetHashCode() => Id;
	public VbInfo Clone() => new() { Id = Id };
}

[TestFixture]
public class JoinedPooledValuesBufferReturnTests {
	private const int TotalChildren = 25; // parent 1 → 20, parent 2 → 5, parent 3 → none

	private InMemoryDataCache<int, VbParent> _parents = null!;
	private InMemoryDataCache<int, VbChild> _children = null!;
	private InMemoryDataCache<int, VbInfo> _infos = null!;
	private CacheKeyValueListIndex<int, VbChild, int> _childByParentId = null!;

	[SetUp]
	public void SetUp() {
		_parents = new InMemoryDataCache<int, VbParent>();
		_children = new InMemoryDataCache<int, VbChild>();
		_infos = new InMemoryDataCache<int, VbInfo>();
		_childByParentId = _children.CacheKeyValueListIndex<int>((_, v) => v.ParentId);

		_parents.AddOrUpdate(1, new VbParent { Id = 1 });
		_parents.AddOrUpdate(2, new VbParent { Id = 2 });
		_parents.AddOrUpdate(3, new VbParent { Id = 3 });

		_infos.AddOrUpdate(1, new VbInfo { Id = 1 });
		_infos.AddOrUpdate(2, new VbInfo { Id = 2 });

		var childId = 1;
		for (var i = 0; i < 20; i++)
			_children.AddOrUpdate(childId, new VbChild { Id = childId++, ParentId = 1 });
		for (var i = 0; i < 5; i++)
			_children.AddOrUpdate(childId, new VbChild { Id = childId++, ParentId = 2 });
	}

	[Test]
	public void InnerJoinMany_EmptyPooledResult_ReturnsValuesBuffer() {
		var pool = ArrayPool<JoinResult<VbParent, QueryResults<VbChild>>>.Shared;
		var probe = pool.Rent(1);
		pool.Return(probe, clearArray: true);

		// Parent 3 has no children — the inner join narrows the pooled result to empty
		// AFTER Init already rented the values array.
		using (var r = _parents.Query().Where(p => p.Id == 3).InnerJoinMany(_children, _childByParentId).ExecutePooled())
			Assert.That(r.Count, Is.EqualTo(0));

		var after = pool.Rent(1);
		pool.Return(after, clearArray: true);
		Assert.That(ReferenceEquals(after, probe), Is.True,
			"empty pooled joined result leaked the rented JoinResult values array");
	}

	[Test]
	public void InnerJoinMany_Count_ReturnsValuesBuffer() {
		var pool = ArrayPool<JoinResult<VbParent, QueryResults<VbChild>>>.Shared;
		var probe = pool.Rent(1);
		pool.Return(probe, clearArray: true);

		var count = _parents.Query().InnerJoinMany(_children, _childByParentId).Count();
		Assert.That(count, Is.EqualTo(2)); // parent 3 has no children — inner semantics

		var after = pool.Rent(1);
		pool.Return(after, clearArray: true);
		Assert.That(ReferenceEquals(after, probe), Is.True,
			"Count() over an inner-join chain leaked the rented JoinResult values array");
	}

	// ── Count() must agree with Execute() on inner-join semantics ───────────────

	[Test]
	public void InnerJoinMany_Count_MatchesExecuteCount() {
		var executed = _parents.Query().InnerJoinMany(_children, _childByParentId).Execute();
		var count = _parents.Query().InnerJoinMany(_children, _childByParentId).Count();
		Assert.That(executed.Count, Is.EqualTo(2)); // parent 3 has no children
		Assert.That(count, Is.EqualTo(executed.Count));
	}

	[Test]
	public void InnerJoinOne_Count_MatchesExecuteCount() {
		var executed = _parents.Query().InnerJoinOne(_infos).Execute();
		var count = _parents.Query().InnerJoinOne(_infos).Count();
		Assert.That(executed.Count, Is.EqualTo(2)); // parent 3 has no info
		Assert.That(count, Is.EqualTo(executed.Count));
	}

	[Test]
	public void JoinMany_Outer_Count_KeepsAllLefts() {
		var count = _parents.Query().JoinMany(_children, _childByParentId).Count();
		Assert.That(count, Is.EqualTo(3)); // outer join preserves every left
	}

	[Test]
	public void Chained_JoinOne_InnerJoinMany_Count_MatchesExecuteCount() {
		var executed = _parents.Query().JoinOne(_infos).InnerJoinMany(_children, _childByParentId).Execute();
		var count = _parents.Query().JoinOne(_infos).InnerJoinMany(_children, _childByParentId).Count();
		Assert.That(executed.Count, Is.EqualTo(2)); // inner Many drops parent 3, outer One keeps the rest
		Assert.That(count, Is.EqualTo(executed.Count));
	}

	[Test]
	public void Where_InnerJoinMany_Count_MatchesExecuteCount() {
		var executed = _parents.Query().Where(p => p.Id == 1).InnerJoinMany(_children, _childByParentId).Execute();
		var count = _parents.Query().Where(p => p.Id == 1).InnerJoinMany(_children, _childByParentId).Count();
		Assert.That(executed.Count, Is.EqualTo(1));
		Assert.That(count, Is.EqualTo(executed.Count));
	}

	[Test]
	public void ExecutePooled_ToListInsideUsing_DoesNotDoubleReturnChildBuffer() {
		List<JoinResult<VbParent, QueryResults<VbChild>>> list;
		using (var r = _parents.Query().JoinMany(_children, _childByParentId).ExecutePooled())
			list = r.ToList(); // disposes internally; the using adds a second Dispose

		Assert.That(list, Has.Count.EqualTo(3));

		// A double ArrayPool.Return poisons the pool: the same array sits in it twice,
		// so two consecutive rents of that bucket hand out one array to two callers.
		var pool = ArrayPool<VbChild>.Shared;
		var a = pool.Rent(TotalChildren);
		var b = pool.Rent(TotalChildren);
		try {
			Assert.That(ReferenceEquals(a, b), Is.False,
				"the Many-join child buffer was returned to the pool twice");
		} finally {
			pool.Return(a, clearArray: true);
			pool.Return(b, clearArray: true);
		}
	}
}
