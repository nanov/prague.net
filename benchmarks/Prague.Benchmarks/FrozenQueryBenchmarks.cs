namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Prague.Core;

/// <summary>
///   Eager builder (baseline) vs <c>Build()</c> (replay) vs <c>BuildFrozen()</c>, shape by shape, on
///   the raw <see cref="InMemoryDataCache{TKey,TValue}" />. The first four categories are the shapes
///   the frozen planner binds to the point-lookup executor; the simple unsorted list / range / key-set /
///   last-updated shapes bind to the stage-3 pipeline, as do <c>Sort</c> / <c>SortBounded</c>, the
///   <c>SortBounded</c> → <c>JoinOne</c> production shapes and (step 5) every chain of fusable
///   <c>JoinOne</c>s — outer, inner, chained; <c>Or</c> / <c>If</c> / <c>Match</c> (step 4); and (step 8) every chain with a
///   <c>JoinMany</c> — the narrowing is the pipeline pass, the fan-out runs after it over the rows the pass formed. A filtered
///   <c>JoinOne</c> replays, so its <c>_Frozen</c> row must sit on top of the <c>_Prepared</c> row. Every body executes pooled and
///   disposes. Same data as <see cref="PreparedQueryBenchmarks" />: 100k rows, list buckets of 1k, range windows of 1k codes; the
///   <c>JoinMany</c> right sides are lines by item (two per item, none for every fourth), three notes per customer shared by the
///   customer's 1k orders (the shared-right chain path) and a tag collection on 10k documents (M:N, a quarter untagged).
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FrozenQueryBenchmarks {
	private const int N = 100_000;
	private const int Buckets = 100;

	private InMemoryDataCache<int, PqbItem> _items = null!;
	private CacheUniqueIndex<int, PqbItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqbItem, int> _byGroup = null!;
	private CacheKeyValueListIndex<int, PqbItem, int> _byTier = null!;
	private CacheKeyValueListIndex<int, PqbItem, int> _byBand = null!;
	private CacheKeyValueListIndex<int, PqbItem, int> _byLane = null!;
	private CacheRangeIndex<int, PqbItem, int> _codeRange = null!;
	private CacheKeySetIndex<int, PqbItem> _flagged = null!;
	private LastUpdatedIndex<int> _itemsUpdated = null!;
	private InMemoryDataCache<int, PqbCustomer> _details = null!;

	// Production shape B: 10k records, timestamps spread over an hour, two skewed list keys, two FKs.
	private const int RecordCount = 10_000;
	private const long RecordBase = 1_700_000_000_000L;
	private const long Hour = 3_600_000L;
	private InMemoryDataCache<int, PqbRecord> _records = null!;
	private LastUpdatedIndex<int> _recordsUpdated = null!;
	private CacheRangeIndex<int, PqbRecord, long> _tsRange = null!;
	private CacheKeyValueListIndex<int, PqbRecord, int> _byKeyA = null!;
	private CacheKeyValueListIndex<int, PqbRecord, int> _byKeyB = null!;
	private CacheSymmetricKeyValueListIndex<int, PqbRecord, int> _recByCustomer = null!;
	private CacheSymmetricKeyValueListIndex<int, PqbRecord, int> _recByProduct = null!;
	private InMemoryDataCache<int, PqbCustomer> _recCustomers = null!;
	private InMemoryDataCache<int, PqbProduct> _recProducts = null!;

	private InMemoryDataCache<int, PqbOrder> _orders = null!;
	private CacheSymmetricKeyValueListIndex<int, PqbOrder, int> _byCustomer = null!;
	private InMemoryDataCache<int, PqbCustomer> _customers = null!;
	// PK-to-PK right side of the orders (Id == order id), present for three orders in four: the inner join drops a quarter of a bucket.
	private InMemoryDataCache<int, PqbCustomer> _orderDetails = null!;

	private PreparedQuery<NoArgs, PqbItem> _uniqueBoundPrepared = null!;
	private FrozenQuery<NoArgs, PqbItem> _uniqueBoundFrozen = null!;
	private FrozenQuery<int, PqbItem> _uniqueBoundIntArgsFrozen = null!;
	private PreparedQuery<int, PqbItem> _uniqueArgPrepared = null!;
	private FrozenQuery<int, PqbItem> _uniqueArgFrozen = null!;
	private PreparedQuery<int, PqbItem> _uniqueWherePrepared = null!;
	private FrozenQuery<int, PqbItem> _uniqueWhereFrozen = null!;
	private PreparedQuery<(int code, int min), PqbItem> _uniqueArgWherePrepared = null!;
	private FrozenQuery<(int code, int min), PqbItem> _uniqueArgWhereFrozen = null!;
	private PreparedQuery<int, PqbItem> _listWherePrepared = null!;
	private FrozenQuery<int, PqbItem> _listWhereFrozen = null!;
	private PreparedQuery<(int lo, int hi), PqbItem> _rangePrepared = null!;
	private FrozenQuery<(int lo, int hi), PqbItem> _rangeFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _joinOnePrepared = null!;
	private FrozenQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _joinOneFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _innerJoinOnePrepared = null!;
	private FrozenQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _innerJoinOneFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbOrder, PqbCustomer?, PqbCustomer?>> _joinOneChainedPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbOrder, PqbCustomer?, PqbCustomer?>> _joinOneChainedFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _joinOneFilteredPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _joinOneFilteredFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _joinOneFilteredDistinctPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbOrder, PqbCustomer?>> _joinOneFilteredDistinctFrozen = null!;
	// Step 8: JoinMany right sides.
	private InMemoryDataCache<int, PqbLine> _lines = null!;
	private CacheKeyValueListIndex<int, PqbLine, int> _lineByItem = null!;
	private InMemoryDataCache<int, PqbNote> _notes = null!;
	private CacheKeyValueListIndex<int, PqbNote, int> _noteByCustomer = null!;
	private InMemoryDataCache<int, PqbDoc> _docs = null!;
	private CacheKeyValueListIndex<int, PqbDoc, int> _docByGroup = null!;
	private CacheCollectionSymmetricKeyValueListIndex<int, PqbDoc, int> _docTags = null!;
	private InMemoryDataCache<int, PqbTag> _tags = null!;
	private PreparedQuery<int, JoinResult<PqbItem, QueryResults<PqbLine>>> _joinManyPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbItem, QueryResults<PqbLine>>> _joinManyFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbItem, QueryResults<PqbLine>>> _innerJoinManyPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbItem, QueryResults<PqbLine>>> _innerJoinManyFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbOrder, QueryResults<PqbNote>>> _joinManyLeftSymPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbOrder, QueryResults<PqbNote>>> _joinManyLeftSymFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbOrder, QueryResults<PqbNote>>> _innerJoinManyLeftSymPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbOrder, QueryResults<PqbNote>>> _innerJoinManyLeftSymFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbDoc, QueryResults<PqbTag>>> _joinManyCollectionPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbDoc, QueryResults<PqbTag>>> _joinManyCollectionFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbDoc, QueryResults<PqbTag>>> _innerJoinManyCollectionPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbDoc, QueryResults<PqbTag>>> _innerJoinManyCollectionFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbItem, QueryResults<PqbLine>>> _sortJoinManyPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbItem, QueryResults<PqbLine>>> _sortJoinManyFrozen = null!;
	private PreparedQuery<int, JoinResult<PqbItem, QueryResults<PqbLine>>> _sortBoundedJoinManyPrepared = null!;
	private FrozenQuery<int, JoinResult<PqbItem, QueryResults<PqbLine>>> _sortBoundedJoinManyFrozen = null!;
	private PreparedQuery<(int group, int band, int lane), JoinResult<PqbItem, QueryResults<PqbLine>>> _threeListSortJoinManyPrepared = null!;
	private FrozenQuery<(int group, int band, int lane), JoinResult<PqbItem, QueryResults<PqbLine>>> _threeListSortJoinManyFrozen = null!;
	private PreparedQuery<(int group, int band, int lane), JoinResult<PqbItem, QueryResults<PqbLine>>> _threeListInnerJoinManyPrepared = null!;
	private FrozenQuery<(int group, int band, int lane), JoinResult<PqbItem, QueryResults<PqbLine>>> _threeListInnerJoinManyFrozen = null!;
	private int _docGroup = 3;
	private PreparedQuery<(int mode, int group, int code), PqbItem> _matchPrepared = null!;
	private FrozenQuery<(int mode, int group, int code), PqbItem> _matchFrozen = null!;

	// Step 4 shapes (composites).
	private PreparedQuery<(int g1, int g2), PqbItem> _orPrepared = null!;
	private FrozenQuery<(int g1, int g2), PqbItem> _orFrozen = null!;
	private FrozenQuery<(int g1, int g2), PqbItem> _orFrozenEagerOrder = null!;
	private PreparedQuery<(int group, int c1, int c2), PqbItem> _orAfterListPrepared = null!;
	private FrozenQuery<(int group, int c1, int c2), PqbItem> _orAfterListFrozen = null!;
	private PreparedQuery<(bool cond, int group, int code), PqbItem> _ifPrepared = null!;
	private FrozenQuery<(bool cond, int group, int code), PqbItem> _ifFrozen = null!;
	private (int g1, int g2) _orArgs = (13, 42);
	private (int group, int c1, int c2) _orAfterListArgs = (13, 1000 + 13, 1000 + 13 + 100 * 420);
	private (bool cond, int group, int code) _ifTakenArgs = (true, 13, 1000 + 13 + 100 * 420);
	private (bool cond, int group, int code) _ifSkippedArgs = (false, 13, 1000 + 13 + 100 * 420);
	private PreparedQuery<(int? lo, int? hi), PqbItem> _optionalRangePrepared = null!;
	private FrozenQuery<(int? lo, int? hi), PqbItem> _optionalRangeFrozen = null!;

	// Stage 2 shapes.
	private PreparedQuery<int, PqbItem> _listTwoWheresPrepared = null!;
	private FrozenQuery<int, PqbItem> _listTwoWheresFrozen = null!;
	private FrozenQuery<int, PqbItem> _listTwoWheresFrozenFixedOrder = null!;
	private PreparedQuery<int, PqbItem> _listThreeWheresPrepared = null!;
	private FrozenQuery<int, PqbItem> _listThreeWheresFrozen = null!;
	private FrozenQuery<int, PqbItem> _listThreeWheresFrozenFixedOrder = null!;
	private PreparedQuery<(int group, int min, int max), PqbItem> _listTwoArgWheresPrepared = null!;
	private FrozenQuery<(int group, int min, int max), PqbItem> _listTwoArgWheresFrozen = null!;
	private FrozenQuery<int, PqbItem> _listWhereFrozenNoHints = null!;
	private PreparedQuery<(int group, int tier), PqbItem> _listListPrepared = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListFrozen = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListFrozenEagerOrder = null!;
	private PreparedQuery<(int group, int tier), PqbItem> _listListReversedPrepared = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListReversedFrozen = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _listListReversedFrozenEagerOrder = null!;
	private PreparedQuery<(int group, int lo, int hi), PqbItem> _listRangePrepared = null!;
	private FrozenQuery<(int group, int lo, int hi), PqbItem> _listRangeFrozen = null!;

	// Stage 3 shapes.
	private PreparedQuery<(int group, int band, int lane), PqbItem> _listListListPrepared = null!;
	private FrozenQuery<(int group, int band, int lane), PqbItem> _listListListFrozen = null!;
	private PreparedQuery<(int group, int tier), PqbItem> _sortListListPrepared = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _sortListListFrozen = null!;
	private PreparedQuery<int, PqbItem> _sortBoundedPrepared = null!;
	private FrozenQuery<int, PqbItem> _sortBoundedFrozen = null!;
	private PreparedQuery<(int group, int tier), PqbItem> _sortBoundedListListPrepared = null!;
	private FrozenQuery<(int group, int tier), PqbItem> _sortBoundedListListFrozen = null!;
	private PreparedQuery<int, PqbItem> _listKeySetPrepared = null!;
	private FrozenQuery<int, PqbItem> _listKeySetFrozen = null!;
	private PreparedQuery<(int group, long after), PqbItem> _listLastUpdatedPrepared = null!;
	private FrozenQuery<(int group, long after), PqbItem> _listLastUpdatedFrozen = null!;
	private PreparedQuery<(int group, int band, int lane), JoinResult<PqbItem, PqbCustomer?>> _threeListSortJoinPrepared = null!;
	private FrozenQuery<(int group, int band, int lane), JoinResult<PqbItem, PqbCustomer?>> _threeListSortJoinFrozen = null!;
	private PreparedQuery<(long t, int keyA, int keyB), JoinResult<PqbRecord, PqbCustomer?, PqbProduct?>> _timeWindowPrepared = null!;
	private FrozenQuery<(long t, int keyA, int keyB), JoinResult<PqbRecord, PqbCustomer?, PqbProduct?>> _timeWindowFrozen = null!;
	private FrozenQuery<(long t, int keyA, int keyB), JoinResult<PqbRecord, PqbCustomer?, PqbProduct?>> _timeWindowFrozenIndexSide = null!;
	private FrozenQuery<(long t, int keyA, int keyB), PqbRecord> _timeWindowNoJoinFrozen = null!;
	private PreparedQuery<(long t, int keyA, int keyB), JoinResult<PqbRecord, PqbCustomer?, PqbProduct?>> _timeRangePrepared = null!;
	private FrozenQuery<(long t, int keyA, int keyB), JoinResult<PqbRecord, PqbCustomer?, PqbProduct?>> _timeRangeFrozen = null!;
	private (int group, long after) _listLastUpdatedArgs = (13, 1_000_000L + N / 2);
	private (int group, int band, int lane) _threeListArgs = (13, 113, 413);
	private (long t, int keyA, int keyB) _timeWindowArgs = (RecordBase + Hour * 85 / 100, 0, 0);

	// Arguments are fields, not constants, so no side gets a constant folded into the query.
	private int _code = 1000 + 42_042;
	private (int code, int min) _uniqueArgWhereArgs = (1000 + 42_042, 0);
	private int _group = 13;
	private (int lo, int hi) _rangeArgs = (1000 + 50_000, 1000 + 51_000);
	private int _customer = 7;
	private (int mode, int group, int code) _matchArgs = (2, 13, 1000 + 13 + 100 * 420);
	private (int? lo, int? hi) _optionalRangeArgs = (1000 + 50_000, 1000 + 51_000);
	private (int group, int min, int max) _listTwoArgWheresArgs = (13, 20_000, 80_000);
	private (int group, int tier) _listListArgs = (13, 13);
	private (int group, int lo, int hi) _listRangeArgs = (13, 1000 + 20_000, 1000 + 80_000);

	[GlobalSetup]
	public void Setup() {
		_items = new InMemoryDataCache<int, PqbItem>();
		_byCode = _items.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byTier = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Tier);
		_codeRange = _items.CacheRangeIndex<int>(static (_, v) => v.Code);
		// Band: 333 rows inside a group (3-way split of the group's 300-row blocks); Lane: 111 rows inside a band (9-way split, lane / 100 % 3 == band / 100).
		_byBand = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Band);
		_byLane = _items.CacheKeyValueListIndex<int>(static (_, v) => v.Lane);
		_flagged = _items.AddKeySetIndex(static (_, v) => v.Flag);
		_itemsUpdated = new LastUpdatedIndex<int>();
		_items.CacheLastUpdatedIndex(_itemsUpdated, static (id, _) => id);
		_details = new InMemoryDataCache<int, PqbCustomer>();
		var rng = new Random(1234);
		for (var i = 0; i < N; i++) {
			_items.AddOrUpdate(i, new PqbItem {
				Id = i, Code = 1000 + i, Group = i % Buckets, Tier = i % (Buckets * 10), Band = i % Buckets + Buckets * (i / 300 % 3), Lane = i % Buckets + Buckets * (i / 300 % 9),
				Flag = i % 3 == 0, Score = rng.Next(int.MaxValue),
			}, 1_000_000L + i);
			if (i % 4 != 0)
				_details.AddOrUpdate(i, new PqbCustomer { Id = i, Region = i % 2 == 0 ? "EU" : "US" });
		}

		_records = new InMemoryDataCache<int, PqbRecord>();
		_recordsUpdated = new LastUpdatedIndex<int>();
		_records.CacheLastUpdatedIndex(_recordsUpdated, static (id, _) => id);
		_tsRange = _records.CacheRangeIndex<long>(static (_, v) => v.Ts);
		_byKeyA = _records.CacheKeyValueListIndex<int>(static (_, v) => v.KeyA);
		_byKeyB = _records.CacheKeyValueListIndex<int>(static (_, v) => v.KeyB);
		_recByCustomer = _records.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_recByProduct = _records.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.ProductId);
		_recCustomers = new InMemoryDataCache<int, PqbCustomer>();
		_recProducts = new InMemoryDataCache<int, PqbProduct>();
		for (var c = 0; c < 2000; c++)
			if (c % 7 != 0) _recCustomers.AddOrUpdate(c, new PqbCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
		for (var p = 0; p < 500; p++)
			_recProducts.AddOrUpdate(p, new PqbProduct { Id = p, Category = "c" + p % 9 });
		// A hot value in each key (KeyA 0: a third of the rows, KeyB 0: a fifth), timestamps over one
		// hour by a bijective shuffle; "newer than 85%" selects the newest ~15% (~100 rows of the hot pair).
		for (var i = 0; i < RecordCount; i++) {
			var ts = RecordBase + (long)(i * 7919 % RecordCount) * Hour / RecordCount;
			_records.AddOrUpdate(i, new PqbRecord { Id = i, KeyA = i % 3 == 0 ? 0 : 1 + i % 19, KeyB = i % 5 == 0 ? 0 : 1 + i % 49, Ts = ts, CustomerId = i % 2000, ProductId = i % 500, Score = rng.Next(1 << 20) }, ts);
		}

		_orders = new InMemoryDataCache<int, PqbOrder>();
		_byCustomer = _orders.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		_customers = new InMemoryDataCache<int, PqbCustomer>();
		for (var c = 0; c < Buckets; c++)
			_customers.AddOrUpdate(c, new PqbCustomer { Id = c, Region = c % 2 == 0 ? "EU" : "US" });
		_orderDetails = new InMemoryDataCache<int, PqbCustomer>();
		for (var i = 0; i < N; i++) {
			_orders.AddOrUpdate(i, new PqbOrder { Id = i, CustomerId = i % Buckets, Qty = i % 13 });
			if (i % 4 != 0)
				_orderDetails.AddOrUpdate(i, new PqbCustomer { Id = i, Region = i % 2 == 0 ? "EU" : "US" });
		}

		_lines = new InMemoryDataCache<int, PqbLine>();
		_lineByItem = _lines.CacheKeyValueListIndex<int>(static (_, v) => v.ItemId);
		for (var i = 0; i < N; i++) {
			if (i % 4 == 0)
				continue;
			_lines.AddOrUpdate(2 * i, new PqbLine { Id = 2 * i, ItemId = i, Qty = i % 7 });
			_lines.AddOrUpdate(2 * i + 1, new PqbLine { Id = 2 * i + 1, ItemId = i, Qty = i % 11 });
		}

		_notes = new InMemoryDataCache<int, PqbNote>();
		_noteByCustomer = _notes.CacheKeyValueListIndex<int>(static (_, v) => v.CustomerId);
		for (var c = 0; c < Buckets; c++)
			for (var k = 0; k < 3; k++)
				_notes.AddOrUpdate(c * 3 + k, new PqbNote { Id = c * 3 + k, CustomerId = c, Text = "n" + k });
		_docs = new InMemoryDataCache<int, PqbDoc>();
		_docByGroup = _docs.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_docTags = _docs.CacheCollectionSymmetricKeyValueListIndex<int>(static (_, v) => v.TagIds);
		_tags = new InMemoryDataCache<int, PqbTag>();
		for (var t = 0; t < 500; t++)
			_tags.AddOrUpdate(t, new PqbTag { Id = t, Name = "t" + t });
		for (var d = 0; d < RecordCount; d++)
			_docs.AddOrUpdate(d, new PqbDoc { Id = d, Group = d % 10, TagIds = d % 4 == 0 ? [] : [d % 500, d * 7 % 500, d * 13 % 500] });

		_uniqueBoundPrepared = _items.Prepare().UseIndex(_byCode, _code).Build();
		_uniqueBoundFrozen = _items.Prepare().UseIndex(_byCode, _code).BuildFrozen();
		_uniqueBoundIntArgsFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, _code).BuildFrozen();
		_uniqueArgPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, static c => c).Build();
		_uniqueArgFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, static c => c).BuildFrozen();
		_uniqueWherePrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, static c => c).Where(static v => v.Flag).Build();
		_uniqueWhereFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byCode, static c => c).Where(static v => v.Flag).BuildFrozen();
		_uniqueArgWherePrepared = _items.Prepare<int, PqbItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static (v, in a) => v.Score >= a.min).Build();
		_uniqueArgWhereFrozen = _items.Prepare<int, PqbItem, (int code, int min)>().UseIndex(_byCode, static a => a.code).Where(static (v, in a) => v.Score >= a.min).BuildFrozen();
		_listWherePrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Build();
		_listWhereFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).BuildFrozen();
		_rangePrepared = _items.Prepare<int, PqbItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build();
		_rangeFrozen = _items.Prepare<int, PqbItem, (int lo, int hi)>().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();
		_joinOnePrepared = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).Build();
		_joinOneFrozen = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).BuildFrozen();
		_innerJoinOnePrepared = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinOne(_orderDetails).Build();
		_innerJoinOneFrozen = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinOne(_orderDetails).BuildFrozen();
		_joinOneChainedPrepared = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).JoinOne(_orderDetails).Build();
		_joinOneChainedFrozen = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers).JoinOne(_orderDetails).BuildFrozen();
		_joinOneFilteredPrepared = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).Build();
		_joinOneFilteredFrozen = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).BuildFrozen();
		_joinOneFilteredDistinctPrepared = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_orderDetails, static q => q.Where(static d => d.Region == "EU")).Build();
		_joinOneFilteredDistinctFrozen = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinOne(_orderDetails, static q => q.Where(static d => d.Region == "EU")).BuildFrozen();
		_joinManyPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).JoinMany(_lines, _lineByItem).Build();
		_joinManyFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).JoinMany(_lines, _lineByItem).BuildFrozen();
		_innerJoinManyPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).InnerJoinMany(_lines, _lineByItem).Build();
		_innerJoinManyFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).InnerJoinMany(_lines, _lineByItem).BuildFrozen();
		_joinManyLeftSymPrepared = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(_byCustomer, _notes, _noteByCustomer).Build();
		_joinManyLeftSymFrozen = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).JoinMany(_byCustomer, _notes, _noteByCustomer).BuildFrozen();
		_innerJoinManyLeftSymPrepared = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).Build();
		_innerJoinManyLeftSymFrozen = _orders.Prepare<int, PqbOrder, int>().UseIndex(_byCustomer, static c => c).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).BuildFrozen();
		_joinManyCollectionPrepared = _docs.Prepare<int, PqbDoc, int>().UseIndex(_docByGroup, static g => g).JoinManyCollectionForward(_tags, _docTags).Build();
		_joinManyCollectionFrozen = _docs.Prepare<int, PqbDoc, int>().UseIndex(_docByGroup, static g => g).JoinManyCollectionForward(_tags, _docTags).BuildFrozen();
		_innerJoinManyCollectionPrepared = _docs.Prepare<int, PqbDoc, int>().UseIndex(_docByGroup, static g => g).InnerJoinManyCollectionForward(_tags, _docTags).Build();
		_innerJoinManyCollectionFrozen = _docs.Prepare<int, PqbDoc, int>().UseIndex(_docByGroup, static g => g).InnerJoinManyCollectionForward(_tags, _docTags).BuildFrozen();
		_sortJoinManyPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Sort(new PqbByScore()).JoinMany(_lines, _lineByItem).Build();
		_sortJoinManyFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Sort(new PqbByScore()).JoinMany(_lines, _lineByItem).BuildFrozen();
		_sortBoundedJoinManyPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new PqbByScore()).JoinMany(_lines, _lineByItem).Build();
		_sortBoundedJoinManyFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new PqbByScore()).JoinMany(_lines, _lineByItem).BuildFrozen();
		_threeListSortJoinManyPrepared = _items.Prepare<int, PqbItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane)
			.SortBounded(new PqbByScoreTies()).JoinMany(_lines, _lineByItem).Build();
		_threeListSortJoinManyFrozen = _items.Prepare<int, PqbItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane)
			.SortBounded(new PqbByScoreTies()).JoinMany(_lines, _lineByItem).BuildFrozen();
		_threeListInnerJoinManyPrepared = _items.Prepare<int, PqbItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane)
			.InnerJoinMany(_lines, _lineByItem).Build();
		_threeListInnerJoinManyFrozen = _items.Prepare<int, PqbItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane)
			.InnerJoinMany(_lines, _lineByItem).BuildFrozen();
		_matchPrepared = _items.Prepare<int, PqbItem, (int mode, int group, int code)>().Match(static a => a.mode, m => m
			.Case(0, b => b.UseIndex(_byCode, static a => a.code))
			.Case(1, b => b.UseIndex(_byGroup, static a => a.group))
			.Case(2, b => b.UseIndex(_byGroup, static a => a.group).Where(static v => v.Flag)).Default()).Build();
		_matchFrozen = _items.Prepare<int, PqbItem, (int mode, int group, int code)>().Match(static a => a.mode, m => m
			.Case(0, b => b.UseIndex(_byCode, static a => a.code))
			.Case(1, b => b.UseIndex(_byGroup, static a => a.group))
			.Case(2, b => b.UseIndex(_byGroup, static a => a.group).Where(static v => v.Flag)).Default()).BuildFrozen();
		_optionalRangePrepared = _items.Prepare<int, PqbItem, (int? lo, int? hi)>().UseIndex(_codeRange, static a => a.lo, static a => a.hi, toInclusive: false).Build();
		_optionalRangeFrozen = _items.Prepare<int, PqbItem, (int? lo, int? hi)>().UseIndex(_codeRange, static a => a.lo, static a => a.hi, toInclusive: false).BuildFrozen();

		_orPrepared = _items.Prepare<int, PqbItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).Build();
		_orFrozen = _items.Prepare<int, PqbItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen();
		var eagerOrder = new FrozenOptions { PreserveEagerOrder = true };
		_orFrozenEagerOrder = _items.Prepare<int, PqbItem, (int g1, int g2)>().Or(b => b.UseIndex(_byGroup, static a => a.g1), b => b.UseIndex(_byGroup, static a => a.g2)).BuildFrozen(eagerOrder);
		_orAfterListPrepared = _items.Prepare<int, PqbItem, (int group, int c1, int c2)>().UseIndex(_byGroup, static a => a.group).Or(b => b.UseIndex(_byCode, static a => a.c1), b => b.UseIndex(_byCode, static a => a.c2)).Build();
		_orAfterListFrozen = _items.Prepare<int, PqbItem, (int group, int c1, int c2)>().UseIndex(_byGroup, static a => a.group).Or(b => b.UseIndex(_byCode, static a => a.c1), b => b.UseIndex(_byCode, static a => a.c2)).BuildFrozen();
		_ifPrepared = _items.Prepare<int, PqbItem, (bool cond, int group, int code)>().UseIndex(_byGroup, static a => a.group).If(static a => a.cond, b => b.UseIndex(_byCode, static a => a.code)).Build();
		_ifFrozen = _items.Prepare<int, PqbItem, (bool cond, int group, int code)>().UseIndex(_byGroup, static a => a.group).If(static a => a.cond, b => b.UseIndex(_byCode, static a => a.code)).BuildFrozen();

		var fixedOrder = new FrozenOptions { AdaptiveFilterOrdering = false };
		var noHints = new FrozenOptions { CapacityHints = false };
		_listTwoWheresPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Where(static v => v.Score % 10 == 0).Build();
		_listTwoWheresFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Where(static v => v.Score % 10 == 0).BuildFrozen();
		_listTwoWheresFrozenFixedOrder = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Where(static v => v.Score % 10 == 0).BuildFrozen(fixedOrder);
		_listThreeWheresPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g)
			.Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Score % 100 == 0).Build();
		_listThreeWheresFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g)
			.Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Score % 100 == 0).BuildFrozen();
		_listThreeWheresFrozenFixedOrder = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g)
			.Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Score % 100 == 0).BuildFrozen(fixedOrder);
		_listTwoArgWheresPrepared = _items.Prepare<int, PqbItem, (int group, int min, int max)>().UseIndex(_byGroup, static a => a.group)
			.Where(static (v, in a) => v.Id >= a.min).Where(static (v, in a) => v.Id <= a.max).Build();
		_listTwoArgWheresFrozen = _items.Prepare<int, PqbItem, (int group, int min, int max)>().UseIndex(_byGroup, static a => a.group)
			.Where(static (v, in a) => v.Id >= a.min).Where(static (v, in a) => v.Id <= a.max).BuildFrozen();
		_listWhereFrozenNoHints = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).BuildFrozen(noHints);
		_listListPrepared = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Build();
		_listListFrozen = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen();
		_listListFrozenEagerOrder = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).BuildFrozen(eagerOrder);
		_listListReversedPrepared = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).Build();
		_listListReversedFrozen = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen();
		_listListReversedFrozenEagerOrder = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byTier, static a => a.tier).UseIndex(_byGroup, static a => a.group).BuildFrozen(eagerOrder);
		_listRangePrepared = _items.Prepare<int, PqbItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).Build();
		_listRangeFrozen = _items.Prepare<int, PqbItem, (int group, int lo, int hi)>().UseIndex(_byGroup, static a => a.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi)).BuildFrozen();

		_listListListPrepared = _items.Prepare<int, PqbItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane).Build();
		_listListListFrozen = _items.Prepare<int, PqbItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane).BuildFrozen();
		_sortListListPrepared = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Sort(new PqbByScore()).Build();
		_sortListListFrozen = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).Sort(new PqbByScore()).BuildFrozen();
		_sortBoundedPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new PqbByScore()).Build();
		_sortBoundedFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).SortBounded(new PqbByScore()).BuildFrozen();
		_sortBoundedListListPrepared = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new PqbByScore()).Build();
		_sortBoundedListListFrozen = _items.Prepare<int, PqbItem, (int group, int tier)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byTier, static a => a.tier).SortBounded(new PqbByScore()).BuildFrozen();
		_listKeySetPrepared = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).UseIndex(_flagged).Build();
		_listKeySetFrozen = _items.Prepare<int, PqbItem, int>().UseIndex(_byGroup, static g => g).UseIndex(_flagged).BuildFrozen();
		_listLastUpdatedPrepared = _items.Prepare<int, PqbItem, (int group, long after)>().UseIndex(_byGroup, static a => a.group).UseIndex(_itemsUpdated, static a => a.after).Build();
		_listLastUpdatedFrozen = _items.Prepare<int, PqbItem, (int group, long after)>().UseIndex(_byGroup, static a => a.group).UseIndex(_itemsUpdated, static a => a.after).BuildFrozen();
		_threeListSortJoinPrepared = _items.Prepare<int, PqbItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane)
			.SortBounded(new PqbByScoreTies()).JoinOne(_details).Build();
		_threeListSortJoinFrozen = _items.Prepare<int, PqbItem, (int group, int band, int lane)>().UseIndex(_byGroup, static a => a.group).UseIndex(_byBand, static a => a.band).UseIndex(_byLane, static a => a.lane)
			.SortBounded(new PqbByScoreTies()).JoinOne(_details).BuildFrozen();
		_timeWindowPrepared = _records.Prepare<int, PqbRecord, (long t, int keyA, int keyB)>().UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new PqbRecordByScoreTies()).JoinOne(_recByCustomer, _recCustomers).JoinOne(_recByProduct, _recProducts).Build();
		_timeWindowFrozen = _records.Prepare<int, PqbRecord, (long t, int keyA, int keyB)>().UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new PqbRecordByScoreTies()).JoinOne(_recByCustomer, _recCustomers).JoinOne(_recByProduct, _recProducts).BuildFrozen();
		_timeWindowFrozenIndexSide = _records.Prepare<int, PqbRecord, (long t, int keyA, int keyB)>().UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new PqbRecordByScoreTies()).JoinOne(_recByCustomer, _recCustomers).JoinOne(_recByProduct, _recProducts).BuildFrozen(new FrozenOptions { IndexSideProbes = true });
		_timeWindowNoJoinFrozen = _records.Prepare<int, PqbRecord, (long t, int keyA, int keyB)>().UseIndex(_recordsUpdated, static a => a.t).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new PqbRecordByScoreTies()).BuildFrozen();
		_timeRangePrepared = _records.Prepare<int, PqbRecord, (long t, int keyA, int keyB)>().UseIndex(_tsRange, static (rb, a) => rb.Gt(a.t)).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new PqbRecordByScoreTies()).JoinOne(_recByCustomer, _recCustomers).JoinOne(_recByProduct, _recProducts).Build();
		_timeRangeFrozen = _records.Prepare<int, PqbRecord, (long t, int keyA, int keyB)>().UseIndex(_tsRange, static (rb, a) => rb.Gt(a.t)).UseIndex(_byKeyA, static a => a.keyA).UseIndex(_byKeyB, static a => a.keyB)
			.SortBounded(new PqbRecordByScoreTies()).JoinOne(_recByCustomer, _recCustomers).JoinOne(_recByProduct, _recProducts).BuildFrozen();
	}

	// ── 1. unique lookup, value bound at build ────────────────────────────────────

	[BenchmarkCategory("Unique"), Benchmark(Baseline = true)]
	public int Unique_Eager() {
		using var r = _items.Query().UseIndex(_byCode, _code).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Unique"), Benchmark]
	public int Unique_Prepared() {
		using var r = _uniqueBoundPrepared.ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Unique"), Benchmark]
	public int Unique_Frozen() {
		using var r = _uniqueBoundFrozen.ExecutePooled();
		return r.Count;
	}

	// Optimization 5 probes (the stage-1 "bound is ~8 ns slower than parameterized" note): the same
	// bound plan called through the virtual terminal directly instead of the NoArgs extension, and a
	// bound plan whose TArgs is int rather than the empty NoArgs struct.
	[BenchmarkCategory("Unique"), Benchmark]
	public int Unique_FrozenDirectCall() {
		using var r = _uniqueBoundFrozen.ExecutePooled(default(NoArgs));
		return r.Count;
	}

	[BenchmarkCategory("Unique"), Benchmark]
	public int Unique_FrozenBoundIntArgs() {
		using var r = _uniqueBoundIntArgsFrozen.ExecutePooled(0);
		return r.Count;
	}

	// ── 2. unique lookup, parameterized ───────────────────────────────────────────

	[BenchmarkCategory("UniqueArg"), Benchmark(Baseline = true)]
	public int UniqueArg_Eager() {
		using var r = _items.Query().UseIndex(_byCode, _code).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("UniqueArg"), Benchmark]
	public int UniqueArg_Prepared() {
		using var r = _uniqueArgPrepared.ExecutePooled(_code);
		return r.Count;
	}

	[BenchmarkCategory("UniqueArg"), Benchmark]
	public int UniqueArg_Frozen() {
		using var r = _uniqueArgFrozen.ExecutePooled(_code);
		return r.Count;
	}

	// ── 3. unique + constant Where ────────────────────────────────────────────────

	[BenchmarkCategory("UniqueWhere"), Benchmark(Baseline = true)]
	public int UniqueWhere_Eager() {
		using var r = _items.Query().UseIndex(_byCode, _code).Where(static v => v.Flag).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("UniqueWhere"), Benchmark]
	public int UniqueWhere_Prepared() {
		using var r = _uniqueWherePrepared.ExecutePooled(_code);
		return r.Count;
	}

	[BenchmarkCategory("UniqueWhere"), Benchmark]
	public int UniqueWhere_Frozen() {
		using var r = _uniqueWhereFrozen.ExecutePooled(_code);
		return r.Count;
	}

	// ── 4. unique + parameterized Where ───────────────────────────────────────────

	// The eager twin is what a caller writes: the per-call argument captured by the lambda, which
	// Roslyn hoists into a display class allocated per call.
	private QueryResults<PqbItem> EagerUniqueArgWhere((int code, int min) a)
		=> _items.Query().UseIndex(_byCode, a.code).Where(v => v.Score >= a.min).ExecutePooled();

	[BenchmarkCategory("UniqueArgWhere"), Benchmark(Baseline = true)]
	public int UniqueArgWhere_Eager() {
		using var r = EagerUniqueArgWhere(_uniqueArgWhereArgs);
		return r.Count;
	}

	[BenchmarkCategory("UniqueArgWhere"), Benchmark]
	public int UniqueArgWhere_Prepared() {
		using var r = _uniqueArgWherePrepared.ExecutePooled(_uniqueArgWhereArgs);
		return r.Count;
	}

	[BenchmarkCategory("UniqueArgWhere"), Benchmark]
	public int UniqueArgWhere_Frozen() {
		using var r = _uniqueArgWhereFrozen.ExecutePooled(_uniqueArgWhereArgs);
		return r.Count;
	}

	// ── 5. list index + constant Where (not eligible: replay) ─────────────────────

	[BenchmarkCategory("ListWhere"), Benchmark(Baseline = true)]
	public int ListWhere_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).Where(static v => v.Flag).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListWhere"), Benchmark]
	public int ListWhere_Prepared() {
		using var r = _listWherePrepared.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListWhere"), Benchmark]
	public int ListWhere_Frozen() {
		using var r = _listWhereFrozen.ExecutePooled(_group);
		return r.Count;
	}

	// Stage 2, optimization 2 control: the same frozen plan with capacity hints off.
	[BenchmarkCategory("ListWhere"), Benchmark]
	public int ListWhere_FrozenNoHints() {
		using var r = _listWhereFrozenNoHints.ExecutePooled(_group);
		return r.Count;
	}

	// ── 6. range, parameterized (not eligible: replay) ────────────────────────────

	[BenchmarkCategory("Range"), Benchmark(Baseline = true)]
	public int Range_Eager() {
		using var r = _items.Query().UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), _rangeArgs).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Range"), Benchmark]
	public int Range_Prepared() {
		using var r = _rangePrepared.ExecutePooled(_rangeArgs);
		return r.Count;
	}

	[BenchmarkCategory("Range"), Benchmark]
	public int Range_Frozen() {
		using var r = _rangeFrozen.ExecutePooled(_rangeArgs);
		return r.Count;
	}

	// ── 7. JoinOne, parameterized: list (1k) → outer left-symmetric JoinOne, fused since step 5 ──────

	[BenchmarkCategory("JoinOne"), Benchmark(Baseline = true)]
	public int JoinOne_Eager() {
		using var r = _orders.Query().UseIndex(_byCustomer, _customer).JoinOne(_byCustomer, _customers).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinOne"), Benchmark]
	public int JoinOne_Prepared() {
		using var r = _joinOnePrepared.ExecutePooled(_customer);
		return r.Count;
	}

	[BenchmarkCategory("JoinOne"), Benchmark]
	public int JoinOne_Frozen() {
		using var r = _joinOneFrozen.ExecutePooled(_customer);
		return r.Count;
	}

	// ── 7b. InnerJoinOne: list (1k) → inner PK-to-PK JoinOne (a quarter of the rights missing), fused ──

	[BenchmarkCategory("InnerJoinOne"), Benchmark(Baseline = true)]
	public int InnerJoinOne_Eager() {
		using var r = _orders.Query().UseIndex(_byCustomer, _customer).InnerJoinOne(_orderDetails).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinOne"), Benchmark]
	public int InnerJoinOne_Prepared() {
		using var r = _innerJoinOnePrepared.ExecutePooled(_customer);
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinOne"), Benchmark]
	public int InnerJoinOne_Frozen() {
		using var r = _innerJoinOneFrozen.ExecutePooled(_customer);
		return r.Count;
	}

	// ── 7c. JoinOneChained: list (1k) → outer left-symmetric JoinOne → outer PK-to-PK JoinOne, both fused ──

	[BenchmarkCategory("JoinOneChained"), Benchmark(Baseline = true)]
	public int JoinOneChained_Eager() {
		using var r = _orders.Query().UseIndex(_byCustomer, _customer).JoinOne(_byCustomer, _customers).JoinOne(_orderDetails).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinOneChained"), Benchmark]
	public int JoinOneChained_Prepared() {
		using var r = _joinOneChainedPrepared.ExecutePooled(_customer);
		return r.Count;
	}

	[BenchmarkCategory("JoinOneChained"), Benchmark]
	public int JoinOneChained_Frozen() {
		using var r = _joinOneChainedFrozen.ExecutePooled(_customer);
		return r.Count;
	}

	// ── 7d. JoinOneFiltered: a right-side filter callback, fused since fda6e9c (the build probe reduces
	// it to a value predicate). Degenerate on purpose and left that way: all ~1k lefts of bucket 7 resolve
	// to the one customer 7, whose Region is "US", so the filter rejects every row and the right lookup
	// hits one perfectly-cached key. It measures the filter and the lookup, not right-cache diversity —
	// 7e is the sibling that does. ──────────

	[BenchmarkCategory("JoinOneFiltered"), Benchmark(Baseline = true)]
	public int JoinOneFiltered_Eager() {
		using var r = _orders.Query().UseIndex(_byCustomer, _customer).JoinOne(_byCustomer, _customers, static q => q.Where(static c => c.Region == "EU")).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinOneFiltered"), Benchmark]
	public int JoinOneFiltered_Prepared() {
		using var r = _joinOneFilteredPrepared.ExecutePooled(_customer);
		return r.Count;
	}

	[BenchmarkCategory("JoinOneFiltered"), Benchmark]
	public int JoinOneFiltered_Frozen() {
		using var r = _joinOneFilteredFrozen.ExecutePooled(_customer);
		return r.Count;
	}

	// ── 7e. JoinOneFilteredDistinct: the same filtered shape with the degeneracy removed — an outer
	// PK-to-PK join instead, so each of the ~1k lefts looks up its own right (a quarter of them absent),
	// and Region alternates by Id so the predicate accepts about half of the rest instead of none. This is
	// the row that reads the fused filter's cost per accepted right and the lookup's cache behaviour. ──

	[BenchmarkCategory("JoinOneFilteredDistinct"), Benchmark(Baseline = true)]
	public int JoinOneFilteredDistinct_Eager() {
		using var r = _orders.Query().UseIndex(_byCustomer, _customer).JoinOne(_orderDetails, static q => q.Where(static d => d.Region == "EU")).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinOneFilteredDistinct"), Benchmark]
	public int JoinOneFilteredDistinct_Prepared() {
		using var r = _joinOneFilteredDistinctPrepared.ExecutePooled(_customer);
		return r.Count;
	}

	[BenchmarkCategory("JoinOneFilteredDistinct"), Benchmark]
	public int JoinOneFilteredDistinct_Frozen() {
		using var r = _joinOneFilteredDistinctFrozen.ExecutePooled(_customer);
		return r.Count;
	}

	// ── 8. JoinMany (step 8): the narrowing is the pipeline pass, the fan-out runs after it over the rows the pass formed ──
	// 8a. right-list family, outer: list (1k) → lines by item (two per item, none for every fourth: ~1.5k rights, no shared right).
	[BenchmarkCategory("JoinMany"), Benchmark(Baseline = true)]
	public int JoinMany_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).JoinMany(_lines, _lineByItem).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinMany"), Benchmark]
	public int JoinMany_Prepared() {
		using var r = _joinManyPrepared.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("JoinMany"), Benchmark]
	public int JoinMany_Frozen() {
		using var r = _joinManyFrozen.ExecutePooled(_group);
		return r.Count;
	}

	// 8b. right-list family, inner: a quarter of the lefts have no line and are dropped (not emitted, not counted).
	[BenchmarkCategory("InnerJoinMany"), Benchmark(Baseline = true)]
	public int InnerJoinMany_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).InnerJoinMany(_lines, _lineByItem).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinMany"), Benchmark]
	public int InnerJoinMany_Prepared() {
		using var r = _innerJoinManyPrepared.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinMany"), Benchmark]
	public int InnerJoinMany_Frozen() {
		using var r = _innerJoinManyFrozen.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("Count_JoinMany"), Benchmark(Baseline = true)]
	public int Count_JoinMany_Eager() => _items.Query().UseIndex(_byGroup, _group).JoinMany(_lines, _lineByItem).Count();

	[BenchmarkCategory("Count_JoinMany"), Benchmark]
	public int Count_JoinMany_Prepared() => _joinManyPrepared.Count(_group);

	[BenchmarkCategory("Count_JoinMany"), Benchmark]
	public int Count_JoinMany_Frozen() => _joinManyFrozen.Count(_group);

	// The inner Count needs the fan-out's answer per left (eager's CountCoreJoined runs the whole inner phase).
	[BenchmarkCategory("Count_InnerJoinMany"), Benchmark(Baseline = true)]
	public int Count_InnerJoinMany_Eager() => _items.Query().UseIndex(_byGroup, _group).InnerJoinMany(_lines, _lineByItem).Count();

	[BenchmarkCategory("Count_InnerJoinMany"), Benchmark]
	public int Count_InnerJoinMany_Prepared() => _innerJoinManyPrepared.Count(_group);

	[BenchmarkCategory("Count_InnerJoinMany"), Benchmark]
	public int Count_InnerJoinMany_Frozen() => _innerJoinManyFrozen.Count(_group);

	// 8c. left-symmetric family: the customer's 1k orders all share the customer's three notes — 3k pairs, 3 distinct
	// rights, every right delivered through the fan-out's chains (the shared-right path).
	[BenchmarkCategory("JoinMany_LeftSym"), Benchmark(Baseline = true)]
	public int JoinMany_LeftSym_Eager() {
		using var r = _orders.Query().UseIndex(_byCustomer, _customer).JoinMany(_byCustomer, _notes, _noteByCustomer).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinMany_LeftSym"), Benchmark]
	public int JoinMany_LeftSym_Prepared() {
		using var r = _joinManyLeftSymPrepared.ExecutePooled(_customer);
		return r.Count;
	}

	[BenchmarkCategory("JoinMany_LeftSym"), Benchmark]
	public int JoinMany_LeftSym_Frozen() {
		using var r = _joinManyLeftSymFrozen.ExecutePooled(_customer);
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinMany_LeftSym"), Benchmark(Baseline = true)]
	public int InnerJoinMany_LeftSym_Eager() {
		using var r = _orders.Query().UseIndex(_byCustomer, _customer).InnerJoinMany(_byCustomer, _notes, _noteByCustomer).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinMany_LeftSym"), Benchmark]
	public int InnerJoinMany_LeftSym_Prepared() {
		using var r = _innerJoinManyLeftSymPrepared.ExecutePooled(_customer);
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinMany_LeftSym"), Benchmark]
	public int InnerJoinMany_LeftSym_Frozen() {
		using var r = _innerJoinManyLeftSymFrozen.ExecutePooled(_customer);
		return r.Count;
	}

	// 8d. collection family (M:N, forward): 1k documents of a group → their tags (three each, a quarter untagged; 500 tags shared
	// across the documents, so the chains carry most deliveries). Inner drops the untagged quarter.
	[BenchmarkCategory("JoinMany_Collection"), Benchmark(Baseline = true)]
	public int JoinMany_Collection_Eager() {
		using var r = _docs.Query().UseIndex(_docByGroup, _docGroup).JoinManyCollectionForward(_tags, _docTags).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("JoinMany_Collection"), Benchmark]
	public int JoinMany_Collection_Prepared() {
		using var r = _joinManyCollectionPrepared.ExecutePooled(_docGroup);
		return r.Count;
	}

	[BenchmarkCategory("JoinMany_Collection"), Benchmark]
	public int JoinMany_Collection_Frozen() {
		using var r = _joinManyCollectionFrozen.ExecutePooled(_docGroup);
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinMany_Collection"), Benchmark(Baseline = true)]
	public int InnerJoinMany_Collection_Eager() {
		using var r = _docs.Query().UseIndex(_docByGroup, _docGroup).InnerJoinManyCollectionForward(_tags, _docTags).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinMany_Collection"), Benchmark]
	public int InnerJoinMany_Collection_Prepared() {
		using var r = _innerJoinManyCollectionPrepared.ExecutePooled(_docGroup);
		return r.Count;
	}

	[BenchmarkCategory("InnerJoinMany_Collection"), Benchmark]
	public int InnerJoinMany_Collection_Frozen() {
		using var r = _innerJoinManyCollectionFrozen.ExecutePooled(_docGroup);
		return r.Count;
	}

	// 8e. classic Sort then JoinMany, page 0..20: the container sorts and crops the 1k rows, the fan-out fills the 20 page rows.
	[BenchmarkCategory("Sort_JoinMany"), Benchmark(Baseline = true)]
	public int Sort_JoinMany_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).Sort(new PqbByScore()).JoinMany(_lines, _lineByItem).ExecutePooled(0, 20);
		return r.Count;
	}

	[BenchmarkCategory("Sort_JoinMany"), Benchmark]
	public int Sort_JoinMany_Prepared() {
		using var r = _sortJoinManyPrepared.ExecutePooled(_group, 0, 20);
		return r.Count;
	}

	[BenchmarkCategory("Sort_JoinMany"), Benchmark]
	public int Sort_JoinMany_Frozen() {
		using var r = _sortJoinManyFrozen.ExecutePooled(_group, 0, 20);
		return r.Count;
	}

	// 8f. SortBounded then JoinMany, page 0..20: the bounded flow — a heap of 20 over the 1k walk, the fan-out fills the page rows only.
	[BenchmarkCategory("SortBounded_JoinMany"), Benchmark(Baseline = true)]
	public int SortBounded_JoinMany_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).SortBounded(new PqbByScore()).JoinMany(_lines, _lineByItem).ExecutePooled(0, 20);
		return r.Count;
	}

	[BenchmarkCategory("SortBounded_JoinMany"), Benchmark]
	public int SortBounded_JoinMany_Prepared() {
		using var r = _sortBoundedJoinManyPrepared.ExecutePooled(_group, 0, 20);
		return r.Count;
	}

	[BenchmarkCategory("SortBounded_JoinMany"), Benchmark]
	public int SortBounded_JoinMany_Frozen() {
		using var r = _sortBoundedJoinManyFrozen.ExecutePooled(_group, 0, 20);
		return r.Count;
	}

	// 8g. the production shape A with a JoinMany: three lists (1k ∩ 333 ∩ 111 → 111 rows), SortBounded page 20..40, lines by item.
	[BenchmarkCategory("ListListListSortBoundedJoinMany"), Benchmark(Baseline = true)]
	public int ListListListSortBoundedJoinMany_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _threeListArgs.group).UseIndex(_byBand, _threeListArgs.band).UseIndex(_byLane, _threeListArgs.lane)
			.SortBounded(new PqbByScoreTies()).JoinMany(_lines, _lineByItem).ExecutePooled(20, 20);
		return r.Count;
	}

	[BenchmarkCategory("ListListListSortBoundedJoinMany"), Benchmark]
	public int ListListListSortBoundedJoinMany_Prepared() {
		using var r = _threeListSortJoinManyPrepared.ExecutePooled(_threeListArgs, 20, 20);
		return r.Count;
	}

	[BenchmarkCategory("ListListListSortBoundedJoinMany"), Benchmark]
	public int ListListListSortBoundedJoinMany_Frozen() {
		using var r = _threeListSortJoinManyFrozen.ExecutePooled(_threeListArgs, 20, 20);
		return r.Count;
	}

	// 8h. narrow hard, then fan out, inner: three lists → 111 rows → inner lines (a quarter dropped).
	[BenchmarkCategory("ListListListInnerJoinMany"), Benchmark(Baseline = true)]
	public int ListListListInnerJoinMany_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _threeListArgs.group).UseIndex(_byBand, _threeListArgs.band).UseIndex(_byLane, _threeListArgs.lane)
			.InnerJoinMany(_lines, _lineByItem).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListListListInnerJoinMany"), Benchmark]
	public int ListListListInnerJoinMany_Prepared() {
		using var r = _threeListInnerJoinManyPrepared.ExecutePooled(_threeListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListListListInnerJoinMany"), Benchmark]
	public int ListListListInnerJoinMany_Frozen() {
		using var r = _threeListInnerJoinManyFrozen.ExecutePooled(_threeListArgs);
		return r.Count;
	}

	[BenchmarkCategory("Count_ListListListInnerJoinMany"), Benchmark(Baseline = true)]
	public int Count_ListListListInnerJoinMany_Eager()
		=> _items.Query().UseIndex(_byGroup, _threeListArgs.group).UseIndex(_byBand, _threeListArgs.band).UseIndex(_byLane, _threeListArgs.lane).InnerJoinMany(_lines, _lineByItem).Count();

	[BenchmarkCategory("Count_ListListListInnerJoinMany"), Benchmark]
	public int Count_ListListListInnerJoinMany_Prepared() => _threeListInnerJoinManyPrepared.Count(_threeListArgs);

	[BenchmarkCategory("Count_ListListListInnerJoinMany"), Benchmark]
	public int Count_ListListListInnerJoinMany_Frozen() => _threeListInnerJoinManyFrozen.Count(_threeListArgs);

	// ── 8. Match, three parameterized arms — step 4: the arm is chosen at bind, the pipeline runs it ──

	private QueryResults<PqbItem> EagerMatch((int mode, int group, int code) a) {
		var q = _items.Query();
		switch (a.mode) {
			case 0: q = q.UseIndex(_byCode, a.code); break;
			case 1: q = q.UseIndex(_byGroup, a.group); break;
			case 2: q = q.UseIndex(_byGroup, a.group).Where(static v => v.Flag); break;
		}

		return q.ExecutePooled();
	}

	[BenchmarkCategory("Match"), Benchmark(Baseline = true)]
	public int Match_Eager() {
		using var r = EagerMatch(_matchArgs);
		return r.Count;
	}

	[BenchmarkCategory("Match"), Benchmark]
	public int Match_Prepared() {
		using var r = _matchPrepared.ExecutePooled(_matchArgs);
		return r.Count;
	}

	[BenchmarkCategory("Match"), Benchmark]
	public int Match_Frozen() {
		using var r = _matchFrozen.ExecutePooled(_matchArgs);
		return r.Count;
	}

	[BenchmarkCategory("Count_Match"), Benchmark(Baseline = true)]
	public int Count_Match_Eager() {
		var q = _items.Query();
		switch (_matchArgs.mode) {
			case 0: q = q.UseIndex(_byCode, _matchArgs.code); break;
			case 1: q = q.UseIndex(_byGroup, _matchArgs.group); break;
			case 2: q = q.UseIndex(_byGroup, _matchArgs.group).Where(static v => v.Flag); break;
		}

		return q.Count();
	}

	[BenchmarkCategory("Count_Match"), Benchmark]
	public int Count_Match_Frozen() => _matchFrozen.Count(_matchArgs);

	// ── 8b. Or as the first narrowing: two 1k buckets — step 4. By default the union itself, in branch
	// order; under PreserveEagerOrder the eager sequence (the store walk kept to the union). Count seeds
	// the union either way. ───────────────────────────────────────────────────────────────────────────

	[BenchmarkCategory("Or"), Benchmark(Baseline = true)]
	public int Or_Eager() {
		using var r = _items.Query().Or(b => b.UseIndex(_byGroup, _orArgs.g1), b => b.UseIndex(_byGroup, _orArgs.g2)).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Or"), Benchmark]
	public int Or_Prepared() {
		using var r = _orPrepared.ExecutePooled(_orArgs);
		return r.Count;
	}

	[BenchmarkCategory("Or"), Benchmark]
	public int Or_Frozen() {
		using var r = _orFrozen.ExecutePooled(_orArgs);
		return r.Count;
	}

	[BenchmarkCategory("Or"), Benchmark]
	public int Or_FrozenEagerOrder() {
		using var r = _orFrozenEagerOrder.ExecutePooled(_orArgs);
		return r.Count;
	}

	[BenchmarkCategory("Count_Or"), Benchmark(Baseline = true)]
	public int Count_Or_Eager() => _items.Query().Or(b => b.UseIndex(_byGroup, _orArgs.g1), b => b.UseIndex(_byGroup, _orArgs.g2)).Count();

	[BenchmarkCategory("Count_Or"), Benchmark]
	public int Count_Or_Frozen() => _orFrozen.Count(_orArgs);

	// ── 8c. A list then an Or of two uniques — step 4: the Or's union is the small-probe seed ────────

	[BenchmarkCategory("OrAfterList"), Benchmark(Baseline = true)]
	public int OrAfterList_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _orAfterListArgs.group).Or(b => b.UseIndex(_byCode, _orAfterListArgs.c1), b => b.UseIndex(_byCode, _orAfterListArgs.c2)).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("OrAfterList"), Benchmark]
	public int OrAfterList_Prepared() {
		using var r = _orAfterListPrepared.ExecutePooled(_orAfterListArgs);
		return r.Count;
	}

	[BenchmarkCategory("OrAfterList"), Benchmark]
	public int OrAfterList_Frozen() {
		using var r = _orAfterListFrozen.ExecutePooled(_orAfterListArgs);
		return r.Count;
	}

	// ── 8d. A list then If(unique) — step 4: taken, the unique arm is the small-probe seed; skipped, the plain list walk ──

	private QueryResults<PqbItem> EagerIf((bool cond, int group, int code) a) {
		var q = _items.Query().UseIndex(_byGroup, a.group);
		if (a.cond) q = q.UseIndex(_byCode, a.code);
		return q.ExecutePooled();
	}

	[BenchmarkCategory("IfTaken"), Benchmark(Baseline = true)]
	public int IfTaken_Eager() {
		using var r = EagerIf(_ifTakenArgs);
		return r.Count;
	}

	[BenchmarkCategory("IfTaken"), Benchmark]
	public int IfTaken_Prepared() {
		using var r = _ifPrepared.ExecutePooled(_ifTakenArgs);
		return r.Count;
	}

	[BenchmarkCategory("IfTaken"), Benchmark]
	public int IfTaken_Frozen() {
		using var r = _ifFrozen.ExecutePooled(_ifTakenArgs);
		return r.Count;
	}

	[BenchmarkCategory("IfSkipped"), Benchmark(Baseline = true)]
	public int IfSkipped_Eager() {
		using var r = EagerIf(_ifSkippedArgs);
		return r.Count;
	}

	[BenchmarkCategory("IfSkipped"), Benchmark]
	public int IfSkipped_Prepared() {
		using var r = _ifPrepared.ExecutePooled(_ifSkippedArgs);
		return r.Count;
	}

	[BenchmarkCategory("IfSkipped"), Benchmark]
	public int IfSkipped_Frozen() {
		using var r = _ifFrozen.ExecutePooled(_ifSkippedArgs);
		return r.Count;
	}

	// ── 9. Optional-bounds range, both bounds ─────────────────────────────────────

	private QueryResults<PqbItem> EagerOptionalRange((int? lo, int? hi) a) {
		var q = _items.Query();
		if (a.lo is not null && a.hi is not null) q = q.UseIndex(_codeRange, static (rb, b) => rb.Gte(b.lo!.Value).Lt(b.hi!.Value), a);
		else if (a.lo is not null) q = q.UseIndex(_codeRange, static (rb, lo) => rb.Gte(lo), a.lo.Value);
		else if (a.hi is not null) q = q.UseIndex(_codeRange, static (rb, hi) => rb.Lt(hi), a.hi.Value);
		return q.ExecutePooled();
	}

	[BenchmarkCategory("OptionalRange"), Benchmark(Baseline = true)]
	public int OptionalRange_Eager() {
		using var r = EagerOptionalRange(_optionalRangeArgs);
		return r.Count;
	}

	[BenchmarkCategory("OptionalRange"), Benchmark]
	public int OptionalRange_Prepared() {
		using var r = _optionalRangePrepared.ExecutePooled(_optionalRangeArgs);
		return r.Count;
	}

	[BenchmarkCategory("OptionalRange"), Benchmark]
	public int OptionalRange_Frozen() {
		using var r = _optionalRangeFrozen.ExecutePooled(_optionalRangeArgs);
		return r.Count;
	}

	// ── Stage 2 ───────────────────────────────────────────────────────────────────
	// Optimization 1 (fused filters + adaptive order). Bucket of 1k rows. Flag passes a third,
	// Score % 10 a tenth, Score % 100 a hundredth; Id >= 0 passes everything. The eager `&&` order is
	// the declared one; the frozen plan learns to ask the most selective predicate first. The
	// `_FixedOrder` rows isolate fusion from ordering.

	// 10. list + 2 constant Wheres, the more selective declared last
	[BenchmarkCategory("ListTwoWheres"), Benchmark(Baseline = true)]
	public int ListTwoWheres_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).Where(static v => v.Flag).Where(static v => v.Score % 10 == 0).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListTwoWheres"), Benchmark]
	public int ListTwoWheres_Prepared() {
		using var r = _listTwoWheresPrepared.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListTwoWheres"), Benchmark]
	public int ListTwoWheres_Frozen() {
		using var r = _listTwoWheresFrozen.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListTwoWheres"), Benchmark]
	public int ListTwoWheres_FrozenFixedOrder() {
		using var r = _listTwoWheresFrozenFixedOrder.ExecutePooled(_group);
		return r.Count;
	}

	// 11. list + 3 constant Wheres, the most selective declared LAST
	[BenchmarkCategory("ListThreeWheresSelectiveLast"), Benchmark(Baseline = true)]
	public int ListThreeWheresSelectiveLast_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).Where(static v => v.Id >= 0).Where(static v => v.Flag).Where(static v => v.Score % 100 == 0).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListThreeWheresSelectiveLast"), Benchmark]
	public int ListThreeWheresSelectiveLast_Prepared() {
		using var r = _listThreeWheresPrepared.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListThreeWheresSelectiveLast"), Benchmark]
	public int ListThreeWheresSelectiveLast_Frozen() {
		using var r = _listThreeWheresFrozen.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListThreeWheresSelectiveLast"), Benchmark]
	public int ListThreeWheresSelectiveLast_FrozenFixedOrder() {
		using var r = _listThreeWheresFrozenFixedOrder.ExecutePooled(_group);
		return r.Count;
	}

	// 12. list + 2 parameterized Wheres. The eager twin is what a caller writes: two lambdas capturing
	// the per-call arguments (a display class per call) plus the core's `&&` closure.
	private QueryResults<PqbItem> EagerListTwoArgWheres((int group, int min, int max) a)
		=> _items.Query().UseIndex(_byGroup, a.group).Where(v => v.Id >= a.min).Where(v => v.Id <= a.max).ExecutePooled();

	[BenchmarkCategory("ListTwoArgWheres"), Benchmark(Baseline = true)]
	public int ListTwoArgWheres_Eager() {
		using var r = EagerListTwoArgWheres(_listTwoArgWheresArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListTwoArgWheres"), Benchmark]
	public int ListTwoArgWheres_Prepared() {
		using var r = _listTwoArgWheresPrepared.ExecutePooled(_listTwoArgWheresArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListTwoArgWheres"), Benchmark]
	public int ListTwoArgWheres_Frozen() {
		using var r = _listTwoArgWheresFrozen.ExecutePooled(_listTwoArgWheresArgs);
		return r.Count;
	}

	// Two list steps. Group buckets hold 1k rows, Tier buckets 100; group 13 ∩ tier 13 is the 100
	// tier-13 rows. Declared large-then-small (13) and small-then-large (14). `_Frozen` is the pipeline's
	// free seed — the default: the smaller bucket seeds whatever the declared order, rows in that bucket's
	// order — and `_FrozenEagerOrder` the PreserveEagerOrder opt-out, which pins the seed back to the first
	// declared step and so walks the 1k group bucket for (13).

	// 13. list(1k) ∩ list(100)
	[BenchmarkCategory("ListList"), Benchmark(Baseline = true)]
	public int ListList_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _listListArgs.group).UseIndex(_byTier, _listListArgs.tier).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListList"), Benchmark]
	public int ListList_Prepared() {
		using var r = _listListPrepared.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListList"), Benchmark]
	public int ListList_Frozen() {
		using var r = _listListFrozen.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListList"), Benchmark]
	public int ListList_FrozenEagerOrder() {
		using var r = _listListFrozenEagerOrder.ExecutePooled(_listListArgs);
		return r.Count;
	}

	// 14. list(100) ∩ list(1k) — the declared order is already the best one
	[BenchmarkCategory("ListListReversed"), Benchmark(Baseline = true)]
	public int ListListReversed_Eager() {
		using var r = _items.Query().UseIndex(_byTier, _listListArgs.tier).UseIndex(_byGroup, _listListArgs.group).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListListReversed"), Benchmark]
	public int ListListReversed_Prepared() {
		using var r = _listListReversedPrepared.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListListReversed"), Benchmark]
	public int ListListReversed_Frozen() {
		using var r = _listListReversedFrozen.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListListReversed"), Benchmark]
	public int ListListReversed_FrozenEagerOrder() {
		using var r = _listListReversedFrozenEagerOrder.ExecutePooled(_listListArgs);
		return r.Count;
	}

	// 14b. list(1k) ∩ list(333) ∩ list(111), largest first — production shape A's narrowing. The free seed
	// walks the lane bucket (smallest, declared last) and probes the group and the band on the value.
	[BenchmarkCategory("ListListList"), Benchmark(Baseline = true)]
	public int ListListList_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _threeListArgs.group).UseIndex(_byBand, _threeListArgs.band).UseIndex(_byLane, _threeListArgs.lane).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListListList"), Benchmark]
	public int ListListList_Prepared() {
		using var r = _listListListPrepared.ExecutePooled(_threeListArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListListList"), Benchmark]
	public int ListListList_Frozen() {
		using var r = _listListListFrozen.ExecutePooled(_threeListArgs);
		return r.Count;
	}

	// 14c. classic Sort (the struct PqbByScore over the random Score) over list(1k) ∩ list(100): the pipeline seeds
	// free (the tier bucket), the container sorts the 100 rows.
	[BenchmarkCategory("Sort_ListList"), Benchmark(Baseline = true)]
	public int Sort_ListList_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _listListArgs.group).UseIndex(_byTier, _listListArgs.tier).Sort(new PqbByScore()).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("Sort_ListList"), Benchmark]
	public int Sort_ListList_Prepared() {
		using var r = _sortListListPrepared.ExecutePooled(_listListArgs);
		return r.Count;
	}

	[BenchmarkCategory("Sort_ListList"), Benchmark]
	public int Sort_ListList_Frozen() {
		using var r = _sortListListFrozen.ExecutePooled(_listListArgs);
		return r.Count;
	}

	// 14d. SortBounded (the struct PqbByScore) over list(1k), page 0..20 — step 6: the pipeline's fixed seed feeds the eager
	// top-k container (a heap of 20 over the 1k bucket walk); eager copies the bucket into a candidate set first.
	[BenchmarkCategory("SortBounded"), Benchmark(Baseline = true)]
	public int SortBounded_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).SortBounded(new PqbByScore()).ExecutePooled(0, 20);
		return r.Count;
	}

	[BenchmarkCategory("SortBounded"), Benchmark]
	public int SortBounded_Prepared() {
		using var r = _sortBoundedPrepared.ExecutePooled(_group, 0, 20);
		return r.Count;
	}

	[BenchmarkCategory("SortBounded"), Benchmark]
	public int SortBounded_Frozen() {
		using var r = _sortBoundedFrozen.ExecutePooled(_group, 0, 20);
		return r.Count;
	}

	// 14e. SortBounded over list(1k) ∩ list(100), page 0..20: the small-probe seed walks the 100-row bucket, the heap sees 100 rows.
	[BenchmarkCategory("SortBounded_ListList"), Benchmark(Baseline = true)]
	public int SortBounded_ListList_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _listListArgs.group).UseIndex(_byTier, _listListArgs.tier).SortBounded(new PqbByScore()).ExecutePooled(0, 20);
		return r.Count;
	}

	[BenchmarkCategory("SortBounded_ListList"), Benchmark]
	public int SortBounded_ListList_Prepared() {
		using var r = _sortBoundedListListPrepared.ExecutePooled(_listListArgs, 0, 20);
		return r.Count;
	}

	[BenchmarkCategory("SortBounded_ListList"), Benchmark]
	public int SortBounded_ListList_Frozen() {
		using var r = _sortBoundedListListFrozen.ExecutePooled(_listListArgs, 0, 20);
		return r.Count;
	}

	// 14f. Count of the SortBounded plan: the sorter never changes the count — the free seed, no container.
	[BenchmarkCategory("Count_SortBounded"), Benchmark(Baseline = true)]
	public int Count_SortBounded_Eager() => _items.Query().UseIndex(_byGroup, _group).SortBounded(new PqbByScore()).Count();

	[BenchmarkCategory("Count_SortBounded"), Benchmark]
	public int Count_SortBounded_Prepared() => _sortBoundedPrepared.Count(_group);

	[BenchmarkCategory("Count_SortBounded"), Benchmark]
	public int Count_SortBounded_Frozen() => _sortBoundedFrozen.Count(_group);

	// 15. list(1k) ∩ range(60k codes) — stage 3: the pipeline probes the range on the fetched value instead of walking the 60k window
	[BenchmarkCategory("ListRange"), Benchmark(Baseline = true)]
	public int ListRange_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _listRangeArgs.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), _listRangeArgs).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListRange"), Benchmark]
	public int ListRange_Prepared() {
		using var r = _listRangePrepared.ExecutePooled(_listRangeArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListRange"), Benchmark]
	public int ListRange_Frozen() {
		using var r = _listRangeFrozen.ExecutePooled(_listRangeArgs);
		return r.Count;
	}

	// ── Stage 3 ───────────────────────────────────────────────────────────────────

	// 16. list(1k) ∩ key-set (a third of all rows): eager copies the bucket and probes the locked key set; the pipeline compares the flag on the value.
	[BenchmarkCategory("ListKeySet"), Benchmark(Baseline = true)]
	public int ListKeySet_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _group).UseIndex(_flagged).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListKeySet"), Benchmark]
	public int ListKeySet_Prepared() {
		using var r = _listKeySetPrepared.ExecutePooled(_group);
		return r.Count;
	}

	[BenchmarkCategory("ListKeySet"), Benchmark]
	public int ListKeySet_Frozen() {
		using var r = _listKeySetFrozen.ExecutePooled(_group);
		return r.Count;
	}

	// 17. list(1k) ∩ updated-after covering half the rows: eager walks 50k index keys; the pipeline reads each candidate's timestamp.
	[BenchmarkCategory("ListLastUpdated"), Benchmark(Baseline = true)]
	public int ListLastUpdated_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _listLastUpdatedArgs.group).UseIndex(_itemsUpdated, _listLastUpdatedArgs.after).ExecutePooled();
		return r.Count;
	}

	[BenchmarkCategory("ListLastUpdated"), Benchmark]
	public int ListLastUpdated_Prepared() {
		using var r = _listLastUpdatedPrepared.ExecutePooled(_listLastUpdatedArgs);
		return r.Count;
	}

	[BenchmarkCategory("ListLastUpdated"), Benchmark]
	public int ListLastUpdated_Frozen() {
		using var r = _listLastUpdatedFrozen.ExecutePooled(_listLastUpdatedArgs);
		return r.Count;
	}

	// 18–20. Count twins: no container, the seed copy plus one store lookup per candidate.
	[BenchmarkCategory("Count_ListWhere"), Benchmark(Baseline = true)]
	public int Count_ListWhere_Eager() => _items.Query().UseIndex(_byGroup, _group).Where(static v => v.Flag).Count();

	[BenchmarkCategory("Count_ListWhere"), Benchmark]
	public int Count_ListWhere_Prepared() => _listWherePrepared.Count(_group);

	[BenchmarkCategory("Count_ListWhere"), Benchmark]
	public int Count_ListWhere_Frozen() => _listWhereFrozen.Count(_group);

	[BenchmarkCategory("Count_ListList"), Benchmark(Baseline = true)]
	public int Count_ListList_Eager() => _items.Query().UseIndex(_byGroup, _listListArgs.group).UseIndex(_byTier, _listListArgs.tier).Count();

	[BenchmarkCategory("Count_ListList"), Benchmark]
	public int Count_ListList_Prepared() => _listListPrepared.Count(_listListArgs);

	[BenchmarkCategory("Count_ListList"), Benchmark]
	public int Count_ListList_Frozen() => _listListFrozen.Count(_listListArgs);

	[BenchmarkCategory("Count_ListRange"), Benchmark(Baseline = true)]
	public int Count_ListRange_Eager() => _items.Query().UseIndex(_byGroup, _listRangeArgs.group).UseIndex(_codeRange, static (rb, a) => rb.Gte(a.lo).Lt(a.hi), _listRangeArgs).Count();

	[BenchmarkCategory("Count_ListRange"), Benchmark]
	public int Count_ListRange_Prepared() => _listRangePrepared.Count(_listRangeArgs);

	[BenchmarkCategory("Count_ListRange"), Benchmark]
	public int Count_ListRange_Frozen() => _listRangeFrozen.Count(_listRangeArgs);

	// ── Production shapes (step 6: the frozen rows take the joined pipeline — the seed pass feeds the eager bounded base container, the joins fill the page) ──

	// A. three list indexes, largest bucket first (1k ∩ 333 ∩ 111 → 111 rows), SortBounded page 20..40 with a tying
	// struct comparer, JoinOne 1:1 by PK (every fourth right row missing).
	[BenchmarkCategory("ListListListSortBoundedJoinOne"), Benchmark(Baseline = true)]
	public int ListListListSortBoundedJoinOne_Eager() {
		using var r = _items.Query().UseIndex(_byGroup, _threeListArgs.group).UseIndex(_byBand, _threeListArgs.band).UseIndex(_byLane, _threeListArgs.lane)
			.SortBounded(new PqbByScoreTies()).JoinOne(_details).ExecutePooled(20, 20);
		return r.Count;
	}

	[BenchmarkCategory("ListListListSortBoundedJoinOne"), Benchmark]
	public int ListListListSortBoundedJoinOne_Prepared() {
		using var r = _threeListSortJoinPrepared.ExecutePooled(_threeListArgs, 20, 20);
		return r.Count;
	}

	[BenchmarkCategory("ListListListSortBoundedJoinOne"), Benchmark]
	public int ListListListSortBoundedJoinOne_Frozen() {
		using var r = _threeListSortJoinFrozen.ExecutePooled(_threeListArgs, 20, 20);
		return r.Count;
	}

	// The joined Count: CountCoreJoined — an outer JoinOne counts every left, so this is the three-way intersection's size.
	[BenchmarkCategory("Count_ListListListSortBoundedJoinOne"), Benchmark(Baseline = true)]
	public int Count_ListListListSortBoundedJoinOne_Eager()
		=> _items.Query().UseIndex(_byGroup, _threeListArgs.group).UseIndex(_byBand, _threeListArgs.band).UseIndex(_byLane, _threeListArgs.lane).SortBounded(new PqbByScoreTies()).JoinOne(_details).Count();

	[BenchmarkCategory("Count_ListListListSortBoundedJoinOne"), Benchmark]
	public int Count_ListListListSortBoundedJoinOne_Prepared() => _threeListSortJoinPrepared.Count(_threeListArgs);

	[BenchmarkCategory("Count_ListListListSortBoundedJoinOne"), Benchmark]
	public int Count_ListListListSortBoundedJoinOne_Frozen() => _threeListSortJoinFrozen.Count(_threeListArgs);

	// B. 10k records: newer than T (last-updated index; ~15% of rows) ∩ hot list key A (a third) ∩ hot list key B (a fifth)
	// → ~100 rows, SortBounded page 20..40, two chained JoinOnes 1:1 by FK (2k customers, 500 products).
	[BenchmarkCategory("TimeWindowListListSortBoundedJoinTwo"), Benchmark(Baseline = true)]
	public int TimeWindowListListSortBoundedJoinTwo_Eager() {
		using var r = _records.Query().UseIndex(_recordsUpdated, _timeWindowArgs.t).UseIndex(_byKeyA, _timeWindowArgs.keyA).UseIndex(_byKeyB, _timeWindowArgs.keyB)
			.SortBounded(new PqbRecordByScoreTies()).JoinOne(_recByCustomer, _recCustomers).JoinOne(_recByProduct, _recProducts).ExecutePooled(20, 20);
		return r.Count;
	}

	[BenchmarkCategory("TimeWindowListListSortBoundedJoinTwo"), Benchmark]
	public int TimeWindowListListSortBoundedJoinTwo_Prepared() {
		using var r = _timeWindowPrepared.ExecutePooled(_timeWindowArgs, 20, 20);
		return r.Count;
	}

	[BenchmarkCategory("TimeWindowListListSortBoundedJoinTwo"), Benchmark]
	public int TimeWindowListListSortBoundedJoinTwo_Frozen() {
		using var r = _timeWindowFrozen.ExecutePooled(_timeWindowArgs, 20, 20);
		return r.Count;
	}

	// The two list steps probed on their buckets (gate-pinned Contains) before the store lookup instead of on
	// the fetched value: the window's ~1.4k non-survivors skip the lookup. Evidence for the probe-placement
	// lever (design §4 / §5.3), not the default (IndexSideProbes is the eager staleness window, §14.1).
	[BenchmarkCategory("TimeWindowListListSortBoundedJoinTwo"), Benchmark]
	public int TimeWindowListListSortBoundedJoinTwo_FrozenIndexSide() {
		using var r = _timeWindowFrozenIndexSide.ExecutePooled(_timeWindowArgs, 20, 20);
		return r.Count;
	}

	// B without its joins: the narrowing and the page alone, so the two join fills' share of the joined row
	// is the difference — the baseline for the JoinOne fusion of design step 5.
	[BenchmarkCategory("TimeWindowListListSortBounded"), Benchmark(Baseline = true)]
	public int TimeWindowListListSortBounded_Eager() {
		using var r = _records.Query().UseIndex(_recordsUpdated, _timeWindowArgs.t).UseIndex(_byKeyA, _timeWindowArgs.keyA).UseIndex(_byKeyB, _timeWindowArgs.keyB)
			.SortBounded(new PqbRecordByScoreTies()).ExecutePooled(20, 20);
		return r.Count;
	}

	[BenchmarkCategory("TimeWindowListListSortBounded"), Benchmark]
	public int TimeWindowListListSortBounded_Frozen() {
		using var r = _timeWindowNoJoinFrozen.ExecutePooled(_timeWindowArgs, 20, 20);
		return r.Count;
	}

	[BenchmarkCategory("Count_TimeWindowListListSortBoundedJoinTwo"), Benchmark(Baseline = true)]
	public int Count_TimeWindowListListSortBoundedJoinTwo_Eager()
		=> _records.Query().UseIndex(_recordsUpdated, _timeWindowArgs.t).UseIndex(_byKeyA, _timeWindowArgs.keyA).UseIndex(_byKeyB, _timeWindowArgs.keyB)
			.SortBounded(new PqbRecordByScoreTies()).JoinOne(_recByCustomer, _recCustomers).JoinOne(_recByProduct, _recProducts).Count();

	[BenchmarkCategory("Count_TimeWindowListListSortBoundedJoinTwo"), Benchmark]
	public int Count_TimeWindowListListSortBoundedJoinTwo_Prepared() => _timeWindowPrepared.Count(_timeWindowArgs);

	[BenchmarkCategory("Count_TimeWindowListListSortBoundedJoinTwo"), Benchmark]
	public int Count_TimeWindowListListSortBoundedJoinTwo_Frozen() => _timeWindowFrozen.Count(_timeWindowArgs);

	// B'. The same shape with a range index on the timestamp property instead of the last-updated index.
	[BenchmarkCategory("TimeRangeListListSortBoundedJoinTwo"), Benchmark(Baseline = true)]
	public int TimeRangeListListSortBoundedJoinTwo_Eager() {
		using var r = _records.Query().UseIndex(_tsRange, static (rb, a) => rb.Gt(a.t), _timeWindowArgs).UseIndex(_byKeyA, _timeWindowArgs.keyA).UseIndex(_byKeyB, _timeWindowArgs.keyB)
			.SortBounded(new PqbRecordByScoreTies()).JoinOne(_recByCustomer, _recCustomers).JoinOne(_recByProduct, _recProducts).ExecutePooled(20, 20);
		return r.Count;
	}

	[BenchmarkCategory("TimeRangeListListSortBoundedJoinTwo"), Benchmark]
	public int TimeRangeListListSortBoundedJoinTwo_Prepared() {
		using var r = _timeRangePrepared.ExecutePooled(_timeWindowArgs, 20, 20);
		return r.Count;
	}

	[BenchmarkCategory("TimeRangeListListSortBoundedJoinTwo"), Benchmark]
	public int TimeRangeListListSortBoundedJoinTwo_Frozen() {
		using var r = _timeRangeFrozen.ExecutePooled(_timeWindowArgs, 20, 20);
		return r.Count;
	}

	[BenchmarkCategory("Count_TimeRangeListListSortBoundedJoinTwo"), Benchmark(Baseline = true)]
	public int Count_TimeRangeListListSortBoundedJoinTwo_Eager()
		=> _records.Query().UseIndex(_tsRange, static (rb, a) => rb.Gt(a.t), _timeWindowArgs).UseIndex(_byKeyA, _timeWindowArgs.keyA).UseIndex(_byKeyB, _timeWindowArgs.keyB)
			.SortBounded(new PqbRecordByScoreTies()).JoinOne(_recByCustomer, _recCustomers).JoinOne(_recByProduct, _recProducts).Count();

	[BenchmarkCategory("Count_TimeRangeListListSortBoundedJoinTwo"), Benchmark]
	public int Count_TimeRangeListListSortBoundedJoinTwo_Prepared() => _timeRangePrepared.Count(_timeWindowArgs);

	[BenchmarkCategory("Count_TimeRangeListListSortBoundedJoinTwo"), Benchmark]
	public int Count_TimeRangeListListSortBoundedJoinTwo_Frozen() => _timeRangeFrozen.Count(_timeWindowArgs);
}

public sealed class PqbRecord : ICacheEquatable<PqbRecord>, ICacheClonable<PqbRecord> {
	public int Id { get; init; }
	public int KeyA { get; init; }
	public int KeyB { get; init; }
	public long Ts { get; init; }
	public int CustomerId { get; init; }
	public int ProductId { get; init; }
	public int Score { get; init; }

	public bool CacheEquals(PqbRecord? other)
		=> other is not null && other.Id == Id && other.KeyA == KeyA && other.KeyB == KeyB && other.Ts == Ts && other.CustomerId == CustomerId && other.ProductId == ProductId && other.Score == Score;

	public int CacheGetHashCode() => HashCode.Combine(Id, KeyA, KeyB, Ts, CustomerId, ProductId, Score);

	public PqbRecord Clone() => new() { Id = Id, KeyA = KeyA, KeyB = KeyB, Ts = Ts, CustomerId = CustomerId, ProductId = ProductId, Score = Score };
}

public sealed class PqbProduct : ICacheEquatable<PqbProduct>, ICacheClonable<PqbProduct> {
	public int Id { get; init; }
	public string Category { get; init; } = "";

	public bool CacheEquals(PqbProduct? other) => other is not null && other.Id == Id && other.Category == Category;

	public int CacheGetHashCode() => HashCode.Combine(Id, Category);

	public PqbProduct Clone() => new() { Id = Id, Category = Category };
}

// Tying struct comparers (eight distinct sort keys): SortBounded breaks ties by encounter order.
public readonly struct PqbByScoreTies : IComparer<PqbItem> {
	public int Compare(PqbItem? x, PqbItem? y) => ((x?.Score ?? 0) & 7).CompareTo((y?.Score ?? 0) & 7);
}

public readonly struct PqbRecordByScoreTies : IComparer<PqbRecord> {
	public int Compare(PqbRecord? x, PqbRecord? y) => ((x?.Score ?? 0) & 7).CompareTo((y?.Score ?? 0) & 7);
}

// JoinMany right sides (step 8).
public sealed class PqbLine : ICacheEquatable<PqbLine>, ICacheClonable<PqbLine> {
	public int Id { get; init; }
	public int ItemId { get; init; }
	public int Qty { get; init; }

	public bool CacheEquals(PqbLine? other) => other is not null && other.Id == Id && other.ItemId == ItemId && other.Qty == Qty;

	public int CacheGetHashCode() => HashCode.Combine(Id, ItemId, Qty);

	public PqbLine Clone() => new() { Id = Id, ItemId = ItemId, Qty = Qty };
}

public sealed class PqbNote : ICacheEquatable<PqbNote>, ICacheClonable<PqbNote> {
	public int Id { get; init; }
	public int CustomerId { get; init; }
	public string Text { get; init; } = "";

	public bool CacheEquals(PqbNote? other) => other is not null && other.Id == Id && other.CustomerId == CustomerId && other.Text == Text;

	public int CacheGetHashCode() => HashCode.Combine(Id, CustomerId, Text);

	public PqbNote Clone() => new() { Id = Id, CustomerId = CustomerId, Text = Text };
}

public sealed class PqbDoc : ICacheEquatable<PqbDoc>, ICacheClonable<PqbDoc> {
	public int Id { get; init; }
	public int Group { get; init; }
	public List<int> TagIds { get; init; } = [];

	public bool CacheEquals(PqbDoc? other) => other is not null && other.Id == Id && other.Group == Group && other.TagIds.SequenceEqual(TagIds);

	public int CacheGetHashCode() => HashCode.Combine(Id, Group, TagIds.Count);

	public PqbDoc Clone() => new() { Id = Id, Group = Group, TagIds = [.. TagIds] };
}

public sealed class PqbTag : ICacheEquatable<PqbTag>, ICacheClonable<PqbTag> {
	public int Id { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(PqbTag? other) => other is not null && other.Id == Id && other.Name == Name;

	public int CacheGetHashCode() => HashCode.Combine(Id, Name);

	public PqbTag Clone() => new() { Id = Id, Name = Name };
}
