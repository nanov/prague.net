namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// Hand-rolled POCOs — no [DataCache], no codegen. Demonstrates that the
// JoinOne family works against a raw InMemoryDataCache<TKey, TValue> via
// IDataCache<InMemoryDataCache<TKey, TValue>, TKey, TValue>.

internal sealed class A : ICacheEquatable<A>, ICacheClonable<A> {
	public int Id { get; init; }
	public string? Name { get; init; }

	public bool CacheEquals(A? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public A Clone() => new() { Id = Id, Name = Name };
}

internal sealed class B : ICacheEquatable<B>, ICacheClonable<B> {
	public int Id { get; init; }
	public string? Status { get; init; }

	public bool CacheEquals(B? other) => other is not null && other.Id == Id && other.Status == Status;
	public int CacheGetHashCode() => HashCode.Combine(Id, Status);
	public B Clone() => new() { Id = Id, Status = Status };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinOneCoreTests {
	private InMemoryDataCache<int, A> _aCache = null!;
	private InMemoryDataCache<int, B> _bCache = null!;

	[SetUp]
	public void SetUp() {
		_aCache = new InMemoryDataCache<int, A>();
		_bCache = new InMemoryDataCache<int, B>();
	}

	// ── PK-to-PK: full match ─────────────────────────────────────────────────

	[Test]
	public void JoinOne_PkToPk_MatchExists_AttachesRight() {
		_aCache.AddOrUpdate(1, new A { Id = 1, Name = "alpha" });
		_aCache.AddOrUpdate(2, new A { Id = 2, Name = "beta" });
		_aCache.AddOrUpdate(3, new A { Id = 3, Name = "gamma" });

		_bCache.AddOrUpdate(1, new B { Id = 1, Status = "active" });
		_bCache.AddOrUpdate(2, new B { Id = 2, Status = "inactive" });
		// 3 intentionally missing in B

		var results = _aCache.Query()
			.JoinOne(_bCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Status, Is.EqualTo("active"));
		Assert.That(byId[2].Right, Is.Not.Null);
		Assert.That(byId[2].Right!.Status, Is.EqualTo("inactive"));
		Assert.That(byId[3].Right, Is.Null, "A id=3 has no B match → null right");
	}

	// ── PK-to-PK: no match → null right ──────────────────────────────────────

	[Test]
	public void JoinOne_PkToPk_NoMatch_ReturnsNullRight() {
		_aCache.AddOrUpdate(10, new A { Id = 10, Name = "lonely" });

		var results = _aCache.Query()
			.JoinOne(_bCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right, Is.Null);
	}

	// ── PK-to-PK with filter (Where predicate, no codegen WithXxx) ───────────

	[Test]
	public void JoinOne_PkToPk_WithWhereFilter_NarrowsRight() {
		_aCache.AddOrUpdate(1, new A { Id = 1, Name = "alpha" });
		_aCache.AddOrUpdate(2, new A { Id = 2, Name = "beta" });

		_bCache.AddOrUpdate(1, new B { Id = 1, Status = "active" });
		_bCache.AddOrUpdate(2, new B { Id = 2, Status = "inactive" });

		var results = _aCache.Query()
			.JoinOne(_bCache, q => q.Where(b => b.Status == "active"))
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Status, Is.EqualTo("active"));
		Assert.That(byId[2].Right, Is.Null, "B id=2 filtered out by Where");
	}

	// ── PK-to-PK with selector + filter+arg (zero-alloc static lambdas) ──────

	[Test]
	public void JoinOne_PkToPk_WithKeySelectorAndFilterArg() {
		_aCache.AddOrUpdate(1, new A { Id = 1, Name = "alpha" });
		_aCache.AddOrUpdate(2, new A { Id = 2, Name = "beta" });

		_bCache.AddOrUpdate(101, new B { Id = 101, Status = "active" });    // matches A.Id=1 with offset 100
		_bCache.AddOrUpdate(102, new B { Id = 102, Status = "inactive" });  // matches A.Id=2

		// Selector transforms left key with +100 offset.
		// Filter narrows right cache to active rows; arg is passed zero-alloc via static lambda.
		const string targetStatus = "active";
		const int offset = 100;
		var results = _aCache.Query()
			.JoinOne(
				static (int k, int off) => k + off, offset,
				_bCache,
				static (q, s) => q.Where(b => b.Status == s), targetStatus)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Id, Is.EqualTo(101));
		Assert.That(byId[1].Right!.Status, Is.EqualTo("active"));
		Assert.That(byId[2].Right, Is.Null, "B id=102 filtered out (inactive)");
	}

	// ── UseIndex chained with JoinOne ─────────────────────────────────────

	[Test]
	public void JoinOne_PkToPk_WithLeftKeyIndexNarrow() {
		_aCache.AddOrUpdate(1, new A { Id = 1, Name = "alpha" });
		_aCache.AddOrUpdate(2, new A { Id = 2, Name = "beta" });
		_aCache.AddOrUpdate(3, new A { Id = 3, Name = "gamma" });

		_bCache.AddOrUpdate(1, new B { Id = 1, Status = "active" });
		_bCache.AddOrUpdate(2, new B { Id = 2, Status = "active" });
		_bCache.AddOrUpdate(3, new B { Id = 3, Status = "active" });

		// Left-side narrow: only A id=1,2 considered. Then join.
		var results = _aCache.Query()
			.UseIndex(_aCache.KeyIndex, new[] { 1, 2 })
			.JoinOne(_bCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
		Assert.That(results.All(r => r.Right is not null), Is.True);
	}

	// ── InnerJoinOne: drops unmatched lefts (vs. outer null-attach) ──────

	[Test]
	public void InnerJoinOne_PkToPk_DropsUnmatched() {
		_aCache.AddOrUpdate(1, new A { Id = 1, Name = "alpha" });
		_aCache.AddOrUpdate(2, new A { Id = 2, Name = "beta" });
		_aCache.AddOrUpdate(3, new A { Id = 3, Name = "gamma" });

		_bCache.AddOrUpdate(1, new B { Id = 1, Status = "active" });
		_bCache.AddOrUpdate(2, new B { Id = 2, Status = "inactive" });
		// 3 intentionally missing in B → must be dropped, not null-attached.

		var results = _aCache.Query()
			.InnerJoinOne(_bCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2), "A id=3 has no B match — must be dropped (inner semantic)");
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
		Assert.That(results.All(r => r.Right is not null), Is.True, "every survivor must have a right value attached");
	}

	[Test]
	public void InnerJoinOne_PkToPk_AllMatched_KeepsAll() {
		_aCache.AddOrUpdate(1, new A { Id = 1, Name = "alpha" });
		_aCache.AddOrUpdate(2, new A { Id = 2, Name = "beta" });

		_bCache.AddOrUpdate(1, new B { Id = 1, Status = "x" });
		_bCache.AddOrUpdate(2, new B { Id = 2, Status = "y" });

		var results = _aCache.Query()
			.InnerJoinOne(_bCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.All(r => r.Right is not null), Is.True);
	}

	[Test]
	public void InnerJoinOne_PkToPk_NoneMatched_EmptyResult() {
		_aCache.AddOrUpdate(1, new A { Id = 1, Name = "alpha" });
		_aCache.AddOrUpdate(2, new A { Id = 2, Name = "beta" });
		// B cache empty → no left survives the inner narrow.

		var results = _aCache.Query()
			.InnerJoinOne(_bCache)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}
}
