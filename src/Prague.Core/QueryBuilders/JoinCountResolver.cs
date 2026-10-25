namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;
using TypeSystem;

// ── Count join: a JoinMany whose slot is the number of rights ─────────────
//
// CountJoin / InnerCountJoin answer "how many rights would JoinMany have delivered for this left" with an
// int slot instead of a QueryResults<TRightValue>. The resolver is generic over the family's bucket read
// (IManyBucketSource — right-list, left-symmetric, collection: the same structs the JoinMany resolvers and
// the frozen fill use), so one resolver covers every JoinMany shape.
//
// Unfiltered (TFilter.IsNoOp, JIT-folded): one bucket read per left and its Count written into the slot —
// no pair set, no fan-out, no store lookup, no child buffer, nothing to clone or to register with the
// disposer. The forward collection read is the exception (TBuckets.KeysAreReferences, folded): its bucket
// holds the keys a left references, which need not be resident, so it counts the resident ones. The count is the bucket's snapshot, the same staleness window the fan-out's recording has (the
// concurrency contract promises no consistency, only that a query never fails). Filtered: the JoinMany
// fan-out and its one paired execute (the filter runs once per query, its predicate once per distinct
// right), delivering into a container that bumps the left's int per surviving right — still no buffer.

/// <summary>
///   Resolver for <c>CountJoin</c> / <c>InnerCountJoin</c>: per left, the number of rights the matching
///   <c>JoinMany</c> would deliver, as an <see cref="int" /> slot. <typeparamref name="TBuckets" /> is the
///   family's bucket read (<see cref="JoinManyRightListBuckets{TLeftKey,TRightKey,TRightValue,TIndexKey,TSelector}" />,
///   <see cref="JoinManyLeftSymBuckets{TLeftKey,TLeftValue,TLookupKey,TRightIndexKey,TRightKey,TRightValue,TSelector}" />,
///   <see cref="JoinManyCollectionBuckets{TLeftKey,TRightKey,TOwnerValue}" />). An inner count join drops the
///   lefts whose count is <c>0</c>.
/// </summary>
/// <typeparam name="TLeftKey">Left cache's key type.</typeparam>
/// <typeparam name="TLeftValue">Left cache's value type.</typeparam>
/// <typeparam name="TRightCache">Right cache wrapper (the filter callback's builder is over it).</typeparam>
/// <typeparam name="TRightKey">Right cache's key type.</typeparam>
/// <typeparam name="TRightValue">Right cache's value type.</typeparam>
/// <typeparam name="TBuckets">The family's bucket read.</typeparam>
/// <typeparam name="TFilter">Filter strategy struct over the paired non-executable builder.</typeparam>
public struct JoinCountResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TBuckets, TFilter>
	: IJoinResolver<TLeftKey, TLeftValue, int>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
	where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
	where TBuckets : struct, IManyBucketSource<TLeftKey, TRightKey>
	where TFilter : struct, IJoinFilter<
		CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> {

	private readonly TRightCache _rightCache;
	private TBuckets _buckets;
	private TFilter _filter;
	private readonly bool _isInner;

	internal JoinCountResolver(TRightCache rightCache, TBuckets buckets, TFilter filter, bool isInner = false) {
		_rightCache = rightCache;
		_buckets = buckets;
		_filter = filter;
		_isInner = isInner;
	}

	public static bool IsSorter => false;

	public bool Inner => _isInner;

	// The frozen pipeline admits it as a JoinMany (design §7.2 as implemented): fused in the fill walk without
	// a filter callback, otherwise its own execute after the pass; an inner one is pruned by
	// UnsafePruneEmptyManySlots. Decided at build, folded per instantiation.
	static bool IJoinResolver.IsMany => true;

	bool IJoinResolver.CanFuse => TFilter.IsNoOp;

	// ── Clone / CloneValue: an int has nothing to clone ─────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult { }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneValue(ref int value) { }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref int value) { }

	// ── Frozen pipeline ──────────────────────────────────────────────────────

	// One bucket read per row, the count straight into the slot. The size hint, the disposer and the store are
	// never touched: there is no buffer.
	bool IJoinResolver.UnsafeFillFusedRows<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer, ref int sizeHint) {
		var keys = accessor.GetKeys<TLeftKey>();
		var empties = 0;
		for (var i = 0; i < keys.Length; i++) {
			var count = _buckets.TryGetBucket(keys[i], out var bucket) ? Count(bucket) : 0;
			accessor.GetSlotAt<int>(i) = count;
			if (count == 0)
				empties++;
		}

		if (!_isInner || empties == 0)
			return false;
		accessor.PruneZeroCountSlots();
		return true;
	}

	// The inner narrowing without a fill (a count, the bounded flow's pre-heap pass): keeps, in order, the
	// keys whose bucket is non-empty.
	int IJoinResolver.UnsafeNarrowFused<TKey, TValue>(Span<TKey> keys, Span<TValue> values) {
		var n = 0;
		for (var i = 0; i < keys.Length; i++) {
			if (!_buckets.TryGetBucket(Unsafe.As<TKey, TLeftKey>(ref keys[i]), out var bucket) || (TBuckets.KeysAreReferences && CountResident(bucket) == 0))
				continue;
			keys[n] = keys[i];
			if (values.Length != 0)
				values[n] = values[i];
			n++;
		}

		return n;
	}

	void IJoinResolver.UnsafePruneEmptyManySlots<TAccessor>(ref TAccessor accessor) => accessor.PruneZeroCountSlots();

	// ── Outer execution ──────────────────────────────────────────────────────

	/// <summary>
	///   Writes every row's count. Unfiltered: one bucket read per row. Filtered: the <c>JoinMany</c> fan-out
	///   over the rows' buckets and one paired execute, each surviving right adding one to every left that
	///   recorded it. The rows were formed by this execution, so their slots start at <c>0</c>.
	/// </summary>
	void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		var keys = accessor.GetKeys<TLeftKey>();
		if (keys.IsEmpty)
			return;

		if (TFilter.IsNoOp) {
			for (var i = 0; i < keys.Length; i++)
				accessor.GetSlotAt<int>(i) = _buckets.TryGetBucket(keys[i], out var bucket) ? Count(bucket) : 0;
			return;
		}

		var fanOut = new JoinManyFanOut<TLeftKey, TRightKey>(keys.Length);
		try {
			for (var i = 0; i < keys.Length; i++)
				if (_buckets.TryGetBucket(keys[i], out var bucket))
					fanOut.RecordBucket(keys[i], bucket);

			var container = new CountContainer<TAccessor>(accessor);
			ExecutePairs(ref fanOut, ref container);
		} finally {
			fanOut.Dispose();
		}
	}

	// ── Inner execution ──────────────────────────────────────────────────────

	void IJoinResolver.PrepareIndexedInner<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer)
		=> _ = leftQuery.GetCandidates<TLeftKey>();

	/// <summary>
	///   Inner-join attach path over the candidate set: materialises a slot per candidate that has a bucket and
	///   counts into it — the bucket's size when unfiltered (no fan-out at all), the filter's survivors through
	///   the fan-out otherwise — then <c>RetainNonZeroCountSlots</c> drops every row whose count is <c>0</c>
	///   (including rows an earlier chained inner join created for a left this one has no right for) and
	///   narrows the candidates to the survivors.
	/// </summary>
	void IJoinResolver.UnsafeExecuteIndexedInner<TAccessor, TExecutor>(ref TAccessor accessor, ref TExecutor leftQuery, bool cloneOnAdd, bool isFirst, ref QueryResultsDisposer disposer) {
		ref var candidates = ref leftQuery.GetCandidates<TLeftKey>();
		if (!candidates.IsInitlized || candidates.Count == 0)
			return;

		if (TFilter.IsNoOp) {
			foreach (var leftKey in candidates)
				if (_buckets.TryGetBucket(leftKey, out var bucket))
					accessor.GetValueRefOrAddDefault<TLeftKey, int>(leftKey, out _) = Count(bucket);

			accessor.RetainNonZeroCountSlots(ref candidates);
			return;
		}

		var fanOut = new JoinManyFanOut<TLeftKey, TRightKey>(candidates.Count);
		try {
			foreach (var leftKey in candidates) {
				if (!_buckets.TryGetBucket(leftKey, out var bucket))
					continue;
				// The slot does not exist yet (no base execute ran): materialise it at 0 so the delivery can count into it.
				_ = accessor.GetValueRefOrAddDefault<TLeftKey, int>(leftKey, out _);
				fanOut.RecordBucket(leftKey, bucket);
			}

			if (fanOut.DistinctRights != 0) {
				var container = new CountContainer<TAccessor>(accessor);
				ExecutePairs(ref fanOut, ref container);
			}

			accessor.RetainNonZeroCountSlots(ref candidates);
		} finally {
			fanOut.Dispose();
		}
	}

	/// <summary>The rights a non-empty bucket delivers unfiltered: its size, or its resident keys for a reference bucket.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private readonly int Count(PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket)
		=> TBuckets.KeysAreReferences ? CountResident(bucket) : bucket.Count;

	private readonly int CountResident(PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket) {
		var cache = _rightCache.Cache;
		var n = 0;
		foreach (var rightKey in bucket)
			if (cache.TryGet(rightKey, out _))
				n++;
		return n;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecutePairs<TContainer>(ref JoinManyFanOut<TLeftKey, TRightKey> fanOut, ref TContainer container)
		where TContainer : struct, IJoinedResultContainer<TLeftKey, TRightValue>, allows ref struct
		=> JoinManyPairedExecute.Run<TLeftKey, TRightCache, TRightKey, TRightValue, TFilter, TContainer>(_rightCache, ref _filter, ref fanOut, ref container);

	/// <summary>The filtered delivery's target: one per surviving right per left that recorded it, into the left's slot.</summary>
	private ref struct CountContainer<TAccessor> : IJoinedResultContainer<TLeftKey, TRightValue>
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {
		private TAccessor _accessor;
		private int _totalCount;

		public CountContainer(TAccessor accessor) {
			_accessor = accessor;
			_totalCount = 0;
		}

		public int TotalCount => _totalCount;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(TLeftKey foreignKey, TRightValue result) {
			ref var slot = ref _accessor.GetValueRef<TLeftKey, int>(foreignKey);
			if (Unsafe.IsNullRef(ref slot))
				return 0;
			_totalCount++;
			return ++slot;
		}
	}
}
