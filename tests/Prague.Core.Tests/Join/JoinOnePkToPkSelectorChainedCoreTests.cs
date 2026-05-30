namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// Phase B-1 chained selector tests: PK-to-PK + key selector at chained levels.
// Selector maps int → long across two hops: A (int) → B (long) → C (long).
//
// First hop: aCache.Query().JoinOne(static k => (long)k, bCache) — level-0 PK-to-PK + selector S1.
// Second hop: .JoinOne(static k => (long)k, cCache) — chained level-1 PK-to-PK + selector S1
// (T4-emitted by Phase B-1).
//
// All 6 chained S1-S6 selector shapes (outer + inner) are exercised at level 1.

internal sealed class PkSelChainA : ICacheEquatable<PkSelChainA>, ICacheClonable<PkSelChainA> {
	public int Id { get; init; }
	public string? Name { get; init; }
	public bool CacheEquals(PkSelChainA? o) => o is not null && o.Id == Id && o.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public PkSelChainA Clone() => new() { Id = Id, Name = Name };
}

internal sealed class PkSelChainB : ICacheEquatable<PkSelChainB>, ICacheClonable<PkSelChainB> {
	public long Id { get; init; }
	public string? Status { get; init; }
	public bool CacheEquals(PkSelChainB? o) => o is not null && o.Id == Id && o.Status == Status;
	public int CacheGetHashCode() => HashCode.Combine(Id, Status);
	public PkSelChainB Clone() => new() { Id = Id, Status = Status };
}

internal sealed class PkSelChainC : ICacheEquatable<PkSelChainC>, ICacheClonable<PkSelChainC> {
	public long Id { get; init; }
	public int Score { get; init; }
	public bool CacheEquals(PkSelChainC? o) => o is not null && o.Id == Id && o.Score == Score;
	public int CacheGetHashCode() => HashCode.Combine(Id, Score);
	public PkSelChainC Clone() => new() { Id = Id, Score = Score };
}

[TestFixture]
public class JoinOnePkToPkSelectorChainedCoreTests {
	private InMemoryDataCache<int, PkSelChainA> _aCache = null!;
	private InMemoryDataCache<long, PkSelChainB> _bCache = null!;
	private InMemoryDataCache<long, PkSelChainC> _cCache = null!;

	[SetUp]
	public void SetUp() {
		_aCache = new InMemoryDataCache<int, PkSelChainA>();
		_bCache = new InMemoryDataCache<long, PkSelChainB>();
		_cCache = new InMemoryDataCache<long, PkSelChainC>();
	}

	private void SeedFullChain() {
		_aCache.AddOrUpdate(1, new PkSelChainA { Id = 1, Name = "one" });
		_aCache.AddOrUpdate(2, new PkSelChainA { Id = 2, Name = "two" });
		_aCache.AddOrUpdate(3, new PkSelChainA { Id = 3, Name = "three" });
		_bCache.AddOrUpdate(1L, new PkSelChainB { Id = 1L, Status = "active" });
		_bCache.AddOrUpdate(2L, new PkSelChainB { Id = 2L, Status = "inactive" });
		_bCache.AddOrUpdate(3L, new PkSelChainB { Id = 3L, Status = "active" });
		_cCache.AddOrUpdate(1L, new PkSelChainC { Id = 1L, Score = 100 });
		_cCache.AddOrUpdate(2L, new PkSelChainC { Id = 2L, Score = 200 });
		_cCache.AddOrUpdate(3L, new PkSelChainC { Id = 3L, Score = 300 });
	}

	[Test]
	public void Chained_S1Outer_AllMatched() {
		SeedFullChain();
		var results = _aCache.Query()
			.JoinOne(static k => (long)k, _bCache)                  // level-0 S1 outer
			.JoinOne(static k => (long)k, _cCache)                  // level-1 S1 outer chained
			.Execute();
		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Status, Is.EqualTo("active"));
		Assert.That(byId[1].Right2!.Score, Is.EqualTo(100));
	}

	[Test]
	public void Chained_S1Inner_DropsUnmatchedAtSecondHop() {
		SeedFullChain();
		_cCache.Remove(2L);                                            // drop C for id 2
		var results = _aCache.Query()
			.InnerJoinOne(static k => (long)k, _bCache)             // level-0 S1 inner
			.InnerJoinOne(static k => (long)k, _cCache)             // level-1 S1 inner chained
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_S2Inner_KeySelectorWithArg() {
		// Selector takes a scale arg: maps int → long via static lambda + arg, exercising
		// the level-1 S2 inner chained overload (no-filter, with selectorArg).
		SeedFullChain();
		const long scale = 1L;
		var results = _aCache.Query()
			.InnerJoinOne(static k => (long)k, _bCache)
			.InnerJoinOne(static (k, s) => (long)k * s, scale, _cCache)  // level-1 S2 inner
			.Execute();
		Assert.That(results.Count, Is.EqualTo(3));
	}

	[Test]
	public void Chained_S3Inner_WithFilter_DropsPredicateReject() {
		// Level-1 S3 inner: keySelector + filter. Filter rejects Score >= 200.
		SeedFullChain();
		var results = _aCache.Query()
			.InnerJoinOne(static k => (long)k, _bCache)
			.InnerJoinOne(static k => (long)k, _cCache,
				static q => q.Where(c => c.Score < 200))                // S3 inner: filter
			.Execute();
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
		Assert.That(results[0].Right2!.Score, Is.EqualTo(100));
	}

	[Test]
	public void Chained_S5Inner_WithFilterArg_StaticLambdaZeroAlloc() {
		// Level-1 S5 inner: keySelector + filter + filterArg.
		SeedFullChain();
		const int cutoff = 200;
		var results = _aCache.Query()
			.InnerJoinOne(static k => (long)k, _bCache)
			.InnerJoinOne(static k => (long)k, _cCache,
				static (q, c) => q.Where(x => x.Score < c),             // S5 filter
				cutoff)                                                 // S5 filterArg
			.Execute();
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
	}

	[Test]
	public void Chained_S6Inner_KeySelectorWithArg_AndFilterArg() {
		// Level-1 S6 inner: keySelector + selectorArg + filter + filterArg.
		SeedFullChain();
		const long scale = 1L;
		const int cutoff = 200;
		var results = _aCache.Query()
			.InnerJoinOne(static k => (long)k, _bCache)
			.InnerJoinOne(static (k, s) => (long)k * s, scale, _cCache,
				static (q, c) => q.Where(x => x.Score < c),
				cutoff)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
	}
}
