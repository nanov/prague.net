namespace Prague.Core.Tests.Prepared;

using System.Diagnostics;
using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using static PreparedQueryDifferentialTests;
using EagerItems = CacheQueryBuilderCombined<Prague.Core.TypeSystem.ExecutableQuery<InMemoryDataCache<int, PreparedQueryDifferentialTests.PqItem>>,
	CacheQueryBuilderCoreCombined<int, PreparedQueryDifferentialTests.PqItem>, int, PreparedQueryDifferentialTests.PqItem,
	Resolvers<BaseResolver<int, PreparedQueryDifferentialTests.PqItem>>, PreparedQueryDifferentialTests.PqItem>;

// Optional-bounds range narrowing (`UseIndex(range, from, to, fromInclusive, toInclusive)`), eager vs
// prepared from the same inputs. The eager type-state builder cannot say "from and/or to" in one
// lambda, so the eager twin is four C# `if` arms over Gte / Gt / Lte / Lt and the two-sided forms;
// the both-null arm never calls the range index at all — which is what the prepared step must
// reproduce, because the eager core has no dispatch arm for an unbounded range.
[TestFixture]
public class PreparedQueryOptionalRangeDifferentialTests {
	private const int N = 240;

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheRangeIndex<int, PqItem, string> _codeTextRange = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_codeTextRange = _cache.CacheRangeIndex<string>(static (_, v) => v.Code.ToString("D6"));
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i));
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	// The eager spelling of an optional range: one type-state chain per bound combination.
	private static EagerItems EagerRange(EagerItems q, CacheRangeIndex<int, PqItem, int> index, int? from, int? to, bool fromInclusive, bool toInclusive) {
		if (from is null && to is null) return q;
		if (to is null) return fromInclusive ? q.UseIndex(index, (rb, f) => rb.Gte(f), from!.Value) : q.UseIndex(index, (rb, f) => rb.Gt(f), from!.Value);
		if (from is null) return toInclusive ? q.UseIndex(index, (rb, t) => rb.Lte(t), to.Value) : q.UseIndex(index, (rb, t) => rb.Lt(t), to.Value);
		var b = (from: from.Value, to: to.Value);
		return (fromInclusive, toInclusive) switch {
			(true, true) => q.UseIndex(index, static (rb, b) => rb.Gte(b.from).Lte(b.to), b),
			(true, false) => q.UseIndex(index, static (rb, b) => rb.Gte(b.from).Lt(b.to), b),
			(false, true) => q.UseIndex(index, static (rb, b) => rb.Gt(b.from).Lte(b.to), b),
			(false, false) => q.UseIndex(index, static (rb, b) => rb.Gt(b.from).Lt(b.to), b),
		};
	}

	private static void AssertParity(Func<EagerItems> eager, Func<QueryResults<PqItem>> rows, Func<int> count) {
		AssertSame(eager().Execute(), rows());
		Assert.That(count(), Is.EqualTo(eager().Count()), "Count()");
	}

	private static readonly (int? from, int? to)[] Bounds = [(1050, 1100), (1050, null), (null, 1100), (null, null), (1100, 1050), (1239, 1239), (5000, null), (null, 0)];

	// ── The four bound combinations × the four inclusivity flags ──────────────────

	[TestCase(true, true)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(false, false)]
	public void Parameterized_EveryBoundCombination_LikeEagerIfArms(bool fromInclusive, bool toInclusive) {
		var prepared = _cache.Prepare<int, PqItem, (int? from, int? to)>()
			.UseIndex(_codeRange, static a => a.from, static a => a.to, fromInclusive, toInclusive)
			.Build();

		foreach (var (from, to) in Bounds) {
			var args = (from, to);
			AssertParity(() => EagerRange(_cache.Query(), _codeRange, from, to, fromInclusive, toInclusive), () => prepared.Execute(args), () => prepared.Count(args));
		}

		using var rows = prepared.Execute((1050, 1100));
		Assert.That(rows.Count, Is.EqualTo(51 - (fromInclusive ? 0 : 1) - (toInclusive ? 0 : 1)));
	}

	[TestCase(true, true)]
	[TestCase(false, false)]
	public void Bound_EveryBoundCombination_LikeEagerIfArms(bool fromInclusive, bool toInclusive) {
		foreach (var (from, to) in Bounds) {
			var prepared = _cache.Prepare().UseIndex(_codeRange, from, to, fromInclusive, toInclusive).Build();
			AssertParity(() => EagerRange(_cache.Query(), _codeRange, from, to, fromInclusive, toInclusive), () => prepared.Execute(), () => prepared.Count());
		}
	}

	// ── Both null ─────────────────────────────────────────────────────────────────

	// Pinned: the eager core has no arm for an unbounded range — handed (None, None) it throws
	// UnreachableException on the plain path as well as the intersecter path — so the prepared step
	// skips the core call and is the eager query that never called the range (`_first` untouched).
	[Test]
	public void BothNull_TheEagerCoreRejectsAnUnboundedRange_SoThePreparedStepIsNoNarrowingAtAll() {
		var unbounded = new OptionalRange<int>(default, default);
		Assert.That(unbounded.IsUnbounded, Is.True);
		Assert.Throws<UnreachableException>(() => _cache.Query().UseIndex(_codeRange, static (_, r) => r, unbounded).Execute().Dispose());
		Assert.Throws<UnreachableException>(() => _cache.Query().Or(b => b.UseIndex(_codeRange, static (_, r) => r, unbounded), b => b.UseIndex(_byGroup, 1)).Execute().Dispose());

		var parameterized = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_codeRange, static a => a.from, static a => a.to).Build();
		var bound = _cache.Prepare().UseIndex(_codeRange, (int?)null, null).Build();
		AssertSame(_cache.Query().Execute(), parameterized.Execute((null, null)));
		AssertSame(_cache.Query().Execute(), bound.Execute());
		Assert.That(parameterized.Count((null, null)), Is.EqualTo(N));

		// As the first narrower, both-null lets the next UseIndex seed rather than intersect with nothing.
		var thenGroup = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_codeRange, static a => a.from, static a => a.to).UseIndex(_byGroup, 3).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).Execute(), thenGroup.Execute((null, null)));
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_codeRange, static rb => rb.Gte(1100)).Execute(), thenGroup.Execute((1100, null)));

		// Inside an Or branch, both-null is the eager no-op branch and drops out of the union.
		var inOr = _cache.Prepare<int, PqItem, (int? from, int? to)>().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_codeRange, static a => a.from, static a => a.to)).Build();
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 1), b => b).Execute(), inOr.Execute((null, null)));
		AssertSame(_cache.Query().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_codeRange, static rb => rb.Lte(1010))).Execute(), inOr.Execute((null, 1010)));
	}

	// ── Placement ─────────────────────────────────────────────────────────────────

	[TestCase(1050, 1150)]
	[TestCase(1050, null)]
	[TestCase(null, 1150)]
	[TestCase(null, null)]
	public void AfterListIndex_BeforeWhere_LikeEager(int? from, int? to) {
		var prepared = _cache.Prepare<int, PqItem, (int? from, int? to)>()
			.UseIndex(_byGroup, 2)
			.UseIndex(_codeRange, static a => a.from, static a => a.to, toInclusive: false)
			.Where(static v => v.Flag)
			.Build();
		var args = (from, to);

		AssertParity(() => EagerRange(_cache.Query().UseIndex(_byGroup, 2), _codeRange, from, to, true, false).Where(static v => v.Flag), () => prepared.Execute(args), () => prepared.Count(args));

		using var rows = prepared.Execute(args);
		Assert.That(rows.Count, Is.GreaterThan(0));
		for (var i = 0; i < rows.Count; i++) {
			Assert.That(rows[i].Group == 2 && rows[i].Flag);
			if (from is not null) Assert.That(rows[i].Code, Is.GreaterThanOrEqualTo(from.Value));
			if (to is not null) Assert.That(rows[i].Code, Is.LessThan(to.Value));
		}
	}

	[Test]
	public void Parameterized_ReusedAcrossSixArgSets_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_codeRange, static a => a.from, static a => a.to, fromInclusive: false).Build();
		foreach (var args in new (int? from, int? to)[] { (1000, 1010), (null, 1005), (1230, null), (null, null), (1100, 1100), (2000, 3000) })
			AssertParity(() => EagerRange(_cache.Query(), _codeRange, args.from, args.to, false, true), () => prepared.Execute(args), () => prepared.Count(args));
	}

	[TestCase(true, 1050, null)]
	[TestCase(true, null, null)]
	[TestCase(false, 1050, 1060)]
	public void InsideIf_AndInsideMatchArm_LikeEager(bool cond, int? from, int? to) {
		var inIf = _cache.Prepare<int, PqItem, (bool cond, int? from, int? to)>()
			.UseIndex(_byGroup, 4)
			.If(static a => a.cond, b => b.UseIndex(_codeRange, static a => a.from, static a => a.to))
			.Build();
		var inMatch = _cache.Prepare<int, PqItem, (bool cond, int? from, int? to)>()
			.UseIndex(_byGroup, 4)
			.Match(static a => a.cond ? 1 : 0, m => m
				.Case(1, b => b.UseIndex(_codeRange, static a => a.from, static a => a.to))
				.Case(0, b => b.UseIndex(_codeRange, 1100, null, fromInclusive: false)))
			.Build();
		var args = (cond, from, to);

		EagerItems EagerIf() {
			var q = _cache.Query().UseIndex(_byGroup, 4);
			return cond ? EagerRange(q, _codeRange, from, to, true, true) : q;
		}

		EagerItems EagerMatch() {
			var q = _cache.Query().UseIndex(_byGroup, 4);
			return cond ? EagerRange(q, _codeRange, from, to, true, true) : q.UseIndex(_codeRange, static rb => rb.Gt(1100));
		}

		AssertParity(EagerIf, () => inIf.Execute(args), () => inIf.Count(args));
		AssertParity(EagerMatch, () => inMatch.Execute(args), () => inMatch.Count(args));
	}

	// ── Reference-type keys ───────────────────────────────────────────────────────

	[TestCase("001050", "001100")]
	[TestCase("001050", null)]
	[TestCase(null, "001100")]
	[TestCase(null, null)]
	public void ReferenceTypeKey_Parameterized_AndBound_LikeEager(string? from, string? to) {
		var parameterized = _cache.Prepare<int, PqItem, (string? from, string? to)>().UseIndex(_codeTextRange, static a => a.from, static a => a.to, toInclusive: false).Build();
		var bound = _cache.Prepare().UseIndex(_codeTextRange, from, to, toInclusive: false).Build();

		EagerItems Eager() {
			var q = _cache.Query();
			if (from is null && to is null) return q;
			if (to is null) return q.UseIndex(_codeTextRange, (rb, f) => rb.Gte(f), from!);
			if (from is null) return q.UseIndex(_codeTextRange, (rb, t) => rb.Lt(t), to);
			return q.UseIndex(_codeTextRange, static (rb, b) => rb.Gte(b.from).Lt(b.to), (from, to));
		}

		AssertParity(Eager, () => parameterized.Execute((from, to)), () => parameterized.Count((from, to)));
		AssertParity(Eager, () => bound.Execute(), () => bound.Count());
	}

	// ── Leaks and plan ────────────────────────────────────────────────────────────

	[Test]
	public void Pooled_EveryBoundCombination_LeavesNoRentedArrays() {
		var prepared = _cache.Prepare<int, PqItem, (int? from, int? to)>()
			.UseIndex(_byGroup, 2)
			.UseIndex(_codeRange, static a => a.from, static a => a.to)
			.Where(static (v, a) => v.Id >= (a.from ?? 0) - 1000)
			.Build();
		var throwing = _cache.Prepare<int, PqItem, (int? from, int? to)>()
			.UseIndex(_byGroup, 2)
			.UseIndex(_codeRange, static a => a.from < 0 ? throw new InvalidOperationException("from") : a.from, static a => a.to)
			.Build();

		LeakAssert.Balanced(() => {
			foreach (var (from, to) in Bounds) {
				prepared.ExecutePooled((from, to)).Dispose();
				_ = prepared.Count((from, to));
			}

			prepared.ExecutePooledCloned((1050, 1200), 2, 5).Dispose();
			Assert.Throws<InvalidOperationException>(() => throwing.ExecutePooled((-1, null)).Dispose());
			Assert.Throws<InvalidOperationException>(() => throwing.Count((-1, null)));
		});
	}

	[Test]
	public void Frozen_OptionalRange_ReplaysAndDescribesARangeStep() {
		var parameterized = _cache.Prepare<int, PqItem, (int? from, int? to)>().UseIndex(_codeRange, static a => a.from, static a => a.to).BuildFrozen();
		var bound = _cache.Prepare().UseIndex(_codeRange, 1050, null, fromInclusive: false).BuildFrozen();
		var unbounded = _cache.Prepare().UseIndex(_codeRange, (int?)null, null).BuildFrozen();

		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1050).Lte(1100)).Execute(), parameterized.Execute((1050, 1100)));
		AssertSame(_cache.Query().UseIndex(_codeRange, static rb => rb.Gt(1050)).Execute(), bound.Execute());
		AssertSame(_cache.Query().Execute(), unbounded.Execute());

		Assert.Multiple(() => {
			Assert.That(parameterized.Plan.Executor, Is.EqualTo("Replay"));
			Assert.That(parameterized.Plan.Narrowers[0].Kind, Is.EqualTo(NarrowerKind.Range));
			Assert.That(parameterized.Plan.Narrowers[0].IsParameterized, Is.True);
			Assert.That(parameterized.Plan.Narrowers[0].Index, Is.SameAs(_codeRange));
			Assert.That(bound.Plan.Narrowers[0].IsParameterized, Is.False);
			Assert.That(bound.Plan.Narrowers[0].Value?.ToString(), Is.EqualTo("(1050, +inf)"));
			Assert.That(unbounded.Plan.Narrowers[0].Value?.ToString(), Is.EqualTo("(-inf, +inf)"));
			Assert.That(bound.Explain(), Does.Contain("Range (bound)").And.Contain("value=(1050, +inf)"));
		});
	}
}
