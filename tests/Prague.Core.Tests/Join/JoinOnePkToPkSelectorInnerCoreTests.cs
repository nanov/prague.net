namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// One test per InnerJoinOne + selector shape (S1..S6) — cross-key-type
// (int → long) to exercise the keySelector path. POCO names are scoped to this
// file (PkSelInner*) to avoid collision with other Core.Tests Join files.

internal sealed class PkSelInnerA : ICacheEquatable<PkSelInnerA>, ICacheClonable<PkSelInnerA> {
	public int Id { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(PkSelInnerA? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public PkSelInnerA Clone() => new() { Id = Id, Name = Name };
}

internal sealed class PkSelInnerB : ICacheEquatable<PkSelInnerB>, ICacheClonable<PkSelInnerB> {
	public long Id { get; init; }
	public string Status { get; init; } = "";

	public bool CacheEquals(PkSelInnerB? other) => other is not null && other.Id == Id && other.Status == Status;
	public int CacheGetHashCode() => HashCode.Combine(Id, Status);
	public PkSelInnerB Clone() => new() { Id = Id, Status = Status };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOnePkToPkSelectorInnerCoreTests {
	private InMemoryDataCache<int, PkSelInnerA> _aCache = null!;
	private InMemoryDataCache<long, PkSelInnerB> _bCache = null!;

	[SetUp]
	public void SetUp() {
		_aCache = new InMemoryDataCache<int, PkSelInnerA>();
		_bCache = new InMemoryDataCache<long, PkSelInnerB>();
	}

	private void SeedThreeLeftsTwoRights() {
		_aCache.AddOrUpdate(1, new PkSelInnerA { Id = 1, Name = "alpha" });
		_aCache.AddOrUpdate(2, new PkSelInnerA { Id = 2, Name = "beta" });
		_aCache.AddOrUpdate(3, new PkSelInnerA { Id = 3, Name = "gamma" });

		// Selector: int lk → 1000L + lk. Only 1001 and 1002 exist on the right.
		_bCache.AddOrUpdate(1001L, new PkSelInnerB { Id = 1001L, Status = "active" });
		_bCache.AddOrUpdate(1002L, new PkSelInnerB { Id = 1002L, Status = "inactive" });
		// 1003 intentionally missing → A id=3 must be DROPPED.
	}

	// ── S1-Inner: keySelector, no filter ─────────────────────────────────────

	[Test]
	public void InnerJoinOne_S1_KeySelector_NoFilter_DropsUnmatched() {
		SeedThreeLeftsTwoRights();

		var results = _aCache.Query()
			.InnerJoinOne((int lk) => 1000L + lk, _bCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "A id=3 has no matching right → must drop");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
		Assert.That(results.All(r => r.Right is not null), Is.True);
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Status, Is.EqualTo("active"));
		Assert.That(byId[2].Right!.Status, Is.EqualTo("inactive"));
	}

	// ── S2-Inner: keySelector + selectorArg, no filter ───────────────────────

	[Test]
	public void InnerJoinOne_S2_KeySelectorWithArg_NoFilter_DropsUnmatched() {
		SeedThreeLeftsTwoRights();

		const long offset = 1000L;
		var results = _aCache.Query()
			.InnerJoinOne(static (int lk, long off) => off + lk, offset, _bCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
		Assert.That(results.All(r => r.Right is not null), Is.True);
	}

	// ── S3-Inner: keySelector, with filter ───────────────────────────────────

	[Test]
	public void InnerJoinOne_S3_KeySelector_WithFilter_DropsRejected() {
		SeedThreeLeftsTwoRights();

		// Filter narrows to Status == "active" → only A id=1 (whose right is 1001/active) survives.
		var results = _aCache.Query()
			.InnerJoinOne(
				(int lk) => 1000L + lk,
				_bCache,
				q => q.Where(b => b.Status == "active"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1), "Only A id=1 → B 1001/active survives filter; id=2 rejected, id=3 missing");
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
		Assert.That(results[0].Right, Is.Not.Null);
		Assert.That(results[0].Right!.Status, Is.EqualTo("active"));
	}

	// ── S4-Inner: keySelector + selectorArg, with filter ─────────────────────

	[Test]
	public void InnerJoinOne_S4_KeySelectorWithArg_WithFilter_DropsRejected() {
		SeedThreeLeftsTwoRights();

		const long offset = 1000L;
		var results = _aCache.Query()
			.InnerJoinOne(
				static (int lk, long off) => off + lk, offset,
				_bCache,
				q => q.Where(b => b.Status == "active"))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
		Assert.That(results[0].Right!.Status, Is.EqualTo("active"));
	}

	// ── S5-Inner: keySelector, with filter + filterArg ───────────────────────

	[Test]
	public void InnerJoinOne_S5_KeySelector_WithFilterArg_DropsRejected() {
		SeedThreeLeftsTwoRights();

		const string targetStatus = "active";
		var results = _aCache.Query()
			.InnerJoinOne(
				(int lk) => 1000L + lk,
				_bCache,
				static (q, s) => q.Where(b => b.Status == s), targetStatus)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
		Assert.That(results[0].Right!.Status, Is.EqualTo("active"));
	}

	// ── S6-Inner: keySelector + selectorArg, with filter + filterArg ─────────

	[Test]
	public void InnerJoinOne_S6_KeySelectorWithArg_WithFilterArg_DropsRejected() {
		SeedThreeLeftsTwoRights();

		const long offset = 1000L;
		const string targetStatus = "active";
		var results = _aCache.Query()
			.InnerJoinOne(
				static (int lk, long off) => off + lk, offset,
				_bCache,
				static (q, s) => q.Where(b => b.Status == s), targetStatus)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Left.Id, Is.EqualTo(1));
		Assert.That(results[0].Right!.Status, Is.EqualTo("active"));
	}
}
