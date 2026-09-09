namespace Prague.Core;

using System.Buffers;
using System.Runtime.CompilerServices;
using Prague.Core.Collections;
using Prague.Core.TypeSystem;
using Prague.Core.Utils;

// ── JoinMany right-list-index resolver ────────────────────────────────────
//
// Mirrors JoinOneRightUniqueIndexResolver structurally but over a list-valued
// FK index (CacheKeyValueListIndex) instead of a unique one. Per-left fan-in
// to QueryResults<TRightValue> uses the standard keyed-init container protocol
// (Init/Seal/PrepareSharedBuffer/Add) — set up BEFORE the paired execute walks
// pairs and dispatches Add calls. Pairs are recorded into a JoinManyFanOut so a
// right that several lefts reach — a non-injective key selector folding lefts
// onto one bucket, or a collection-backed index whose buckets overlap — is
// delivered to every one of them; in the plain FK shape (every right belongs to
// one left) the fan-out writes nothing but the pair set and the execute runs
// straight from the store. Filter narrowing can only reduce the pair set, so
// the recorded counts are upper bounds (slack capacity is OK).

/// <summary>
/// Resolver for JoinMany over a right-side <see cref="CacheKeyValueListIndex{TKey,TValue,TIndexKey}"/>.
/// Classic OneToMany direction — each leftKey maps to many rightKeys via the index bucket.
/// </summary>
/// <typeparam name="TLeftKey">Left cache's key type.</typeparam>
/// <typeparam name="TLeftValue">Left cache's value type.</typeparam>
/// <typeparam name="TRightCache">Right cache wrapper.</typeparam>
/// <typeparam name="TRightKey">Right cache's key type.</typeparam>
/// <typeparam name="TIndexKey">Right index's lookup key (== TLeftKey for identity selector).</typeparam>
/// <typeparam name="TRightValue">Right cache's value type (also the inner type of QueryResults).</typeparam>
/// <typeparam name="TFilter">Filter strategy struct over the paired non-executable builder.</typeparam>
/// <typeparam name="TSelector">Key-selector strategy: TLeftKey → TIndexKey.</typeparam>
public struct JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue, TFilter, TSelector>
	: IJoinManyResolver<TLeftKey, TLeftValue, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>
	where TIndexKey : notnull, IEquatable<TIndexKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
	where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
	where TFilter : struct, IJoinFilter<
		CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>
	where TSelector : struct, IKeySelector<TLeftKey, TIndexKey> {

	// ── Fields ───────────────────────────────────────────────────────────────

	private readonly TRightCache _rightCache;
	private readonly CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> _rightIndex;
	private TFilter _filter;
	private TSelector _selector;
	private readonly bool _isInner;

	static JoinManyRightListIndexResolver() {
		SlotCloner<QueryResults<TRightValue>>.Register(
			static (ref QueryResults<TRightValue> v) => v.CloneElements());
	}

	internal JoinManyRightListIndexResolver(
		TRightCache rightCache,
		CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
		TFilter filter,
		TSelector selector,
		bool isInner = false) {
		_rightCache = rightCache;
		_rightIndex = rightIndex;
		_filter = filter;
		_selector = selector;
		_isInner = isInner;
	}

	// ── Static / property values ─────────────────────────────────────────────

	public static bool IsSorter { get; } = false;
	public bool Inner => _isInner;

	// ── Clone / CloneValue ───────────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult {
		ref var item = ref value.TUnsafeGetValAt<QueryResults<TRightValue>>(index);
		item.CloneInPlace();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneValue(ref QueryResults<TRightValue> value) => value.CloneElements();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref QueryResults<TRightValue> value) => CloneValue(ref value);

	// ── Keyed-init container ────────────────────────────────────────────────
	//
	// Mirrors the legacy ManyResolver.UnsafeResolverContainer (Resolvers.cs:292-349).
	// Init/Seal/PrepareSharedBuffer/GetSharedBuffer drive per-leftKey QueryResults
	// buffer allocation; Add fills slots once pairs are walked.

	private ref struct UnsafeResolverContainer<TAccessor> : IJoinedKeyedResultContainer<TLeftKey, TRightValue>
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {
		private readonly bool _cloneOnAdd;
		private readonly bool _shouldPool;
		private int _totalCount;
		private TRightValue[]? _sharedBuffer;
		private TAccessor _accessor;

		public int TotalCount => _totalCount;

		public UnsafeResolverContainer(TAccessor accessor, bool cloneOnAdd, bool shouldPool) {
			_accessor = accessor;
			_cloneOnAdd = cloneOnAdd;
			_shouldPool = shouldPool;
			_totalCount = 0;
			_sharedBuffer = null;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Init(TLeftKey foreignKey, int maxCount) {
			ref var valueRef = ref _accessor.GetValueRef<TLeftKey, QueryResults<TRightValue>>(foreignKey);
			if (!Unsafe.IsNullRef(in valueRef)) {
				valueRef.SetPendingCapacity(maxCount);
				_totalCount += maxCount;
			}
		}

		public void Seal(TLeftKey foreignKey, int actualCount) {
		}

		public void PrepareSharedBuffer() {
			_sharedBuffer = _shouldPool ? PragueArrayPool<TRightValue>.Pool.Rent(_totalCount) : new TRightValue[_totalCount];
			var offset = 0;
			var keys = _accessor.GetKeys<TLeftKey>();
			for (var i = 0; i < keys.Length; i++) {
				ref var valueRef = ref _accessor.GetValueRef<TLeftKey, QueryResults<TRightValue>>(keys[i]);
				if (!Unsafe.IsNullRef(in valueRef))
					offset = valueRef.AssignSharedBuffer(_sharedBuffer, offset);
			}
		}

		public TRightValue[]? GetSharedBuffer() => _shouldPool ? _sharedBuffer : null;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(TLeftKey foreignKey, TRightValue result) {
			ref var valueRef = ref _accessor.GetValueRef<TLeftKey, QueryResults<TRightValue>>(foreignKey);
			if (!Unsafe.IsNullRef(in valueRef))
				return valueRef.UnsafeAdd(_cloneOnAdd ? result.Clone() : result);
			return 0;
		}
	}

	// ── Core execution loop ──────────────────────────────────────────────────

	/// <summary>
	/// One paired execute over every recorded right: builds the paired core over the fan-out's pair
	/// set, applies the user filter once, then hands the set to the core and delivers each surviving
	/// right to all of its lefts — through the plain paired execute when no right is shared (the FK
	/// shape), through a <see cref="JoinManyFanOut{TLeftKey,TRightKey}.Delivery{TRightValue,TContainer}"/>
	/// otherwise. The set is marked handed off right before the execute — after the filter ran — so a
	/// throw inside the user lambda leaves it to <see cref="JoinManyFanOut{TLeftKey,TRightKey}.Dispose"/>,
	/// while a set that reached the core is never disposed twice.
	/// </summary>
	private void ExecutePairs<TContainer>(ref JoinManyFanOut<TLeftKey, TRightKey> fanOut, ref TContainer container)
		where TContainer : struct, IJoinedResultContainer<TLeftKey, TRightValue>, allows ref struct {
		if (fanOut.DistinctRights == 0) {
			return;
		}

		var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>(_rightCache.Cache, fanOut.Pairs);
		var builder = new CacheQueryBuilderCombined<
			NonExecutableQuery<TRightCache>,
			PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>,
			TRightKey, TRightValue,
			Resolvers<BaseResolver<TRightKey, TRightValue>>,
			TRightValue>(
			new NonExecutableQuery<TRightCache>(_rightCache),
			pairedCore,
			new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
			0);

		// NoFilter.Apply() is JIT-elided. The filter narrows pairs in place; it cannot add or renumber
		// them. A throw here (user lambda) must leave disposal to the fan-out.
		builder = _filter.Apply(builder);

		fanOut.MarkPairsHandedOff();
		ref var core = ref Unsafe.AsRef(in builder._leftQuery);
		if (fanOut.SingleLeftPerRight) {
			// No right is shared, so a pair's JoinedKey is its whole chain: the plain paired execute (the
			// store calls Add(pair.JoinedKey, value)) delivers exactly what a Delivery would, without the
			// wrapper or the chain walk.
			core.ExecutePaired(ref container);
			return;
		}

		var delivery = new JoinManyFanOut<TLeftKey, TRightKey>.Delivery<TRightValue, TContainer>(
			container, fanOut.Heads, fanOut.NodeLefts, fanOut.NodeNexts);
		core.ExecutePairedJoined(ref delivery);
		container = delivery.Inner;
	}

	/// <summary>
	/// Walks the right index per leftKey, records its (leftKey, rightKey) pairs, calls
	/// <c>container.Init(leftKey, pairsRecorded)</c> for keyed-init, then <c>PrepareSharedBuffer</c>,
	/// then runs the single paired execute which dispatches <c>Add(leftKey, rightValue)</c> per pair. A
	/// right shared by several lefts — a non-injective selector folding lefts onto one bucket, or a
	/// collection-backed index whose buckets overlap — reaches every left that recorded it. Filter
	/// narrowing can only reduce pairs — the recorded counts are upper bounds.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteReverse<TContainer>(ref TContainer container, ReadOnlySpan<TLeftKey> leftKeys)
		where TContainer : struct, IJoinedKeyedResultContainer<TLeftKey, TRightValue>, allows ref struct {

		if (leftKeys.IsEmpty)
			return;

		// ── 1. Walk index, keyed-init container, record the pairs ─────────────
		// Inline rather than calling _rightIndex.IntersectValuesInit because that
		// helper requires the container to be parameterized on TIndexKey (not
		// TLeftKey), and reinterpreting refs to ref structs across generic params
		// is brittle. The inline loop is identical work and avoids the gymnastics.
		var fanOut = new JoinManyFanOut<TLeftKey, TRightKey>(leftKeys.Length);
		try {
			foreach (var leftKey in leftKeys) {
				TIndexKey indexKey;
				if (TSelector.IsIdentity)
					indexKey = Unsafe.As<TLeftKey, TIndexKey>(ref Unsafe.AsRef(in leftKey));
				else
					indexKey = _selector.Select(leftKey);

				var bucket = _rightIndex.GetValuesUnsafe(indexKey);
				if (bucket is null || bucket.Count == 0)
					continue;

				// Size the slot from the pairs this walk recorded, not from bucket.Count: the bucket
				// is written concurrently, and a right published between the two reads would leave
				// the slot short of the pairs the execute later delivers to it.
				container.Init(leftKey, fanOut.RecordBucket(leftKey, bucket));
			}

			// PrepareSharedBuffer allocates the contiguous TRightValue[] partitioned per-leftKey.
			container.PrepareSharedBuffer();

			// ── 2. One paired execute — walks the pairs, calls container.Add ──────
			ExecutePairs(ref fanOut, ref container);
		}
		finally {
			fanOut.Dispose();
		}
	}

	void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(
		ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		// Pool the per-left child buffer only when a disposer exists to return it (pooled execution).
		var container = new UnsafeResolverContainer<TAccessor>(accessor, cloneOnAdd, disposer.IsActive);
		try {
			ExecuteReverse(ref container, accessor.GetKeys<TLeftKey>());
		} finally {
			// Register even when the user filter / paired execute throws mid-ExecuteReverse —
			// the buffer is rented before that user code runs, and only the disposer (fired
			// by the pipeline's container cleanup) can still return it on that path.
			RegisterPooledBuffer(ref disposer, container.GetSharedBuffer());
		}
	}

	void IJoinManyResolver<TLeftKey, TLeftValue, TRightValue>.ExecuteReverseMany<TContainer>(
		ref TContainer container, ReadOnlySpan<TLeftKey> keys) {
		ExecuteReverse(ref container, keys);
	}

	// Register the rented contiguous child buffer for return to the pool on result Dispose.
	// disposer is non-null exactly for pooled execution; a zero-length buffer (Rent(0) → Array.Empty)
	// must not be returned.
	private static void RegisterPooledBuffer(ref QueryResultsDisposer disposer, TRightValue[]? buffer) {
		if (disposer.IsActive && buffer is { Length: > 0 })
			disposer.AddPooledBuffer(buffer);
	}

	// ── IndexedInner: drop lefts with empty QueryResults ────────────────────

	/// <summary>
	/// Triggers the outer-executor's auto-populate-from-leftCache when no prior
	/// <c>UseIndex</c> narrowed the candidate set. Mirrors the JoinOne pattern.
	/// </summary>
	void IJoinResolver.PrepareIndexedInner<TExecutor>(
		ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		_ = leftQuery.GetCandidates<TLeftKey>();
	}

	/// <summary>
	/// Inner-join attach path: same keyed-init + paired execute as outer, then post-walk
	/// <c>accessor.RetainNonEmptyManySlots</c> drops result-map entries whose per-left
	/// <see cref="QueryResults{TRightValue}"/> has <c>Count == 0</c> (no rights matched, or
	/// filter rejected them all) and narrows <c>leftQuery.Candidates</c> to surviving keys.
	/// </summary>
	void IJoinResolver.UnsafeExecuteIndexedInner<TAccessor, TExecutor>(
		ref TAccessor accessor,
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool isFirst,
		ref QueryResultsDisposer disposer) {
		ref var candidates = ref leftQuery.GetCandidates<TLeftKey>();
		if (!candidates.IsInitlized || candidates.Count == 0)
			return;

		// Same flow as outer ExecuteReverse, but operate on candidates (not accessor.GetKeys).
		var fanOut = new JoinManyFanOut<TLeftKey, TRightKey>(candidates.Count);
		var container = new UnsafeResolverContainer<TAccessor>(accessor, cloneOnAdd, disposer.IsActive);
		try {
			foreach (var leftKey in candidates) {
				TIndexKey indexKey = TSelector.IsIdentity
					? Unsafe.As<TLeftKey, TIndexKey>(ref Unsafe.AsRef(in leftKey))
					: _selector.Select(leftKey);

				var bucket = _rightIndex.GetValuesUnsafe(indexKey);
				if (bucket is null || bucket.Count == 0)
					continue;

				// For inner mode, the slot doesn't pre-exist (no outer base execute ran yet).
				// Materialise it via GetValueRefOrAddDefault BEFORE Init — otherwise Init's
				// GetValueRef call returns NullRef and silently skips _totalCount tracking,
				// leaving PrepareSharedBuffer with a zero-length buffer.
				_ = accessor.GetValueRefOrAddDefault<TLeftKey, QueryResults<TRightValue>>(leftKey, out _);
				// Sized from the recorded pairs, not bucket.Count — see ExecuteReverse.
				container.Init(leftKey, fanOut.RecordBucket(leftKey, bucket));
			}

			if (fanOut.DistinctRights == 0) {
				// No left has any right — Inner semantic narrows to empty.
				candidates.IntersectWith(ReadOnlySpan<TLeftKey>.Empty);
				return;
			}

			container.PrepareSharedBuffer();
			RegisterPooledBuffer(ref disposer, container.GetSharedBuffer());

			// One paired execute (Add per surviving right, to every left that recorded it).
			ExecutePairs(ref fanOut, ref container);

			// Post-walk: drop slots with QueryResults.Count == 0 (no rights or all filtered out)
			// and narrow candidates to surviving keys. Subsequent base execute fills Left for survivors.
			accessor.RetainNonEmptyManySlots<TLeftKey, TRightValue>(ref candidates);
		}
		finally {
			fanOut.Dispose();
		}
	}
}

