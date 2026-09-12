namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model — a left with a PK-to-PK right and two FK-ish int lanes ─────

internal sealed class EmpLeft : ICacheEquatable<EmpLeft>, ICacheClonable<EmpLeft> {
	public int Id { get; init; }
	public int GroupId { get; init; }
	public int LinkId { get; init; }

	public bool CacheEquals(EmpLeft? other) => other is not null && other.Id == Id && other.GroupId == GroupId && other.LinkId == LinkId;

	public int CacheGetHashCode() => HashCode.Combine(Id, GroupId, LinkId);

	public EmpLeft Clone() => new() { Id = Id, GroupId = GroupId, LinkId = LinkId };
}

internal sealed class EmpRight : ICacheEquatable<EmpRight>, ICacheClonable<EmpRight> {
	public int Id { get; init; }
	public int Code { get; init; }

	public bool CacheEquals(EmpRight? other) => other is not null && other.Id == Id && other.Code == Code;

	public int CacheGetHashCode() => HashCode.Combine(Id, Code);

	public EmpRight Clone() => new() { Id = Id, Code = Code };
}

/// <summary>
///   Eager-only regression pin for the <b>empty pair set</b> branch of the inner <c>JoinOne</c> resolvers.
///   When a chained inner join finds that no left has a right at all, it used to narrow the candidate set
///   to nothing and return — leaving behind the rows an <i>earlier</i> chained inner resolver had already
///   created in the result map. The outer base execute then walked an empty candidate set and never filled
///   their <c>Left</c>, so <c>Execute()</c> handed back rows with a default (null) left. The resolvers now
///   run the same <c>RetainNonNullSlots</c> post-walk the non-empty path runs, which drops those rows.
///   No prepared or frozen query is involved: this is the eager builder's own behaviour.
///   <para>
///   Reachability: the branch is entered when the pair seeding produces nothing, which happens for
///   PK-to-PK (the right store is empty), right-unique (the right index is empty) and left-symmetric
///   shape B (the translating right index is empty). For left-unique and left-symmetric shape A the pair
///   seeding reads only the <i>left</i> index, so it always emits a pair for a live candidate and the
///   branch is defensive; those two are covered here through the non-empty-but-no-hits path, which must
///   drop the same rows.
///   </para>
/// </summary>
[TestFixture]
public class JoinOneChainedInnerEmptyPairSetCoreTests {
	private const int N = 12;

	private InMemoryDataCache<int, EmpLeft> _lefts = null!;
	private CacheKeyValueListIndex<int, EmpLeft, int> _byGroup = null!;
	private CacheSymmetricKeyValueListIndex<int, EmpLeft, int> _byLink = null!;
	private CacheSymmetricUniqueIndex<int, EmpLeft, int> _linkUnique = null!;
	private InMemoryDataCache<int, EmpRight> _first = null!;
	private InMemoryDataCache<int, EmpRight> _empty = null!;
	private CacheUniqueIndex<int, EmpRight, int> _emptyByCode = null!;

	[SetUp]
	public void SetUp() {
		_lefts = new InMemoryDataCache<int, EmpLeft>();
		_byGroup = _lefts.CacheKeyValueListIndex<int>(static (_, v) => v.GroupId);
		_byLink = _lefts.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.LinkId);
		_linkUnique = _lefts.AddSymmetricKeyValueIndex<int>(static (id, _) => 500 + id);
		_first = new InMemoryDataCache<int, EmpRight>();
		_empty = new InMemoryDataCache<int, EmpRight>();
		_emptyByCode = _empty.AddKeyValueIndex<int>(static (_, v) => v.Code);
		for (var i = 0; i < N; i++) {
			_lefts.AddOrUpdate(i, new EmpLeft { Id = i, GroupId = i % 3, LinkId = i % 4 });
			// The first inner join matches every left, so it creates a row for every candidate.
			_first.AddOrUpdate(i, new EmpRight { Id = i, Code = 1000 + i });
		}
	}

	/// <summary>The first inner join alone keeps every candidate — so the drops below are the second join's.</summary>
	[Test]
	public void FirstInnerJoinAlone_KeepsEveryCandidate() {
		using var rows = _lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).Execute();
		Assert.That(rows.Count, Is.EqualTo(4), "group 0 holds ids 0, 3, 6, 9");
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Left, Is.Not.Null);
			Assert.That(rows[i].Right, Is.Not.Null);
		}
	}

	// ── The empty-pair-set branch, per family ────────────────────────────────

	[Test]
	public void PkToPk_SecondInnerJoinWithAnEmptyRightStore_DropsTheFirstJoinsRows() {
		using var rows = _lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_empty).Execute();
		AssertNoPhantomRows(in rows);
		Assert.That(_lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_empty).Count(), Is.Zero);
	}

	[Test]
	public void RightUnique_SecondInnerJoinWithAnEmptyIndex_DropsTheFirstJoinsRows() {
		using var rows = _lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_empty, _emptyByCode).Execute();
		AssertNoPhantomRows(in rows);
		Assert.That(_lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_empty, _emptyByCode).Count(), Is.Zero);
	}

	[Test]
	public void LeftSymViaRightIndex_SecondInnerJoinWithAnEmptyIndex_DropsTheFirstJoinsRows() {
		using var rows = _lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_byLink, _empty, _emptyByCode).Execute();
		AssertNoPhantomRows(in rows);
		Assert.That(_lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_byLink, _empty, _emptyByCode).Count(), Is.Zero);
	}

	// ── The non-empty-but-no-hits path, for the two families whose seeding always emits pairs ──

	[Test]
	public void LeftUnique_SecondInnerJoinWithAnEmptyRightStore_DropsTheFirstJoinsRows() {
		using var rows = _lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_linkUnique, _empty).Execute();
		AssertNoPhantomRows(in rows);
		Assert.That(_lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_linkUnique, _empty).Count(), Is.Zero);
	}

	[Test]
	public void LeftSym_SecondInnerJoinWithAnEmptyRightStore_DropsTheFirstJoinsRows() {
		using var rows = _lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_byLink, _empty).Execute();
		AssertNoPhantomRows(in rows);
		Assert.That(_lefts.Query().UseIndex(_byGroup, 0).InnerJoinOne(_first).InnerJoinOne(_byLink, _empty).Count(), Is.Zero);
	}

	// A row the second inner join could not satisfy must not survive — and if one ever does, it must at
	// least carry the left the outer base execute never filled, which is the shape of the old defect.
	private static void AssertNoPhantomRows(in QueryResults<JoinResult<EmpLeft, EmpRight?, EmpRight?>> rows) {
		for (var i = 0; i < rows.Count; i++)
			Assert.That(rows[i].Left, Is.Not.Null, "row " + i + " has no Left: the outer base execute never saw it");
		var (count, total) = (rows.Count, rows.TotalCount);
		Assert.Multiple(() => {
			Assert.That(count, Is.Zero, "no left has a right in the second inner join, so no row survives it");
			Assert.That(total, Is.Zero);
		});
	}
}
