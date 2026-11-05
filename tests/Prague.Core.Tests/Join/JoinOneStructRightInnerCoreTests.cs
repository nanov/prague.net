namespace Prague.Core.Tests.Join;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using NUnit.Framework;

internal sealed class SrLeft : ICacheEquatable<SrLeft>, ICacheClonable<SrLeft> {
	public int Id { get; init; }
	public int Group { get; init; }
	public int Code { get; init; }

	public bool CacheEquals(SrLeft? other) => other is not null && other.Id == Id && other.Group == Group && other.Code == Code;
	public int CacheGetHashCode() => HashCode.Combine(Id, Group, Code);
	public SrLeft Clone() => new() { Id = Id, Group = Group, Code = Code };
}

// A struct right keyed by int: the identity PK-to-PK and left-symmetric / left-unique shapes need TRightKey = int.
internal struct SrStruct : ICacheEquatable<SrStruct>, ICacheClonable<SrStruct> {
	public int Id { get; init; }
	public int Owner { get; init; }
	public string Label { get; init; }

	public bool CacheEquals(SrStruct other) => other.Id == Id && other.Owner == Owner && other.Label == Label;
	public int CacheGetHashCode() => HashCode.Combine(Id, Owner, Label);
	public SrStruct Clone() => this;
}

// An inner JoinOne with a STRUCT right value keeps exactly the rows it matched. The slot post-walks
// (RetainNonNullSlots / PruneNullSlots) read a miss as a null slot, which a struct never is, so before the
// key-based post-walk a chained inner join or the fused frozen fill kept every row (reproduced on f6c305a:
// InnerJoinOne(a).InnerJoinOne(structRight) returned all of a). Every family, chained and unchained, eager,
// Count and the fused frozen fill.
[TestFixture]
public class JoinOneStructRightInnerCoreTests {
	private InMemoryDataCache<int, SrLeft> _left = null!;
	private CacheKeyValueListIndex<int, SrLeft, int> _byGroup = null!;
	private CacheSymmetricKeyValueListIndex<int, SrLeft, int> _byCode = null!;
	private CacheSymmetricUniqueIndex<int, SrLeft, int> _uniqueCode = null!;
	private InMemoryDataCache<int, SelLeft> _all = null!;

	// Struct rights, only for lefts 1 and 3 (right id = left id; Owner = left id).
	private InMemoryDataCache<int, SrStruct> _structs = null!;
	private CacheUniqueIndex<int, SrStruct, int> _structByOwner = null!;

	[SetUp]
	public void SetUp() {
		_left = new InMemoryDataCache<int, SrLeft>();
		_byGroup = _left.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byCode = _left.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.Code);
		_uniqueCode = _left.AddSymmetricKeyValueIndex<int>(static (_, v) => v.Id);
		for (var i = 1; i <= 4; i++)
			_left.AddOrUpdate(i, new SrLeft { Id = i, Group = 7, Code = i });

		_structs = new InMemoryDataCache<int, SrStruct>();
		_structByOwner = _structs.AddKeyValueIndex<int>(static (_, v) => v.Owner);
		_structs.AddOrUpdate(1, new SrStruct { Id = 1, Owner = 1, Label = "s1" });
		_structs.AddOrUpdate(3, new SrStruct { Id = 3, Owner = 3, Label = "s3" });