// ── JoinMany left-symmetric-index resolver ────────────────────────────────
//
// Lefts reach their rights through a lookup group: the left symmetric index's
// Reverse half maps a left to its lookup key, the (optionally selector-translated)
// lookup key addresses a bucket of right keys in the right list index. Every left
// of a group shares that bucket, and a non-injective selector folds several groups
// onto one bucket — so pairs sharing a right key are the norm here and the plain
// right-key-identity pair set cannot hold them all. The resolver records every
// (left, right) pair into a JoinManyFanOut — one pair per distinct right plus a chain
// of the lefts sharing it — and runs ONE paired execute that delivers each surviving
// right to every left in its chain. Each left's bucket is enumerated exactly once and
// Init(left) receives exactly the number of pairs recorded for it, so a slot can never
// receive more Adds than it reserved, whatever the index writer does concurrently. A
// right whose bucket membership is removed and re-added while that left's bucket is
// being walked can be yielded twice by the enumerator; the fan-out sees the repeat (the
// right's latest recorder is this left), records it once and delivers it once.

/// <summary>
/// Resolver for JoinMany driven by a symmetric many index on the LEFT side plus a list index on
/// the RIGHT side. Per input left: <c>Reverse</c> → lookup key → (selector) → right bucket; each
/// (left, right) pair is recorded into the fan-out (one pair per distinct right, a chain of lefts
/// per pair), the slot is sized from the pairs recorded, and one paired execute delivers every
/// surviving right to all of its lefts. The user filter therefore runs once per query and its
/// predicate once per distinct right; rights inside a slot come out in pair-set order
/// (first-recording order of the distinct rights).
/// </summary>
public struct JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue, TFilter, TSelector>
	: IJoinManyResolver<TLeftKey, TLeftValue, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>
	where TLookupKey : notnull, IEquatable<TLookupKey>
	where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
	where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
	where TFilter : struct, IJoinFilter<
		CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>
	where TSelector : struct, IKeySelector<TLookupKey, TRightIndexKey> {

	// ── Fields ───────────────────────────────────────────────────────────────

	private readonly CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> _leftIndex;
	private readonly TRightCache _rightCache;
	private readonly CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> _rightIndex;
	private TFilter _filter;
	private TSelector _selector;
	private readonly bool _isInner;

	static JoinManyLeftSymResolver() {
		SlotCloner<QueryResults<TRightValue>>.Register(
			static (ref QueryResults<TRightValue> v) => v.CloneElements());
	}

	internal JoinManyLeftSymResolver(
		CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
		TRightCache rightCache,
		CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
		TFilter filter,
		TSelector selector,
		bool isInner = false) {
		_leftIndex = leftIndex;
		_rightCache = rightCache;
		_rightIndex = rightIndex;
		_filter = filter;
		_selector = selector;
		_isInner = isInner;
	}

	// ── Static / property values ─────────────────────────────────────────────

	public static bool IsSorter { get; } = false;
	public bool Inner => _isInner;

	// ── Clone / CloneValue ───────────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult {
		ref var item = ref value.TUnsafeGetValAt<QueryResults<TRightValue>>(index);
		item.CloneInPlace();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneValue(ref QueryResults<TRightValue> value) => value.CloneElements();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref QueryResults<TRightValue> value) => CloneValue(ref value);

	// ── Pair recording ───────────────────────────────────────────────────────

	/// <summary>
	/// Resolves a left to its right bucket: <c>Reverse</c> → lookup key → selector → right index
	/// key → bucket. False when the left has no lookup key or the bucket is empty.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private readonly bool TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket) {
		if (!_leftIndex.Reverse.TryGetValue(leftKey, out var lookupKey)) {
			bucket = null!;
			return false;
		}

		var rightIndexKey = TSelector.IsIdentity
			? Unsafe.As<TLookupKey, TRightIndexKey>(ref lookupKey)
			: _selector.Select(lookupKey);
		bucket = _rightIndex.GetValuesUnsafe(rightIndexKey);
		return bucket.Count > 0;
	}

	// ── Execution ────────────────────────────────────────────────────────────

	/// <summary>
	/// One paired execute over every recorded right: builds the paired core over the fan-out's pair
	/// set, applies the user filter once, then hands the set to the core and delivers each surviving
	/// right to all of its lefts — through the plain paired execute when no right is shared, through a
	/// <see cref="JoinManyFanOut{TLeftKey,TRightKey}.Delivery{TRightValue,TContainer}"/> otherwise.
	/// The set is marked handed off right before the execute — after the
	/// filter ran — so a throw inside the user lambda leaves it to
	/// <see cref="JoinManyFanOut{TLeftKey,TRightKey}.Dispose"/>, while a set that reached the core is
	/// never disposed twice.
	/// </summary>
	private void ExecutePairs<TContainer>(ref JoinManyFanOut<TLeftKey, TRightKey> fanOut, ref TContainer container)
		where TContainer : struct, IJoinedResultContainer<TLeftKey, TRightValue>, allows ref struct {
		if (fanOut.DistinctRights == 0) {
			return;
		}

		var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>(_rightCache.Cache, fanOut.Pairs);
		var builder = new CacheQueryBuilderCombined<
			NonExecutableQuery<TRightCache>,
			PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>,
			TRightKey, TRightValue,
			Resolvers<BaseResolver<TRightKey, TRightValue>>,
			TRightValue>(
			new NonExecutableQuery<TRightCache>(_rightCache),
			pairedCore,
			new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
			0);

		// NoFilter.Apply() is JIT-elided. The filter narrows pairs in place; it cannot add or renumber
		// them. A throw here (user lambda) must leave disposal to the fan-out.
		builder = _filter.Apply(builder);

		fanOut.MarkPairsHandedOff();
		ref var core = ref Unsafe.AsRef(in builder._leftQuery);
		if (fanOut.SingleLeftPerRight) {
			// No right is shared, so a pair's JoinedKey is its whole chain: the plain paired execute (the
			// store calls Add(pair.JoinedKey, value)) delivers exactly what a Delivery would, without the
			// wrapper or the chain walk.
			core.ExecutePaired(ref container);
			return;
		}

		var delivery = new JoinManyFanOut<TLeftKey, TRightKey>.Delivery<TRightValue, TContainer>(
			container, fanOut.Heads, fanOut.NodeLefts, fanOut.NodeNexts);
		core.ExecutePairedJoined(ref delivery);
		container = delivery.Inner;
	}

	// ── Core outer execution ─────────────────────────────────────────────────

	/// <summary>
	/// Walks input lefts: per left resolve the right bucket, record its pairs and <c>Init</c> the slot
	/// with the pairs recorded; then <c>PrepareSharedBuffer</c>, register the pooled buffer with
	/// <paramref name="disposer"/> (before any user code can throw) and run the single paired execute.
	/// </summary>
	private void ExecuteOuter<TContainer>(ref TContainer container, ReadOnlySpan<TLeftKey> leftKeys,
		ref QueryResultsDisposer disposer)
		where TContainer : struct, IJoinedKeyedResultContainer<TLeftKey, TRightValue>, allows ref struct {

		if (leftKeys.IsEmpty)
			return;

		var fanOut = new JoinManyFanOut<TLeftKey, TRightKey>(leftKeys.Length);
		try {
			foreach (var leftKey in leftKeys) {
				if (!TryGetBucket(leftKey, out var bucket))
					continue;

				var added = fanOut.RecordBucket(leftKey, bucket);
				if (added > 0)
					container.Init(leftKey, added);
			}

			// PrepareSharedBuffer allocates the contiguous TRightValue[] partitioned per left;
			// register it for pooled return BEFORE the execute so a throw inside the user filter
			// cannot strand the rental.
			container.PrepareSharedBuffer();
			RegisterPooledBuffer(ref disposer, container.GetSharedBuffer());

			ExecutePairs(ref fanOut, ref container);
		}
		finally {
			fanOut.Dispose();
		}
	}

	void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(
		ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		// Pool the per-left child buffer only when a disposer exists to return it (pooled execution).
		var inner = new InnerKeyedContainer<TAccessor>(accessor, cloneOnAdd, disposer.IsActive);
		ExecuteOuter(ref inner, accessor.GetKeys<TLeftKey>(), ref disposer);
	}

	void IJoinManyResolver<TLeftKey, TLeftValue, TRightValue>.ExecuteReverseMany<TContainer>(
		ref TContainer container, ReadOnlySpan<TLeftKey> keys) {
		// The caller owns the container's buffer; an inert disposer makes registration a no-op.
		var disposer = default(QueryResultsDisposer);
		ExecuteOuter(ref container, keys, ref disposer);
	}

	// Register the rented contiguous child buffer for return to the pool on result Dispose.
	private static void RegisterPooledBuffer(ref QueryResultsDisposer disposer, TRightValue[]? buffer) {
		if (disposer.IsActive && buffer is { Length: > 0 })
			disposer.AddPooledBuffer(buffer);
	}

	// ── IndexedInner ────────────────────────────────────────────────────────

	void IJoinResolver.PrepareIndexedInner<TExecutor>(
		ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		_ = leftQuery.GetCandidates<TLeftKey>();
	}

	/// <summary>
	/// Inner-join attach path: the same recording flow as <c>ExecuteOuter</c> over the candidate set,
	/// materialising each candidate's result slot before <c>Init</c>; a post-walk
	/// <c>RetainNonEmptyManySlots</c> drops lefts whose per-left <see cref="QueryResults{TRightValue}"/>
	/// stayed empty (no rights, or the filter rejected all of them) and narrows candidates to the
	/// survivors.
	/// </summary>
	void IJoinResolver.UnsafeExecuteIndexedInner<TAccessor, TExecutor>(
		ref TAccessor accessor,
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool isFirst,
		ref QueryResultsDisposer disposer) {
		ref var candidates = ref leftQuery.GetCandidates<TLeftKey>();
		if (!candidates.IsInitlized || candidates.Count == 0)
			return;

		var inner = new InnerKeyedContainer<TAccessor>(accessor, cloneOnAdd, disposer.IsActive);
		var fanOut = new JoinManyFanOut<TLeftKey, TRightKey>(candidates.Count);
		try {
			var anyPairs = false;
			foreach (var leftKey in candidates) {
				if (!TryGetBucket(leftKey, out var bucket))
					continue;

				// For inner mode, the slot doesn't pre-exist (no outer base execute ran yet).
				// Materialise it via GetValueRefOrAddDefault BEFORE Init — otherwise Init's
				// GetValueRef call returns NullRef and silently skips _totalCount tracking,
				// leaving PrepareSharedBuffer with a zero-length buffer.
				_ = accessor.GetValueRefOrAddDefault<TLeftKey, QueryResults<TRightValue>>(leftKey, out _);

				var added = fanOut.RecordBucket(leftKey, bucket);
				if (added > 0) {
					inner.Init(leftKey, added);
					anyPairs = true;
				}
			}

			if (!anyPairs) {
				// No left has any right — Inner semantic narrows to empty.
				candidates.IntersectWith(ReadOnlySpan<TLeftKey>.Empty);
				return;
			}

			inner.PrepareSharedBuffer();
			RegisterPooledBuffer(ref disposer, inner.GetSharedBuffer());

			ExecutePairs(ref fanOut, ref inner);

			// Post-walk: drop slots with QueryResults.Count == 0 (no rights or all filtered out)
			// and narrow candidates to surviving keys. Subsequent base execute fills Left for survivors.
			accessor.RetainNonEmptyManySlots<TLeftKey, TRightValue>(ref candidates);
		}
		finally {
			fanOut.Dispose();
		}
	}

	/// <summary>
	/// Keyed inner container used during execution. Same shape as the legacy ManyResolver's
	/// container — Init tracks per-left capacity, PrepareSharedBuffer allocates the contiguous
	/// TRightValue[] partition.
	/// </summary>
	private ref struct InnerKeyedContainer<TAccessor> : IJoinedKeyedResultContainer<TLeftKey, TRightValue>
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {
		private readonly bool _cloneOnAdd;
		private readonly bool _shouldPool;
		private int _totalCount;
		private TRightValue[]? _sharedBuffer;
		private TAccessor _accessor;

		public int TotalCount => _totalCount;

		public InnerKeyedContainer(TAccessor accessor, bool cloneOnAdd, bool shouldPool) {
			_accessor = accessor;
			_cloneOnAdd = cloneOnAdd;
			_shouldPool = shouldPool;
			_totalCount = 0;
			_sharedBuffer = null;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Init(TLeftKey foreignKey, int maxCount) {
			ref var valueRef = ref _accessor.GetValueRef<TLeftKey, QueryResults<TRightValue>>(foreignKey);
			if (!Unsafe.IsNullRef(in valueRef)) {
				valueRef.SetPendingCapacity(maxCount);
				_totalCount += maxCount;
			}
		}

		public void Seal(TLeftKey foreignKey, int actualCount) {
		}

		public void PrepareSharedBuffer() {
			_sharedBuffer = _shouldPool ? PragueArrayPool<TRightValue>.Pool.Rent(_totalCount) : new TRightValue[_totalCount];
			var offset = 0;
			var keys = _accessor.GetKeys<TLeftKey>();
			for (var i = 0; i < keys.Length; i++) {
				ref var valueRef = ref _accessor.GetValueRef<TLeftKey, QueryResults<TRightValue>>(keys[i]);
				if (!Unsafe.IsNullRef(in valueRef))
					offset = valueRef.AssignSharedBuffer(_sharedBuffer, offset);
			}
		}

		public TRightValue[]? GetSharedBuffer() => _shouldPool ? _sharedBuffer : null;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(TLeftKey foreignKey, TRightValue result) {
			ref var valueRef = ref _accessor.GetValueRef<TLeftKey, QueryResults<TRightValue>>(foreignKey);
			if (!Unsafe.IsNullRef(in valueRef))
				return valueRef.UnsafeAdd(_cloneOnAdd ? result.Clone() : result);
			return 0;
		}
	}
}

