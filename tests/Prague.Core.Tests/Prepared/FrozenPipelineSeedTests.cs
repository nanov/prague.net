namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using Prague.Core.TypeSystem;
using static PreparedQueryDifferentialTests;
using EagerItems = CacheQueryBuilderCombined<Prague.Core.TypeSystem.ExecutableQuery<InMemoryDataCache<int, PreparedQueryDifferentialTests.PqItem>>,
	CacheQueryBuilderCoreCombined<int, PreparedQueryDifferentialTests.PqItem>, int, PreparedQueryDifferentialTests.PqItem,
	Resolvers<BaseResolver<int, PreparedQueryDifferentialTests.PqItem>>, PreparedQueryDifferentialTests.PqItem>;

// BuildFrozen() stage 3, step 3: seed selection. Free mode is the default — the step with the smallest
// cardinality signal seeds and the rest are probed per candidate, so the rows come out in that step's
// order, which the ordering contract leaves unspecified for an unsorted result and for the ties of a
// sorted one. Fixed mode (FrozenOptions.PreserveEagerOrder; a single active step either way) walks the
// first declared step and keeps the eager sequence. Count seeds free regardless. Pinned here: (a)
// byte-identical parity under the opt-out on every narrowing shape × every Execute variant × the five
// pages; (b) set + Count parity under the default free seed, classic Sort sequence parity with a total
// comparer and tie-group parity with ties; (c) the decision in Explain(); (d) rented arrays balanced
// incl. throwing user code mid-walk on the pool path; (f) 8 readers against a writer on the seed path.
// Model: 240 items, Code = 1000 + Id (unique), Group = Id % 7 (~34 per bucket), Band = Id % 12 (20),
// Tier = Id % 40 (6), Tags = [Group, Group + 100] (a collection index: overlapping buckets),
// Flag = Id % 3 == 0 (80 keys).
[TestFixture]
public class FrozenPipelineSeedTests {
	private const int N = 240;

	private enum Variant { Execute, ExecuteCloned, ExecutePooled, ExecutePooledCloned }

	private static readonly Variant[] Variants = [Variant.Execute, Variant.ExecuteCloned, Variant.ExecutePooled, Variant.ExecutePooledCloned];
	private static readonly (int skip, int take)[] Pages = [(0, int.MaxValue), (0, 1), (1, int.MaxValue), (0, 0), (5, 5)];

