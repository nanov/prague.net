namespace Prague.Core.Tests.Join;

using System;
using System.Buffers;
using Prague.Core;
using Prague.Core.Collections;
using NUnit.Framework;

// ── Domain ──────────────────────────────────────────────────────────────────
// When a joined query stage throws BEFORE the base execute runs (here: the user
// Where predicate throws during candidate auto-populate inside
// PrepareIndexedInner), the left candidate ValueSet has already rented its
// arrays — the executor must return them on the way out.
//
// Detection: the candidate set's SlotMeta[] comes from ArrayPool<SlotMeta>.Shared,
// and exactly one such rent happens in the failing query (GetPrime(100) = 101 →
// the 128 bucket). Prime the pool's thread-local slot with a probe, run the
// throwing query, rent again: getting the probe back proves the set was disposed.
// The cache must exceed ValueSet's inline threshold (47) so the set actually rents.

internal sealed class CtParent : ICacheEquatable<CtParent>, ICacheClonable<CtParent> {
	public int Id { get; init; }

	public bool CacheEquals(CtParent? other) => other is not null && other.Id == Id;
	public int CacheGetHashCode() => Id;
	public CtParent Clone() => new() { Id = Id };
}

internal sealed class CtChild : ICacheEquatable<CtChild>, ICacheClonable<CtChild> {
	public int Id { get; init; }
	public int ParentId { get; init; }

	public bool CacheEquals(CtChild? other) => other is not null && other.Id == Id && other.ParentId == ParentId;
	public int CacheGetHashCode() => HashCode.Combine(Id, ParentId);
	public CtChild Clone() => new() { Id = Id, ParentId = ParentId };
}

[TestFixture]
public class JoinedCandidatesReturnOnThrowTests {
	private const int ParentCount = 100;

	private InMemoryDataCache<int, CtParent> _parents = null!;
	private InMemoryDataCache<int, CtChild> _children = null!;
	private CacheKeyValueListIndex<int, CtChild, int> _childByParentId = null!;

	[SetUp]
	public void SetUp() {
		_parents = new InMemoryDataCache<int, CtParent>();
		_children = new InMemoryDataCache<int, CtChild>();
		_childByParentId = _children.CacheKeyValueListIndex<int>((_, v) => v.ParentId);

		for (var i = 1; i <= ParentCount; i++)
			_parents.AddOrUpdate(i, new CtParent { Id = i });
		_children.AddOrUpdate(1, new CtChild { Id = 1, ParentId = 1 });
		_children.AddOrUpdate(2, new CtChild { Id = 2, ParentId = 2 });
	}

	[Test]
	public void ExecutePooled_WherePredicateThrows_ReturnsCandidateSet() =>
		AssertCandidateSetReturned(() => {
			var builder = _parents.Query()
				.Where(_ => throw new InvalidOperationException("boom"))
				.InnerJoinMany(_children, _childByParentId);
			Assert.Throws<InvalidOperationException>(() => builder.ExecutePooled());
		});

	[Test]
	public void Count_WherePredicateThrows_ReturnsCandidateSet() =>
		AssertCandidateSetReturned(() => {
			var builder = _parents.Query()
				.Where(_ => throw new InvalidOperationException("boom"))
				.InnerJoinMany(_children, _childByParentId);
			Assert.Throws<InvalidOperationException>(() => builder.Count());
		});

	private static void AssertCandidateSetReturned(Action runThrowingQuery) {
		var pool = ArrayPool<SlotMeta>.Shared;
		var probe = pool.Rent(ParentCount + 1); // GetPrime(ParentCount) = 101 → same 128 bucket
		pool.Return(probe, clearArray: true);

		runThrowingQuery();

		var after = pool.Rent(ParentCount + 1);
		pool.Return(after, clearArray: true);
		Assert.That(ReferenceEquals(after, probe), Is.True,
			"the left candidate set rented before the throw was not returned to the pool");
	}
}