// ── JoinMany NESTED resolver ──────────────────────────────────────────────────
//
// True nested join: the Many side (e.g. Desk) is itself joined (e.g. Desk⋈Occupier),
// so each parent (Room) row carries QueryResults<JoinResult<Desk, Occupier?>>. The inner
// join is scoped to each Desk, NOT back to the Room.
//
// Shared-execution model: the inner plan runs EXACTLY ONCE over the union of every parent's
// children (keyed by child key), then a key→row scatter partitions the inner rows back to
// their parents. This is O(1) inner executions, not O(parents). The inner element type is an
// unconstrained JoinResult (only IJoinResult<TDesk>), which the right-list resolver forbids
// (its TRightValue : ICacheClonable) — hence a distinct resolver.

/// <summary>
/// Resolver for a nested JoinMany: the Many-side cache is itself queried via a captured inner
/// plan, producing <c>QueryResults&lt;TInnerResult&gt;</c> per parent. Identity selector, no outer
/// filter (v1). The inner plan is a full <see cref="CacheQueryBuilderCombined{T1,T2,T3,T4,T5,T6}"/>
/// built once at query-build time from the continuation lambda.
/// </summary>
/// <typeparam name="TLeftKey">Parent (Room) key type.</typeparam>
/// <typeparam name="TLeftValue">Parent (Room) value type.</typeparam>
/// <typeparam name="TRightCache">Child (Desk) cache wrapper.</typeparam>
/// <typeparam name="TRightKey">Child (Desk) key type.</typeparam>
/// <typeparam name="TRightValue">Child (Desk) value type — the inner join's left.</typeparam>
/// <typeparam name="TInnerDisc">Inner builder discriminator.</typeparam>
/// <typeparam name="TInnerExec">Inner builder executor (over the child cache).</typeparam>
/// <typeparam name="TInnerChain">Inner builder resolver chain.</typeparam>
/// <typeparam name="TInnerResult">Inner result row, e.g. <c>JoinResult&lt;Desk, Occupier?&gt;</c>.</typeparam>
public struct JoinManyNestedResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>
	: IJoinManyResolver<TLeftKey, TLeftValue, TInnerResult>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
	where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
	where TInnerDisc : struct
	where TInnerExec : struct, ICandidatesExecutor<TRightKey, TRightValue>
	where TInnerChain : struct, IResolvers
	where TInnerResult : struct, IJoinResult<TRightValue> {

	// ── Fields ───────────────────────────────────────────────────────────────

	private readonly TRightCache _rightCache;
	private readonly CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> _rightIndex;
	private CacheQueryBuilderCombined<TInnerDisc, TInnerExec, TRightKey, TRightValue, TInnerChain, TInnerResult> _innerBuilder;
	private readonly bool _isInner;

	static JoinManyNestedResolver() {
		// Deep-clone each inner row (Child + its own join slots) via the inner plan's resolver
		// chain. Drives the OUTER ExecuteCloned `_clone` (slice) path; the no-slice clone path
		// clones on scatter (see Execute).
		SlotCloner<QueryResults<TInnerResult>>.Register(
			static (ref QueryResults<TInnerResult> v) =>
				v.CloneElements(new ResolveChainCloner<TInnerChain, TRightValue, TInnerResult>()));
	}

	internal JoinManyNestedResolver(
		TRightCache rightCache,
		CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
		CacheQueryBuilderCombined<TInnerDisc, TInnerExec, TRightKey, TRightValue, TInnerChain, TInnerResult> innerBuilder,
		bool isInner = false) {
		_rightCache = rightCache;
		_rightIndex = rightIndex;
		_innerBuilder = innerBuilder;
		_isInner = isInner;
	}

	// ── Static / property values ─────────────────────────────────────────────

	public static bool IsSorter { get; } = false;
	public bool Inner => _isInner;

	// ── Clone (slot cascade registered in Phase 3 — no-op until then) ─────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult {
		ref var item = ref value.TUnsafeGetValAt<QueryResults<TInnerResult>>(index);
		SlotCloner<QueryResults<TInnerResult>>.Clone(ref item);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneValue(ref QueryResults<TInnerResult> value) => SlotCloner<QueryResults<TInnerResult>>.Clone(ref value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref QueryResults<TInnerResult> value) => CloneValue(ref value);

	// ── Per-parent scatter container ───────────────────────────────────────────
	//
	// Mirrors UnsafeResolverContainer but over the (unconstrained) inner result type and
	// without per-element clone-on-add (clone is handled at the OUTER BuildResults level via
	// the SlotCloner cascade). Init/PrepareSharedBuffer/Add follow the standard keyed-init
	// protocol against the parent's QueryResults<TInnerResult> slot.

	private ref struct NestedScatterContainer<TAccessor>
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {
		private TAccessor _accessor;
		private readonly bool _shouldPool;
		private int _totalCount;
		private TInnerResult[]? _sharedBuffer;

		public NestedScatterContainer(TAccessor accessor, bool shouldPool) {
			_accessor = accessor;
			_shouldPool = shouldPool;
			_totalCount = 0;
			_sharedBuffer = null;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Init(TLeftKey parentKey, int count) {
			ref var slot = ref _accessor.GetValueRef<TLeftKey, QueryResults<TInnerResult>>(parentKey);
			if (!Unsafe.IsNullRef(in slot)) {
				slot.SetPendingCapacity(count);
				_totalCount += count;
			}
		}

		public void PrepareSharedBuffer() {
			_sharedBuffer = _shouldPool ? PragueArrayPool<TInnerResult>.Pool.Rent(_totalCount) : new TInnerResult[_totalCount];
			var offset = 0;
			var keys = _accessor.GetKeys<TLeftKey>();
			for (var i = 0; i < keys.Length; i++) {
				ref var slot = ref _accessor.GetValueRef<TLeftKey, QueryResults<TInnerResult>>(keys[i]);
				if (!Unsafe.IsNullRef(in slot))
					offset = slot.AssignSharedBuffer(_sharedBuffer, offset);
			}
		}

		public TInnerResult[]? GetSharedBuffer() => _shouldPool ? _sharedBuffer : null;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Add(TLeftKey parentKey, TInnerResult row) {
			ref var slot = ref _accessor.GetValueRef<TLeftKey, QueryResults<TInnerResult>>(parentKey);
			if (!Unsafe.IsNullRef(in slot))
				slot.UnsafeAdd(row);
		}
	}

	// ── Core execution ─────────────────────────────────────────────────────────

	private void Execute<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, ref QueryResultsDisposer disposer)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {

		var parentKeys = accessor.GetKeys<TLeftKey>();
		if (parentKeys.IsEmpty)
			return;

		// 1. Union of every parent's child keys → the inner candidate set (identity selector).
		// Until the inner plan takes ownership below, a throw out of UnionWith (user
		// GetHashCode/Equals) leaves us the only owner of the rented set.
		var union = new ValueSet<TRightKey, DefaultKeyComparer<TRightKey>>(parentKeys.Length);
		try {
			foreach (var parentKey in parentKeys) {
				var bucket = _rightIndex.GetValuesUnsafe(Unsafe.As<TLeftKey, TLeftKey>(ref Unsafe.AsRef(in parentKey)));
				if (bucket is null || bucket.Count == 0)
					continue;
				union.UnionWith(bucket);
			}
		} catch {
			if (union.IsInitlized)
				union.Dispose();
			throw;
		}

		// 2. Run the inner plan ONCE over the union → keyed by child key. The union is handed to
		// the inner plan as its candidate set and the inner execution OWNS it from here (it disposes
		// Candidates when it consumes them) — we must NOT dispose `union` ourselves (double-return).
		// On the no-slice ExecuteCloned path (cloneOnAdd), the inner plan clones at add time
		// (null-safe), so the extracted rows are already independent of the source caches.
		var inner = _innerBuilder;
		inner.UnsafeSeedCandidates(union);
		inner.ExecuteCoreJoinedKeyed<TInnerResult>(disposer.IsActive, cloneOnAdd, out var innerByKey, out var innerDisposer);

		try {
			// 3a. Count present inner rows per parent → keyed-init the parent slots.
			var container = new NestedScatterContainer<TAccessor>(accessor, disposer.IsActive);
			foreach (var parentKey in parentKeys) {
				var bucket = _rightIndex.GetValuesUnsafe(parentKey);
				if (bucket is null || bucket.Count == 0)
					continue;
				var count = 0;
				foreach (var childKey in bucket)
					if (innerByKey.TryGetValue(childKey, out _))
						count++;
				if (count > 0)
					container.Init(parentKey, count);
			}

			container.PrepareSharedBuffer();

			// 3b. Scatter inner rows into their parent's contiguous slice. Rows were already
			// cloned (if requested) by the inner plan at add time, so this is a plain copy.
			foreach (var parentKey in parentKeys) {
				var bucket = _rightIndex.GetValuesUnsafe(parentKey);
				if (bucket is null || bucket.Count == 0)
					continue;
				foreach (var childKey in bucket)
					if (innerByKey.TryGetValue(childKey, out var row))
						container.Add(parentKey, row);
			}

			// 4. Pooling/disposal cascade: register the parent partition buffer and absorb the
			// inner plan's pooled buffers (depth-3 inner-Many slices) into the outer disposer, so
			// they are returned exactly once when the outer result is disposed. (No-op while
			// non-pooled — disposer.IsActive is false for Execute().)
			RegisterPooledBuffer(ref disposer, container.GetSharedBuffer());
			if (disposer.IsActive)
				disposer.Absorb(in innerDisposer);
		}
		finally {
			// The inner dict's value array is no longer needed once rows are copied into the parent
			// partitions; return it to the pool now (when pooled — withValues:true). Inner-Many
			// slices (depth-3), referenced by the copied rows, live in innerDisposer's buffers which
			// were absorbed above, so clearing the dict's value slots here does not touch them.
			// (`union` is deliberately NOT disposed here — the inner plan owns it; see above.)
			innerByKey.Dispose(disposer.IsActive);
		}
	}

	void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(
		ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer)
		=> Execute(ref accessor, cloneOnAdd, ref disposer);

	void IJoinManyResolver<TLeftKey, TLeftValue, TInnerResult>.ExecuteReverseMany<TContainer>(
		ref TContainer container, ReadOnlySpan<TLeftKey> keys)
		=> throw new NotSupportedException("Nested JoinMany behind a chained Many is not supported in v1.");

	private static void RegisterPooledBuffer(ref QueryResultsDisposer disposer, TInnerResult[]? buffer) {
		if (disposer.IsActive && buffer is { Length: > 0 })
			disposer.AddPooledBuffer(buffer);
	}

	// ── IndexedInner (InnerJoinMany-nested: drop parents with no inner rows) ─────

	/// <summary>
	/// Triggers the outer executor's candidate population (the parent key set) before the inner
	/// attach runs — mirrors the JoinMany right-list resolver's inner-prepare step.
	/// </summary>
	void IJoinResolver.PrepareIndexedInner<TExecutor>(
		ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		_ = leftQuery.GetCandidates<TLeftKey>();
	}

	/// <summary>
	/// Inner-join attach: materialize a slot for every candidate parent so the shared <see cref="Execute"/>
	/// fill path (which writes through existing slots) populates them, then <c>RetainNonEmptyManySlots</c>
	/// drops parents whose inner collection stayed empty (no children, or all inner-join-dropped) and
	/// narrows <paramref name="leftQuery"/>'s candidates to the survivors — the subsequent base execute
	/// fills <c>Left</c> for those only.
	/// </summary>
	void IJoinResolver.UnsafeExecuteIndexedInner<TAccessor, TExecutor>(
		ref TAccessor accessor,
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool isFirst,
		ref QueryResultsDisposer disposer) {
		ref var candidates = ref leftQuery.GetCandidates<TLeftKey>();
		if (!candidates.IsInitlized || candidates.Count == 0)
			return;

		// Materialize each candidate parent's Many slot (Left stays default until base execute).
		foreach (var parentKey in candidates)
			_ = accessor.GetValueRefOrAddDefault<TLeftKey, QueryResults<TInnerResult>>(parentKey, out _);

		// Same union → inner-execute → scatter as the outer path, now over the materialized slots.
		Execute(ref accessor, cloneOnAdd, ref disposer);

		// Drop parents whose inner QueryResults is empty and narrow candidates to the survivors.
		accessor.RetainNonEmptyManySlots<TLeftKey, TInnerResult>(ref candidates);
	}
}