		// A reference right every left has: the first link of the chained cases.
		_all = new InMemoryDataCache<int, SelLeft>();
		for (var i = 1; i <= 4; i++)
			_all.AddOrUpdate(i, new SelLeft { Id = i, Name = $"A{i}" });
	}

	private static string Ids<TRow>(IEnumerable<TRow> rows, Func<TRow, int> id)
		=> string.Join(",", rows.Select(id).OrderBy(static x => x));

	// ── Unchained: the first inner join creates rows for matches only ──

	[Test]
	public void PkIdentity_Inner_OnlyMatched() {
		var rows = _left.Query().InnerJoinOne(_structs).Execute();
		Assert.That(Ids(rows, static r => r.Left.Id), Is.EqualTo("1,3"));
		Assert.That(rows.All(static r => r.Right.Label is "s1" or "s3"), Is.True);
	}

	[Test]
	public void LeftSym_Inner_OnlyMatched() {
		var rows = _left.Query().InnerJoinOne(_byCode, _structs).Execute();
		Assert.That(Ids(rows, static r => r.Left.Id), Is.EqualTo("1,3"));
	}

	// ── Chained: the second inner join meets rows the first created ──

	[Test]
	public void PkIdentity_ChainedInner_StructSecond_OnlyMatched() {
		var query = _left.Query().InnerJoinOne(_all).InnerJoinOne(_structs);
		Assert.That(Ids(query.Execute(), static r => r.Left.Id), Is.EqualTo("1,3"));
		Assert.That(_left.Query().InnerJoinOne(_all).InnerJoinOne(_structs).Count(), Is.EqualTo(2));
	}

	[Test]
	public void PkSelector_ChainedInner_StructSecond_OnlyMatched() {
		var rows = _left.Query().InnerJoinOne(_all).InnerJoinOne(static (int id) => id, _structs).Execute();
		Assert.That(Ids(rows, static r => r.Left.Id), Is.EqualTo("1,3"));
	}

	[Test]
	public void PkSelector_ChainedInner_StructSecond_FilterRejectsEverything_Empty() {
		var rows = _left.Query().InnerJoinOne(_all).InnerJoinOne(static (int id) => id, _structs, static q => q.Where(static _ => false)).Execute();
		Assert.That(rows, Is.Empty);
	}

	[Test]
	public void PkSelector_ChainedInner_StructSecond_NoRightAtAll_Empty() {
		// No left key has a resident right: the empty-pair early-out drops the first join's rows too.
		var rows = _left.Query().InnerJoinOne(_all).InnerJoinOne(static (int id) => id + 100, _structs).Execute();
		Assert.That(rows, Is.Empty);
	}

	[Test]
	public void RightUnique_ChainedInner_StructSecond_OnlyMatched() {
		var rows = _left.Query().InnerJoinOne(_all).InnerJoinOne(_structs, _structByOwner).Execute();
		Assert.That(Ids(rows, static r => r.Left.Id), Is.EqualTo("1,3"));
	}

	[Test]
	public void LeftUnique_ChainedInner_StructSecond_OnlyMatched() {
		var rows = _left.Query().InnerJoinOne(_all).InnerJoinOne(_uniqueCode, _structs).Execute();
		Assert.That(Ids(rows, static r => r.Left.Id), Is.EqualTo("1,3"));
	}

	[Test]
	public void LeftSym_ChainedInner_StructSecond_OnlyMatched() {
		var rows = _left.Query().InnerJoinOne(_all).InnerJoinOne(_byCode, _structs).Execute();
		Assert.That(Ids(rows, static r => r.Left.Id), Is.EqualTo("1,3"));
	}

	[Test]
	public void LeftSym_ChainedInner_StructSecond_Filtered_OnlyKept() {
		var rows = _left.Query().InnerJoinOne(_all).InnerJoinOne(_byCode, _structs, static q => q.Where(static s => s.Label == "s3")).Execute();
		Assert.That(Ids(rows, static r => r.Left.Id), Is.EqualTo("3"));
	}

	// ── The fused frozen fill prunes a struct right's misses by key ──

	[Test]
	public void Frozen_FusedInner_Struct_OnlyMatched() {
		var frozen = _left.Prepare<int, SrLeft, int>().UseIndex(_byGroup, static g => g).InnerJoinOne(_structs).BuildFrozen();
		Assert.That(frozen.Explain(), Does.Contain("executor: Pipeline").And.Contain("fused: 1"));
		using var rows = frozen.Execute(7);
		Assert.That(Ids(rows, static r => r.Left.Id), Is.EqualTo("1,3"));
		Assert.That(frozen.Count(7), Is.EqualTo(2));
	}

	[Test]
	public void Frozen_FusedChainedInner_StructSecond_OnlyMatched() {
		var frozen = _left.Prepare<int, SrLeft, int>().UseIndex(_byGroup, static g => g).InnerJoinOne(_all).InnerJoinOne(_structs).BuildFrozen();
		Assert.That(frozen.Explain(), Does.Contain("executor: Pipeline"));
		using var rows = frozen.Execute(7);
		Assert.That(Ids(rows, static r => r.Left.Id), Is.EqualTo("1,3"));
		Assert.That(frozen.Count(7), Is.EqualTo(2));
	}

	[Test]
	public void StructInner_Eager_Frozen_Balanced() =>
		LeakAssert.Balanced(() => {
			using (var eager = _left.Query().InnerJoinOne(_all).InnerJoinOne(_byCode, _structs).ExecutePooled())
				Assert.That(eager.Count, Is.EqualTo(2));
			using (var selector = _left.Query().InnerJoinOne(_all).InnerJoinOne(static (int id) => id, _structs).ExecutePooled())
				Assert.That(selector.Count, Is.EqualTo(2));
			var frozen = _left.Prepare<int, SrLeft, int>().UseIndex(_byGroup, static g => g).InnerJoinOne(_structs).BuildFrozen();
			using (var fused = frozen.ExecutePooled(7))
				Assert.That(fused.Count, Is.EqualTo(2));
		});
}