	// (a) asserts the eager sequence, which only the opt-out promises; (b) onwards run the default.
	private static readonly FrozenOptions EagerOrder = new() { PreserveEagerOrder = true };

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byBand = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byTier = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byTags = null!;
	private CacheRangeIndex<int, PqItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqItem> _flagged = null!;
	private LastUpdatedIndex<int> _lastUpdated = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byBand = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 12);
		_byTier = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 40);
		_byTags = _cache.CacheCollectionKeyValueListIndex<int>(static (_, v) => [v.Group, v.Group + 100]);
		_codeRange = _cache.CacheRangeIndex<int>(static (_, v) => v.Code);
		_flagged = _cache.AddKeySetIndex(static (_, v) => v.Flag);
		_lastUpdated = new LastUpdatedIndex<int>();
		_cache.CacheLastUpdatedIndex(_lastUpdated, static (id, _) => id);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i), 1_000_000L + i * 1000L);
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	// ── Helpers ───────────────────────────────────────────────────────────────────

	private static QueryResults<PqItem> Run<TArgs>(PreparedQuery<TArgs, PqItem> q, in TArgs args, Variant v, int skip, int take) => v switch {
		Variant.Execute => q.Execute(in args, skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(in args, skip, take),
		Variant.ExecutePooled => q.ExecutePooled(in args, skip, take),
		_ => q.ExecutePooledCloned(in args, skip, take),
	};

	private static QueryResults<PqItem> RunEager(EagerItems q, Variant v, int skip, int take) => v switch {
		Variant.Execute => q.Execute(skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(skip, take),
		Variant.ExecutePooled => q.ExecutePooled(skip, take),
		_ => q.ExecutePooledCloned(skip, take),
	};

	private static bool IsClone(Variant v) => v is Variant.ExecuteCloned or Variant.ExecutePooledCloned;

	private static int[] Ids(QueryResults<PqItem> r) {
		try {
			var ids = new int[r.Count];
			for (var i = 0; i < r.Count; i++) ids[i] = r[i].Id;
			return ids;
		} finally {
			r.Dispose();
		}
	}

	private void AssertSameAndIdentity(QueryResults<PqItem> eager, QueryResults<PqItem> frozen, bool clone, string label) {
		try {
			Assert.Multiple(() => {
				Assert.That(frozen.Count, Is.EqualTo(eager.Count), label + " Count");
				Assert.That(frozen.TotalCount, Is.EqualTo(eager.TotalCount), label + " TotalCount");
				Assert.That(frozen.Truncated, Is.False, label + " Truncated");
			});
			for (var i = 0; i < eager.Count; i++) {
				Assert.That(frozen[i].Id, Is.EqualTo(eager[i].Id), label + " row " + i);
				Assert.That(_cache.TryGet(frozen[i].Id, out var cached), Is.True);
				if (clone) {
					Assert.That(ReferenceEquals(frozen[i], cached), Is.False, label + " clone must be a fresh instance");
					Assert.That(frozen[i].CacheEquals(cached), Is.True, label + " clone must equal the cached value");
				} else {
					Assert.That(ReferenceEquals(frozen[i], cached), Is.True, label + " non-clone must be the cached instance");
				}
			}
		} finally {
			eager.Dispose();
			frozen.Dispose();
		}
	}

	/// <summary>(a): the pipeline, every variant × page byte-identical to eager (with identity) and to prepared; Count.</summary>
	private void AssertSequence<TArgs>(Func<EagerItems> eager, PreparedQuery<TArgs, PqItem> prepared, FrozenQuery<TArgs, PqItem> frozen, TArgs args, string label) {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"), label);
		foreach (var v in Variants)
			foreach (var (skip, take) in Pages) {
				var tag = $"{label} {v} skip={skip} take={take}";
				AssertSameAndIdentity(RunEager(eager(), v, skip, take), Run(frozen, in args, v, skip, take), IsClone(v), tag);
				AssertSame(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take));
			}

		var expected = eager().Count();
		Assert.That(frozen.Count(in args), Is.EqualTo(expected), label + " Count");
		Assert.That(prepared.Count(in args), Is.EqualTo(expected), label + " prepared Count");
	}

	private static QueryResults<PqItem> RunSortedEager<TComparer>(CacheQueryBuilderCombined<SortedQuery<ExecutableQuery<InMemoryDataCache<int, PqItem>>>, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, Resolvers<SortResolver<int, PqItem, PqItem, TComparer>>, PqItem> q, Variant v, int skip, int take) where TComparer : IComparer<PqItem> => v switch {
		Variant.Execute => q.Execute(skip, take),
		Variant.ExecuteCloned => q.ExecuteCloned(skip, take),
		Variant.ExecutePooled => q.ExecutePooled(skip, take),
		_ => q.ExecutePooledCloned(skip, take),
	};

	/// <summary>(a) for a classic Sort: the sorted eager builder is its own closed type.</summary>
	private void AssertSortedSequence<TArgs, TComparer>(Func<CacheQueryBuilderCombined<SortedQuery<ExecutableQuery<InMemoryDataCache<int, PqItem>>>, CacheQueryBuilderCoreCombined<int, PqItem>, int, PqItem, Resolvers<SortResolver<int, PqItem, PqItem, TComparer>>, PqItem>> eager, PreparedQuery<TArgs, PqItem> prepared, FrozenQuery<TArgs, PqItem> frozen, TArgs args, string label)
		where TComparer : IComparer<PqItem> {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"), label);
		foreach (var v in Variants)
			foreach (var (skip, take) in Pages) {
				var tag = $"{label} {v} skip={skip} take={take}";
				AssertSameAndIdentity(RunSortedEager(eager(), v, skip, take), Run(frozen, in args, v, skip, take), IsClone(v), tag);
				AssertSame(Run(prepared, in args, v, skip, take), Run(frozen, in args, v, skip, take));
			}

		var expected = eager().Count();
		Assert.That(frozen.Count(in args), Is.EqualTo(expected), label + " Count");
		Assert.That(prepared.Count(in args), Is.EqualTo(expected), label + " prepared Count");
	}

	/// <summary>(b): the free seed — same set, same TotalCount, pages partition the whole, Count.</summary>
	private static void AssertSet<TArgs>(Func<EagerItems> eager, FrozenQuery<TArgs, PqItem> frozen, TArgs args, string label) {
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"), label);
		var expected = Ids(eager().Execute());
		var all = Ids(frozen.Execute(in args));
		Assert.That(all.OrderBy(static x => x), Is.EqualTo(expected.OrderBy(static x => x)).AsCollection, label + " set");
		Assert.That(frozen.Count(in args), Is.EqualTo(expected.Length), label + " Count");
		foreach (var v in Variants) {
			using var full = Run(frozen, in args, v, 0, int.MaxValue);
			Assert.That(full.Count, Is.EqualTo(expected.Length), label + " " + v);
			var pages = new List<int>();
			for (var skip = 0; skip < expected.Length + 3; skip += 3) {
				using var page = Run(frozen, in args, v, skip, 3);
				Assert.That(page.TotalCount, Is.EqualTo(expected.Length), label + " TotalCount " + v);
				for (var i = 0; i < page.Count; i++) pages.Add(page[i].Id);
			}

			Assert.That(pages, Is.EqualTo(all).AsCollection, label + " pages partition the whole " + v);
		}
	}

	// The plan records a decision (and its signals) only when the decision changes, so the signals shown
	// are those of the execution that last changed it: a fresh plan pins one execution's decision exactly.
	private static string Decision<TArgs>(FrozenQuery<TArgs, PqItem> fresh, TArgs args, bool count = false) {
		if (count)
			fresh.Count(in args);
		else
			fresh.Execute(in args).Dispose();
		return Last(fresh.Explain());
	}

	private static string Last(string explain) {
		var at = explain.IndexOf("last seed:", StringComparison.Ordinal);
		return at < 0 ? "" : explain[at..].TrimEnd();
	}

	// ── (a) PreserveEagerOrder: byte-identical parity on every narrowing shape ──────

	[Test]
	public void ListUnique_EagerOrder_EveryArgSet() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).BuildFrozen(EagerOrder);
		foreach (var (g, c) in new[] { (3, 1010), (3, 1011), (0, 1000), (-1, 1000), (3, -1), (6, 1237), (6, 1238) })
			AssertSequence(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byCode, c), prepared, frozen, (g, c), $"list∩unique {g}/{c}");
		Assert.That(Decision(Build(), (3, 1010)), Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"));
		Assert.That(Decision(Build(), (3, -1)), Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"), "a missing unique key: the walk yields nothing");
		Assert.That(Decision(Build(), (0, 1000)), Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"));
		FrozenQuery<(int group, int code), PqItem> Build() => _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).BuildFrozen(EagerOrder);
	}

	[Test]
	public void ListList_SmallSecond_AndReversed_EveryArgSet() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(EagerOrder);
		var reversedPrepared = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).Build();
		var reversed = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen(EagerOrder);
		for (var g = -1; g < 8; g++)
			for (var t = -1; t < 41; t += 3) {
				AssertSequence(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t), prepared, frozen, (g, t), $"list∩list {g}/{t}");
				AssertSequence(() => _cache.Query().UseIndex(_byTier, t).UseIndex(_byGroup, g), reversedPrepared, reversed, (g, t), $"reversed {g}/{t}");
			}

		Assert.That(Decision(_cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(EagerOrder), (3, 3)),
			Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"));
		Assert.That(Decision(_cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen(EagerOrder), (3, 3)),
			Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"), "the opt-out walks the first declared step whatever the bucket sizes");
	}

	// Production shape A's narrowing: three list steps, the largest declared first, the smallest last.
	[Test]
	public void ListListList_SmallestLast_EveryArgSet() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).Build();
		var frozen = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).BuildFrozen(EagerOrder);
		var bandFirst = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byBand, static a => a.band).UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(EagerOrder);
		foreach (var (g, b, t) in new[] { (3, 3, 3), (3, 3, 23), (0, 0, 0), (6, 6, 6), (1, 5, 21), (-1, 3, 3), (3, -1, 3), (3, 3, -1), (2, 2, 2), (5, 5, 25) }) {
			AssertSequence(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, b).UseIndex(_byTier, t), prepared, frozen, (g, b, t), $"list∩list∩list {g}/{b}/{t}");
			AssertSequence(() => _cache.Query().UseIndex(_byBand, b).UseIndex(_byGroup, g).UseIndex(_byTier, t),
				_cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byBand, static a => a.band).UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Build(), bandFirst, (g, b, t), $"band first {g}/{b}/{t}");
		}

		Assert.That(Decision(_cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).BuildFrozen(EagerOrder), (3, 3, 3)),
			Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"));
	}

	[Test]
	public void KeySetFirst_ThenList_ThenUnique_EagerOrder() {
		var list = _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byGroup, static g => g).BuildFrozen(EagerOrder);
		var tier = _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byTier, static t => t).Where(static v => v.Id > 10).BuildFrozen(EagerOrder);
		var unique = _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byCode, static c => c).BuildFrozen(EagerOrder);
		for (var g = -1; g < 7; g++) {
			AssertSequence(() => _cache.Query().UseIndex(_flagged).UseIndex(_byGroup, g), _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byGroup, static x => x).Build(), list, g, "keyset∩list " + g);
			AssertSequence(() => _cache.Query().UseIndex(_flagged).UseIndex(_byTier, g).Where(static v => v.Id > 10), _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byTier, static x => x).Where(static v => v.Id > 10).Build(), tier, g, "keyset∩tier " + g);
			AssertSequence(() => _cache.Query().UseIndex(_flagged).UseIndex(_byCode, 1000 + g * 3), _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byCode, static x => x).Build(), unique, 1000 + g * 3, "keyset∩unique " + g);
		}

		Assert.That(Decision(_cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byGroup, static g => g).BuildFrozen(EagerOrder), 3), Is.EqualTo("last seed: step 0 KeySet (signal 0), fixed: first active step"));
	}

	// A collection-backed index: a bucket is still one PooledSet (slot-addressable), an In over its
	// buckets overlaps and must dedupe first-occurrence-first (fixed seed: ListIn is not slot-addressable).
	[Test]
	public void CollectionBackedFirst_OverlappingBuckets_EqAndIn() {
		var eq = _cache.Prepare<int, PqItem, (int tag, int tier)>().UseIndex(_byTags, static a => a.tag).UseIndex(_byTier, static a => a.tier).BuildFrozen(EagerOrder);
		var eqPrepared = _cache.Prepare<int, PqItem, (int tag, int tier)>().UseIndex(_byTags, static a => a.tag).UseIndex(_byTier, static a => a.tier).Build();
		var inn = _cache.Prepare<int, PqItem, int>().UseIndex(_byTags, new[] { 3, 103, 5, 3 }).UseIndex(_byTier, static t => t).BuildFrozen(EagerOrder);
		var innPrepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byTags, new[] { 3, 103, 5, 3 }).UseIndex(_byTier, static t => t).Build();
		var inUnique = _cache.Prepare<int, PqItem, int>().UseIndex(_byTags, new[] { 103, 5 }).UseIndex(_byCode, static c => c).BuildFrozen(EagerOrder);
		foreach (var (tag, t) in new[] { (3, 3), (103, 3), (103, 10), (5, 5), (-1, 3), (3, -1), (104, 24) }) {
			AssertSequence(() => _cache.Query().UseIndex(_byTags, tag).UseIndex(_byTier, t), eqPrepared, eq, (tag, t), $"tags∩tier {tag}/{t}");
			AssertSequence(() => _cache.Query().UseIndex(_byTags, new[] { 3, 103, 5, 3 }).UseIndex(_byTier, t), innPrepared, inn, t, "tags in∩tier " + t);
			AssertSequence(() => _cache.Query().UseIndex(_byTags, new[] { 103, 5 }).UseIndex(_byCode, 1000 + t), _cache.Prepare<int, PqItem, int>().UseIndex(_byTags, new[] { 103, 5 }).UseIndex(_byCode, static c => c).Build(), inUnique, 1000 + t, "tags in∩unique " + t);
		}

		Assert.That(Decision(_cache.Prepare<int, PqItem, (int tag, int tier)>().UseIndex(_byTags, static a => a.tag).UseIndex(_byTier, static a => a.tier).BuildFrozen(EagerOrder), (103, 3)),
			Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"));
		Assert.That(Decision(_cache.Prepare<int, PqItem, int>().UseIndex(_byTags, new[] { 3, 103, 5, 3 }).UseIndex(_byTier, static t => t).BuildFrozen(EagerOrder), 3),
			Is.EqualTo("last seed: step 0 ListIn (signal 0), fixed: first active step"), "an In over buckets seeds the same way: the first declared step");
	}

	// The small side can be a multi-value step (dedupe), and a range / last-updated step never is (its
	// signal is an estimate) — it stays a probe on the survivors.
	[Test]
	public void ListWithUniqueIn_ListIn_RangeAndUnique_Filters() {
		var uniqueIn = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, 3).UseIndex(_byCode, static a => a).BuildFrozen(EagerOrder);
		var uniqueInPrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, 3).UseIndex(_byCode, static a => a).Build();
		var listIn = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, 3).UseIndex(_byTier, static a => a).BuildFrozen(EagerOrder);
		var listInPrepared = _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, 3).UseIndex(_byTier, static a => a).Build();
		foreach (var span in new int[][] { [1003, 1010, 1017], [1003, 1010, 1017, 1024, 1031, 1038, 1045, 1052, 1059, 1066, 1073, 1080, 1087, 1094, 1101, 1108, 1115, 1122], [1000, 1003, 1003], [-1], [] }) {
			ReadOnlyMemory<int> memory = span;
			AssertSequence(() => _cache.Query().UseIndex(_byGroup, 3).UseIndex(_byCode, span), uniqueInPrepared, uniqueIn, memory, "list∩unique in " + span.Length);
		}

		foreach (var span in new int[][] { [3, 9], [3, 9, 3, 15], [3, 9, 15, 21, 27, 33, 39], [-1], [] }) {
			ReadOnlyMemory<int> memory = span;
			AssertSequence(() => _cache.Query().UseIndex(_byGroup, 3).UseIndex(_byTier, span), listInPrepared, listIn, memory, "list∩list in " + span.Length);
		}

		Assert.That(Decision(BuildUniqueIn(), new[] { 1003, 1010, 1017 }), Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"));
		Assert.That(Decision(BuildUniqueIn(), new[] { 1003, 1010, 1017, 1024, 1031, 1038, 1045, 1052, 1059, 1066, 1073, 1080, 1087, 1094, 1101, 1108, 1115, 1122 }),
			Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"), "a wider In does not move the fixed seed either");
		Assert.That(Decision(_cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, 3).UseIndex(_byTier, static a => a).BuildFrozen(EagerOrder), new[] { 3, 9 }),
			Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"));
		FrozenQuery<ReadOnlyMemory<int>, PqItem> BuildUniqueIn() => _cache.Prepare<int, PqItem, ReadOnlyMemory<int>>().UseIndex(_byGroup, 3).UseIndex(_byCode, static a => a).BuildFrozen(EagerOrder);

		var rangeUnique = _cache.Prepare<int, PqItem, (int group, int lo, int hi, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).UseIndex(_byCode, static a => a.code).BuildFrozen(EagerOrder);
		var rangeUniquePrepared = _cache.Prepare<int, PqItem, (int group, int lo, int hi, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).UseIndex(_byCode, static a => a.code).Build();
		foreach (var args in new[] { (3, 1000, 1100, 1010), (3, 1000, 1100, 1150), (3, 1100, 1000, 1010), (-1, 1000, 1100, 1010) })
			AssertSequence(() => _cache.Query().UseIndex(_byGroup, args.Item1).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), (lo: args.Item2, hi: args.Item3)).UseIndex(_byCode, args.Item4), rangeUniquePrepared, rangeUnique, args, "list∩range∩unique " + args);
		Assert.That(Decision(_cache.Prepare<int, PqItem, (int group, int lo, int hi, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).UseIndex(_byCode, static a => a.code).BuildFrozen(EagerOrder), (3, 1000, 1100, 1010)),
			Is.EqualTo("last seed: step 0 ListEq (signal 0), fixed: first active step"));

		var filtered = _cache.Prepare<int, PqItem, (int group, int tier, int min)>().UseIndex(_byGroup, static a => a.group).Where(static v => v.Flag).UseIndex(_byTier, static a => a.tier).Where(static (v, a) => v.Id >= a.min).UseIndex(_lastUpdated, 0L).BuildFrozen(EagerOrder);
		var filteredPrepared = _cache.Prepare<int, PqItem, (int group, int tier, int min)>().UseIndex(_byGroup, static a => a.group).Where(static v => v.Flag).UseIndex(_byTier, static a => a.tier).Where(static (v, a) => v.Id >= a.min).UseIndex(_lastUpdated, 0L).Build();
		foreach (var (g, t, min) in new[] { (3, 3, 0), (3, 3, 100), (0, 0, 0), (6, 6, 300) })
			AssertSequence(() => _cache.Query().UseIndex(_byGroup, g).Where(static v => v.Flag).UseIndex(_byTier, t).Where(v => v.Id >= min).UseIndex(_lastUpdated, 0L), filteredPrepared, filtered, (g, t, min), $"filters {g}/{t}/{min}");
	}

	[Test]
	public void NotApplicable_RangeFirst_LastUpdatedFirst_UniqueFirst_KeepTheFixedWalk() {
		var rangeFirst = _cache.Prepare<int, PqItem, int>().UseIndex(_codeRange, static rb => rb.Gte(1000).Lt(1100)).UseIndex(_byTier, static t => t).BuildFrozen(EagerOrder);
		var updatedFirst = _cache.Prepare<int, PqItem, int>().UseIndex(_lastUpdated, 1_000_000L).UseIndex(_byTier, static t => t).BuildFrozen(EagerOrder);
		var uniqueFirst = _cache.Prepare<int, PqItem, int>().UseIndex(_byCode, 1003).UseIndex(_byTier, static t => t).BuildFrozen(EagerOrder);
		for (var t = -1; t < 41; t += 5) {
			AssertSequence(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1000).Lt(1100)).UseIndex(_byTier, t), _cache.Prepare<int, PqItem, int>().UseIndex(_codeRange, static rb => rb.Gte(1000).Lt(1100)).UseIndex(_byTier, static x => x).Build(), rangeFirst, t, "range∩tier " + t);
			AssertSequence(() => _cache.Query().UseIndex(_lastUpdated, 1_000_000L).UseIndex(_byTier, t), _cache.Prepare<int, PqItem, int>().UseIndex(_lastUpdated, 1_000_000L).UseIndex(_byTier, static x => x).Build(), updatedFirst, t, "updated∩tier " + t);
			AssertSequence(() => _cache.Query().UseIndex(_byCode, 1003).UseIndex(_byTier, t), _cache.Prepare<int, PqItem, int>().UseIndex(_byCode, 1003).UseIndex(_byTier, static x => x).Build(), uniqueFirst, t, "unique∩tier " + t);
		}

		Assert.That(Decision(_cache.Prepare<int, PqItem, int>().UseIndex(_codeRange, static rb => rb.Gte(1000).Lt(1100)).UseIndex(_byTier, static t => t).BuildFrozen(EagerOrder), 3),
			Is.EqualTo("last seed: step 0 Range (signal 0), fixed: first active step"), "no signal is read: the fixed seed has nothing to choose between");
		Assert.That(Decision(_cache.Prepare<int, PqItem, int>().UseIndex(_lastUpdated, 1_000_000L).UseIndex(_byTier, static t => t).BuildFrozen(EagerOrder), 3),
			Is.EqualTo("last seed: step 0 LastUpdatedAfter (signal 0), fixed: first active step"));
		Assert.That(Decision(_cache.Prepare<int, PqItem, int>().UseIndex(_byCode, 1003).UseIndex(_byTier, static t => t).BuildFrozen(EagerOrder), 3),
			Is.EqualTo("last seed: step 0 UniqueEq (signal 0), fixed: first active step"));
	}

	// The free seed reads live counts: after the bucket sizes change, the decision follows — and the rows
	// stay eager's set throughout, in whatever order the winning step yields them.
	[Test]
	public void FreeSeed_FollowsLiveSignals_AndStaysExactAcrossMutations() {
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		AssertSameRows(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_byTier, 3).Execute(), frozen.Execute((3, 3)));
		Assert.That(Last(frozen.Explain()), Is.EqualTo("last seed: step 1 ListEq (signal 6), free: smallest signal"), "tier 3 (6 rows) beats group 3 (34)");
		var moved = new InMemoryDataCache<int, PqItem>();
		var byGroup = moved.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var byTier = moved.CacheKeyValueListIndex<int>(static (_, v) => v.Code % 40);
		for (var i = 0; i < N; i++)
			moved.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + (i % 2 == 0 ? 3 : i), Group = i % 7, Flag = false });
		var q = moved.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier).BuildFrozen();
		AssertSameRows(moved.Query().UseIndex(byGroup, 3).UseIndex(byTier, 3).Execute(), q.Execute((3, 3)));
		Assert.That(Last(q.Explain()), Is.EqualTo("last seed: step 0 ListEq (signal 34), free: smallest signal"), "tier 3 now holds 121 rows: the group bucket is the smaller one");
		// Shrink it again and the seed moves back; the rows stay eager's set throughout.
		for (var i = 0; i < N; i += 2)
			moved.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 7, Flag = false });
		AssertSameRows(moved.Query().UseIndex(byGroup, 3).UseIndex(byTier, 3).Execute(), q.Execute((3, 3)));
		Assert.That(Last(q.Explain()), Does.Contain("free: smallest signal").And.Contain("step 1"));
		moved.Remove(3);
		moved.Remove(10);
		AssertSameRows(moved.Query().UseIndex(byGroup, 3).UseIndex(byTier, 3).Execute(), q.Execute((3, 3)));
		moved.AddOrUpdate(3, new PqItem { Id = 3, Code = 1003, Group = 3, Flag = false });
		AssertSameRows(moved.Query().UseIndex(byGroup, 3).UseIndex(byTier, 3).Execute(), q.Execute((3, 3)));
	}

	// A seed past the 128-long stack buffer: the key buffer is rented and the walk still exact.
	[Test]
	public void FreeSeed_LargeSeed_PoolPath_LikeEager() {
		var big = new InMemoryDataCache<int, PqItem>();
		// group: 1500 rows, tier: 600 — the 600-key seed spills well past the stack buffer.
		var byGroup = big.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var byTier = big.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 5);
		for (var i = 0; i < 3000; i++)
			big.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 2, Flag = i % 3 == 0 });
		var frozen = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier).BuildFrozen();
		for (var g = 0; g < 2; g++)
			for (var t = 0; t < 5; t++) {
				AssertSameRows(big.Query().UseIndex(byGroup, g).UseIndex(byTier, t).Execute(), frozen.Execute((g, t)));
				Assert.That(frozen.Count((g, t)), Is.EqualTo(big.Query().UseIndex(byGroup, g).UseIndex(byTier, t).Count()), $"count {g}/{t}");
			}

		Assert.That(Decision(big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier).BuildFrozen(), (0, 1)),
			Is.EqualTo("last seed: step 1 ListEq (signal 600), free: smallest signal"));
		LeakAssert.Balanced(() => {
			frozen.ExecutePooled((0, 1)).Dispose();
			frozen.ExecutePooledCloned((1, 3), 3, 9).Dispose();
			frozen.Count((1, 2));
		});
	}

	// ── (b) The default free seed: Execute, Count, classic Sort ────────────────────

	[Test]
	public void FreeSeed_SameSetAndCount_OnEveryShape_RowOrderFollowsTheSeed() {
		var listList = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		var listUnique = _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).BuildFrozen();
		var listKeySet = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).UseIndex(_flagged).BuildFrozen();
		var keySetTier = _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byTier, static t => t).BuildFrozen();
		var listRange = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Where(static v => v.Id > 2).BuildFrozen();
		var rangeList = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).UseIndex(_byGroup, static a => a.group).BuildFrozen();
		var three = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		var updated = _cache.Prepare<int, PqItem, (int group, long after)>().UseIndex(_byGroup, static a => a.group).UseIndex(_lastUpdated, static a => a.after).BuildFrozen();
		for (var g = -1; g < 7; g++) {
			for (var t = -1; t < 41; t += 4)
				AssertSet(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t), listList, (g, t), $"free seed list∩list {g}/{t}");
			AssertSet(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byCode, 1000 + g * 7), listUnique, (g, 1000 + g * 7), "free seed list∩unique " + g);
			AssertSet(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byCode, -1), listUnique, (g, -1), "free seed list∩missing unique " + g);
			AssertSet(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_flagged), listKeySet, g, "free seed list∩keyset " + g);
			AssertSet(() => _cache.Query().UseIndex(_flagged).UseIndex(_byTier, g), keySetTier, g, "free seed keyset∩tier " + g);
			AssertSet(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_codeRange, static rb => rb.Gte(1020).Lt(1030)).Where(static v => v.Id > 2), listRange, (g, 1020, 1030), "free seed list∩narrow range " + g);
			AssertSet(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_codeRange, static rb => rb.Gte(1000).Lt(1200)).Where(static v => v.Id > 2), listRange, (g, 1000, 1200), "free seed list∩wide range " + g);
			AssertSet(() => _cache.Query().UseIndex(_codeRange, static rb => rb.Gte(1000).Lt(1200)).UseIndex(_byGroup, g), rangeList, (g, 1000, 1200), "free seed range∩list " + g);
			AssertSet(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, g + 2).UseIndex(_byTier, g + 5), three, (g, g + 2, g + 5), "free seed three " + g);
			AssertSet(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_lastUpdated, 1_000_000L + 200 * 1000L), updated, (g, 1_000_000L + 200 * 1000L), "free seed list∩updated " + g);
		}

		// The order follows the seeding source: the tier bucket, i.e. the reversed spelling's eager order.
		AssertSame(_cache.Query().UseIndex(_byTier, 3).UseIndex(_byGroup, 3).Execute(), listList.Execute((3, 3)));
		Assert.That(Decision(BuildListList(), (3, 3)), Is.EqualTo("last seed: step 1 ListEq (signal 6), free: smallest signal"));
		Assert.That(Decision(BuildListUnique(), (3, 1010)), Is.EqualTo("last seed: step 1 UniqueEq (signal 1), free: smallest signal"));
		Assert.That(Decision(BuildListRange(), (3, 1020, 1030)), Does.StartWith("last seed: step 1 Range (signal ").And.EndWith("), free: smallest signal"), "a narrow window's estimate beats the bucket twice over");
		Assert.That(Decision(BuildListRange(), (3, 1000, 1200)), Is.EqualTo("last seed: step 0 ListEq (signal 34), free: smallest signal"), "a wide window's estimate does not");
		Assert.That(Decision(BuildListUnique(), (3, -1)), Is.EqualTo("last seed: step 1 UniqueEq (signal 0), free: smallest signal"), "an exact zero: no walk at all");
		FrozenQuery<(int group, int tier), PqItem> BuildListList() => _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		FrozenQuery<(int group, int code), PqItem> BuildListUnique() => _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).BuildFrozen();
		FrozenQuery<(int group, int lo, int hi), PqItem> BuildListRange() => _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Where(static v => v.Id > 2).BuildFrozen();
	}

	[Test]
	public void Count_SeedsFree_OnEveryShape_LikeEager() {
		var listList = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		var listRange = _cache.Prepare<int, PqItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		var keySet = _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byGroup, static g => g).Where(static v => v.Id % 2 == 0).BuildFrozen();
		var three = _cache.Prepare<int, PqItem, (int group, int band, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		for (var g = -1; g < 7; g++)
			for (var t = -1; t < 41; t += 4) {
				Assert.That(listList.Count((g, t)), Is.EqualTo(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).Count()), $"count list∩list {g}/{t}");
				Assert.That(listRange.Count((g, 1000 + t, 1000 + t + 30)), Is.EqualTo(_cache.Query().UseIndex(_byGroup, g).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), (lo: 1000 + t, hi: 1000 + t + 30)).Count()), $"count list∩range {g}/{t}");
				Assert.That(keySet.Count(g), Is.EqualTo(_cache.Query().UseIndex(_flagged).UseIndex(_byGroup, g).Where(static v => v.Id % 2 == 0).Count()), "count keyset∩list " + g);
				Assert.That(three.Count((g, t % 12, t)), Is.EqualTo(_cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, t % 12).UseIndex(_byTier, t).Count()), $"count three {g}/{t}");
			}

		Assert.That(Decision(_cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(), (3, 3), count: true),
			Is.EqualTo("last seed: step 1 ListEq (signal 6), free: smallest signal"));
		Assert.That(listList.Explain(), Does.Contain("pipeline: seed = free for Execute, free for Count"));
	}

	private readonly struct ByCode : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Code ?? 0).CompareTo(y?.Code ?? 0);
	}

	private sealed class ByCodeDesc : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (y?.Code ?? 0).CompareTo(x?.Code ?? 0);
	}

	// Two tie groups over any list ∩ list result.
	private readonly struct ByFlag : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Flag ?? false).CompareTo(y?.Flag ?? false);
	}

	[Test]
	public void Sort_TotalComparer_ByteIdentical_OnEveryShapeVariantAndPage() {
		var listList = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Sort(new ByCode()).BuildFrozen();
		var listListPrepared = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Sort(new ByCode()).Build();
		var listDesc = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => !v.Flag).Sort(new ByCodeDesc()).BuildFrozen();
		var listDescPrepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => !v.Flag).Sort(new ByCodeDesc()).Build();
		var range = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi)).Sort(new ByCodeDesc()).BuildFrozen();
		var rangePrepared = _cache.Prepare<int, PqItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi)).Sort(new ByCodeDesc()).Build();
		var keySetList = _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byGroup, static g => g).Sort(new ByCode()).BuildFrozen();
		var keySetListPrepared = _cache.Prepare<int, PqItem, int>().UseIndex(_flagged).UseIndex(_byGroup, static g => g).Sort(new ByCode()).Build();
		Assert.That(listList.Plan.IsSorted, Is.True);
		Assert.That(listList.Explain(), Does.Contain("pipeline: seed = free for Execute"));
		for (var g = -1; g < 7; g++) {
			for (var t = -1; t < 41; t += 5)
				AssertSortedSequence(() => _cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).Sort(new ByCode()), listListPrepared, listList, (g, t), $"sort list∩list {g}/{t}");
			AssertSortedSequence(() => _cache.Query().UseIndex(_byGroup, g).Where(static v => !v.Flag).Sort(new ByCodeDesc()), listDescPrepared, listDesc, g, "sort list desc " + g);
			AssertSortedSequence(() => _cache.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lte(a.hi), (lo: 1000 + g * 20, hi: 1040 + g * 20)).Sort(new ByCodeDesc()), rangePrepared, range, (1000 + g * 20, 1040 + g * 20), "sort range " + g);
			AssertSortedSequence(() => _cache.Query().UseIndex(_flagged).UseIndex(_byGroup, g).Sort(new ByCode()), keySetListPrepared, keySetList, g, "sort keyset∩list " + g);
		}

		Assert.That(Decision(_cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Sort(new ByCode()).BuildFrozen(), (3, 3)),
			Is.EqualTo("last seed: step 1 ListEq (signal 6), free: smallest signal"));
	}

	// A tying comparer: StableSort keeps comparer-equal rows in encounter order, which the free seed
	// changes — every tie group holds the same rows as eager's, the sequence inside it may differ, and
	// the pipeline's own pages partition its own full sequence.
	[Test]
	public void Sort_TieComparer_SameRowsPerTieGroup_PagesPartition() {
		var frozen = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Sort(new ByFlag()).BuildFrozen();
		var wide = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.tier).Sort(new ByFlag()).BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		for (var g = 0; g < 7; g++)
			for (var t = 0; t < 40; t += 3) {
				foreach (var (q, eager) in new[] {
					(frozen, _cache.Query().UseIndex(_byGroup, g).UseIndex(_byTier, t).Sort(new ByFlag())),
					(wide, _cache.Query().UseIndex(_byGroup, g).UseIndex(_byBand, t).Sort(new ByFlag())),
				}) {
					using var expected = eager.Execute();
					using var all = q.Execute((g, t));
					Assert.That(all.Count, Is.EqualTo(expected.Count));
					// Sorted by the tie key both ways; within a tie group the same multiset.
					for (var i = 1; i < all.Count; i++)
						Assert.That(all[i - 1].Flag.CompareTo(all[i].Flag), Is.LessThanOrEqualTo(0), "sorted");
					Assert.That(all.Where(static v => !v.Flag).Select(static v => v.Id).OrderBy(static x => x), Is.EqualTo(expected.Where(static v => !v.Flag).Select(static v => v.Id).OrderBy(static x => x)).AsCollection);
					Assert.That(all.Where(static v => v.Flag).Select(static v => v.Id).OrderBy(static x => x), Is.EqualTo(expected.Where(static v => v.Flag).Select(static v => v.Id).OrderBy(static x => x)).AsCollection);
					var pages = new List<int>();
					for (var skip = 0; skip < all.Count + 2; skip += 2) {
						using var page = q.ExecutePooled((g, t), skip, 2);
						Assert.That(page.TotalCount, Is.EqualTo(all.Count));
						for (var i = 0; i < page.Count; i++) pages.Add(page[i].Id);
					}

					Assert.That(pages, Is.EqualTo(all.Select(static v => v.Id)).AsCollection, "pages partition the full sequence");
				}
			}

		// A single active step is not moved: the sequence is eager's even with ties.
		var alone = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Sort(new ByFlag()).BuildFrozen();
		for (var g = 0; g < 7; g++)
			AssertSame(_cache.Query().UseIndex(_byGroup, g).Sort(new ByFlag()).Execute(), alone.Execute(g));
	}

	[Test]
	public void Sort_ClassicAndBounded_RouteToThePipeline() {
		Assert.Multiple(() => {
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Sort(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Where(static v => v.Flag).Sort(new ByCodeDesc()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).SortBounded(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "step 6: the bounded feed");
			Assert.That(_cache.Prepare().UseIndex(_byGroup, 3).Sort(new ByCode()).BuildFrozen(new FrozenOptions { Pipeline = false }).Plan.Executor, Is.EqualTo("Replay"));
			Assert.That(_cache.Prepare().Sort(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Replay"), "filter-only / all rows sorted: replay");
			Assert.That(_cache.Prepare().Or(b => b.UseIndex(_byGroup, 1), b => b.UseIndex(_byGroup, 2)).Sort(new ByCode()).BuildFrozen().Plan.Executor, Is.EqualTo("Pipeline"), "composite (step 4)");
		});
		// SortBounded parity (step 6, FrozenPipelineSortBoundedTests in depth): the two spellings agree with eager on every page.
		var bounded = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new ByCode()).BuildFrozen();
		for (var g = 0; g < 7; g++)
			foreach (var (skip, take) in Pages)
				AssertSame(_cache.Query().UseIndex(_byGroup, g).SortBounded(new ByCode()).Execute(skip, take), bounded.Execute(g, skip, take));
	}

	// ── (d) Leaks ─────────────────────────────────────────────────────────────────

	[Test]
	public void Throwing_Selector_Predicate_Comparer_Clone_LeaveNoRentedArrays_OnEverySeedPath() {
		var big = new InMemoryDataCache<int, PqItem>();
		var byGroup = big.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var byTier = big.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 5);
		var byCode = big.AddKeyValueIndex<int>(static (_, v) => v.Code);
		for (var i = 0; i < 3000; i++)
			big.AddOrUpdate(i, new PqItem { Id = i, Code = 1000 + i, Group = i % 2, Flag = i % 3 == 0 });
		// The second step's selector throws at bind, before any seed; the small step's own walk is fine.
		var selector = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier < 0 ? throw new InvalidOperationException("boom") : a.tier).BuildFrozen();
		// A predicate that throws mid-walk after a pool-path seed (600 keys, well past the stack buffer).
		var predicate = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier)
			.Where(static (v, a) => v.Id > 2000 && a.group == 0 ? throw new InvalidOperationException("boom") : true).BuildFrozen();
		var predicateFree = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier)
			.Where(static (v, a) => v.Id > 2000 && a.group == 0 ? throw new InvalidOperationException("boom") : true).BuildFrozen();
		var comparer = big.Prepare<int, PqItem, (int group, int tier)>().UseIndex(byGroup, static a => a.group).UseIndex(byTier, static a => a.tier).Sort(new Bomb()).BuildFrozen();
		var uniqueBomb = big.Prepare<int, PqItem, (int group, int code)>().UseIndex(byGroup, static a => a.group).UseIndex(byCode, static a => a.code < 0 ? throw new InvalidOperationException("boom") : a.code).BuildFrozen();
		Assert.That(comparer.Plan.Executor, Is.EqualTo("Pipeline"));
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => selector.ExecutePooled((0, -1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => selector.Count((0, -1)));
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooled((0, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.ExecutePooledCloned((0, 1), 5, 5).Dispose());
			Assert.Throws<InvalidOperationException>(() => predicate.Count((0, 1)));
			Assert.Throws<InvalidOperationException>(() => predicateFree.ExecutePooled((0, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooled((0, 1)).Dispose());
			Assert.Throws<InvalidOperationException>(() => comparer.ExecutePooledCloned((0, 1), 2, 3).Dispose());
			Assert.Throws<InvalidOperationException>(() => uniqueBomb.ExecutePooled((0, -1)).Dispose());
			predicate.ExecutePooled((1, 1)).Dispose();
			comparer.ExecutePooled((1, 3)).Dispose();
		});
		AssertSame(big.Query().UseIndex(byGroup, 0).UseIndex(byTier, 1).Execute(), selector.Execute((0, 1)));

		// A throwing Clone on the sorted, pooled, cloned path: the container's buffer goes back.
		var bombs = new InMemoryDataCache<int, PqBomb>();
		var bombGroup = bombs.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		var bombTier = bombs.CacheKeyValueListIndex<int>(static (_, v) => v.Id % 4);
		for (var i = 0; i < 100; i++)
			bombs.AddOrUpdate(i, new PqBomb { Id = i, Group = i % 3 });
		var cloned = bombs.Prepare().UseIndex(bombGroup, 0).UseIndex(bombTier, 2).Sort(new ByBombId()).BuildFrozen();
		Assert.That(cloned.Plan.Executor, Is.EqualTo("Pipeline"));
		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => cloned.ExecutePooledCloned().Dispose());
			Assert.Throws<InvalidOperationException>(() => cloned.ExecuteCloned(0, 8).Dispose());
			cloned.ExecutePooled().Dispose();
		});
	}

	private readonly struct Bomb : IComparer<PqItem> {
		public int Compare(PqItem? x, PqItem? y) => (x?.Group ?? 0) == 0 ? throw new InvalidOperationException("compare boom") : (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
	}

	private sealed class PqBomb : ICacheEquatable<PqBomb>, ICacheClonable<PqBomb> {
		public int Id { get; init; }
		public int Group { get; init; }
		public bool CacheEquals(PqBomb? other) => other is not null && other.Id == Id && other.Group == Group;
		public int CacheGetHashCode() => HashCode.Combine(Id, Group);
		public PqBomb Clone() => Id == 30 ? throw new InvalidOperationException("clone boom") : new PqBomb { Id = Id, Group = Group };
	}

	private readonly struct ByBombId : IComparer<PqBomb> {
		public int Compare(PqBomb? x, PqBomb? y) => (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
	}

	// ── (f) Concurrency on the seed path ──────────────────────────────────────────

	[Test]
	public void FreeSeed_EightReaders_AgainstAWriter_NeverThrow_NoDuplicates_ValueJudgedSeedStep() {
		var listList = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		var listUnique = _cache.Prepare<int, PqItem, (int group, int code)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byCode, static a => a.code).BuildFrozen();
		var sorted = _cache.Prepare<int, PqItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Sort(new ByCode()).BuildFrozen();
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => {
			var i = 0;
			while (!stop.IsCancellationRequested) {
				var id = i++ % N;
				// Moves rows across groups and tiers (the tier is Id % 40 — the id itself moves), removes and re-adds.
				_cache.AddOrUpdate(id, new PqItem { Id = id, Code = 1000 + id, Group = (id + i) % 7, Flag = i % 5 == 0 });
				if (i % 11 == 0) _cache.Remove((id * 13) % N);
				if (i % 11 == 5) _cache.AddOrUpdate((id * 13) % N, Make((id * 13) % N));
			}
		});

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				var seen = new HashSet<int>();
				for (var i = 0; i < 2_000; i++) {
					var group = (seed + i) % 7;
					var tier = (seed * 5 + i) % 40;
					using (var rows = listList.ExecutePooled((group, tier))) {
						seen.Clear();
						for (var r = 0; r < rows.Count; r++) {
							Assert.That(rows[r].Id % 40, Is.EqualTo(tier), "the seeding step is value-judged");
							Assert.That(seen.Add(rows[r].Id), Is.True, "no duplicate keys");
						}
					}

					using (var rows = listUnique.ExecutePooledCloned((group, 1000 + (i * 7) % N)))
						Assert.That(rows.Count, Is.LessThanOrEqualTo(1));
					using (var rows = sorted.ExecutePooled((group, tier), 1, 3))
						Assert.That(rows.Count, Is.LessThanOrEqualTo(rows.TotalCount));
					Assert.That(listList.Count((group, tier)), Is.GreaterThanOrEqualTo(0));
				}
			});
		}

		Task.WaitAll(readers);
		stop.Cancel();
		writer.Wait();
	}
}
