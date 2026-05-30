namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model — three POCOs that chain via PK-to-PK identity ─────────────

internal sealed class ChainA : ICacheEquatable<ChainA>, ICacheClonable<ChainA> {
	public int Id { get; init; }
	public string? Name { get; init; }
	public bool CacheEquals(ChainA? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public ChainA Clone() => new() { Id = Id, Name = Name };
}

internal sealed class ChainB : ICacheEquatable<ChainB>, ICacheClonable<ChainB> {
	public int Id { get; init; }
	public string? Status { get; init; }
	public bool CacheEquals(ChainB? other) => other is not null && other.Id == Id && other.Status == Status;
	public int CacheGetHashCode() => HashCode.Combine(Id, Status);
	public ChainB Clone() => new() { Id = Id, Status = Status };
}

internal sealed class ChainC : ICacheEquatable<ChainC>, ICacheClonable<ChainC> {
	public int Id { get; init; }
	public int Score { get; init; }
	public bool CacheEquals(ChainC? other) => other is not null && other.Id == Id && other.Score == Score;
	public int CacheGetHashCode() => HashCode.Combine(Id, Score);
	public ChainC Clone() => new() { Id = Id, Score = Score };
}

// ── Phase 1B verification: chained .JoinOne(...).JoinOne(...) ─────────
// Exercises the T4-emitted level-1 PK-to-PK identity overload.

[TestFixture]
public class JoinOneChainedCoreTests {
	private InMemoryDataCache<int, ChainA> _aCache = null!;
	private InMemoryDataCache<int, ChainB> _bCache = null!;
	private InMemoryDataCache<int, ChainC> _cCache = null!;

	[SetUp]
	public void SetUp() {
		_aCache = new InMemoryDataCache<int, ChainA>();
		_bCache = new InMemoryDataCache<int, ChainB>();
		_cCache = new InMemoryDataCache<int, ChainC>();
	}

	[Test]
	public void JoinOne_Chained_TwoLevels_AllMatched() {
		_aCache.AddOrUpdate(1, new ChainA { Id = 1, Name = "one" });
		_aCache.AddOrUpdate(2, new ChainA { Id = 2, Name = "two" });

		_bCache.AddOrUpdate(1, new ChainB { Id = 1, Status = "active" });
		_bCache.AddOrUpdate(2, new ChainB { Id = 2, Status = "inactive" });

		_cCache.AddOrUpdate(1, new ChainC { Id = 1, Score = 100 });
		_cCache.AddOrUpdate(2, new ChainC { Id = 2, Score = 200 });

		var results = _aCache.Query()
			.JoinOne(_bCache)
			.JoinOne(_cCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Status, Is.EqualTo("active"));
		Assert.That(byId[1].Right2, Is.Not.Null);
		Assert.That(byId[1].Right2!.Score, Is.EqualTo(100));
		Assert.That(byId[2].Right2!.Score, Is.EqualTo(200));
	}

	[Test]
	public void JoinOne_Chained_TwoLevels_PartialMatches() {
		_aCache.AddOrUpdate(1, new ChainA { Id = 1, Name = "one" });
		_aCache.AddOrUpdate(2, new ChainA { Id = 2, Name = "two" });
		_aCache.AddOrUpdate(3, new ChainA { Id = 3, Name = "three" });

		_bCache.AddOrUpdate(1, new ChainB { Id = 1, Status = "active" });
		// 2, 3 missing in B
		_cCache.AddOrUpdate(1, new ChainC { Id = 1, Score = 100 });
		_cCache.AddOrUpdate(3, new ChainC { Id = 3, Score = 300 });
		// 2 missing in C

		var results = _aCache.Query()
			.JoinOne(_bCache)
			.JoinOne(_cCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right2, Is.Not.Null);
		Assert.That(byId[2].Right, Is.Null, "A id=2 has no B");
		Assert.That(byId[2].Right2, Is.Null, "A id=2 has no C");
		Assert.That(byId[3].Right, Is.Null, "A id=3 has no B");
		Assert.That(byId[3].Right2, Is.Not.Null);
		Assert.That(byId[3].Right2!.Score, Is.EqualTo(300));
	}

	[Test]
	public void InnerJoinOne_Chained_TwoLevels_DropsLeftsMissingInEither() {
		_aCache.AddOrUpdate(1, new ChainA { Id = 1, Name = "one" });
		_aCache.AddOrUpdate(2, new ChainA { Id = 2, Name = "two" });
		_aCache.AddOrUpdate(3, new ChainA { Id = 3, Name = "three" });

		_bCache.AddOrUpdate(1, new ChainB { Id = 1, Status = "active" });
		_bCache.AddOrUpdate(3, new ChainB { Id = 3, Status = "active" });
		// 2 missing — inner-join drops it on first hop

		_cCache.AddOrUpdate(1, new ChainC { Id = 1, Score = 100 });
		// 3 missing — inner-join drops it on second hop

		var results = _aCache.Query()
			.InnerJoinOne(_bCache)
			.InnerJoinOne(_cCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
		Assert.That(results[0].Right!.Status, Is.EqualTo("active"));
		Assert.That(results[0].Right2!.Score, Is.EqualTo(100));
	}

	[Test]
	public void JoinOne_Chained_OuterThenInner_NullSecondDropsLeft() {
		_aCache.AddOrUpdate(1, new ChainA { Id = 1, Name = "one" });
		_aCache.AddOrUpdate(2, new ChainA { Id = 2, Name = "two" });

		_bCache.AddOrUpdate(1, new ChainB { Id = 1, Status = "active" });
		// 2 missing in B → outer keeps it with Right=null

		_cCache.AddOrUpdate(1, new ChainC { Id = 1, Score = 100 });
		// 2 missing in C → inner drops it

		var results = _aCache.Query()
			.JoinOne(_bCache)
			.InnerJoinOne(_cCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
	}

	[Test]
	public void JoinOne_Chained_WithFilter_OnSecondHop() {
		_aCache.AddOrUpdate(1, new ChainA { Id = 1, Name = "one" });
		_aCache.AddOrUpdate(2, new ChainA { Id = 2, Name = "two" });

		_bCache.AddOrUpdate(1, new ChainB { Id = 1, Status = "active" });
		_bCache.AddOrUpdate(2, new ChainB { Id = 2, Status = "active" });

		_cCache.AddOrUpdate(1, new ChainC { Id = 1, Score = 100 });
		_cCache.AddOrUpdate(2, new ChainC { Id = 2, Score = 200 });

		// Filter on the C join: filter is a no-op (identity) — proves the with-filter
		// overload is reachable at chained level.
		var results = _aCache.Query()
			.JoinOne(_bCache)
			.JoinOne(_cCache, static q => q)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right2!.Score, Is.EqualTo(100));
		Assert.That(byId[2].Right2!.Score, Is.EqualTo(200));
	}

	[Test]
	public void InnerJoinOne_Chained_PredicateFilter_DropsRejectedMatches() {
		// All three lefts have B and C entries, but the predicate filter on C
		// rejects Score >= 200. Only id=1 should survive.
		_aCache.AddOrUpdate(1, new ChainA { Id = 1, Name = "one" });
		_aCache.AddOrUpdate(2, new ChainA { Id = 2, Name = "two" });
		_aCache.AddOrUpdate(3, new ChainA { Id = 3, Name = "three" });

		_bCache.AddOrUpdate(1, new ChainB { Id = 1, Status = "active" });
		_bCache.AddOrUpdate(2, new ChainB { Id = 2, Status = "active" });
		_bCache.AddOrUpdate(3, new ChainB { Id = 3, Status = "active" });

		_cCache.AddOrUpdate(1, new ChainC { Id = 1, Score = 100 });
		_cCache.AddOrUpdate(2, new ChainC { Id = 2, Score = 200 });
		_cCache.AddOrUpdate(3, new ChainC { Id = 3, Score = 300 });

		var results = _aCache.Query()
			.InnerJoinOne(_bCache)
			.InnerJoinOne(_cCache, static q => q.Where(c => c.Score < 200))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
		Assert.That(results[0].Right2!.Score, Is.EqualTo(100));
	}
}
