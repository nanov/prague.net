namespace Prague.Core;

using System.Buffers;
using System.Runtime.CompilerServices;
using Prague.Core.Collections;
using Prague.Core.TypeSystem;
using Prague.Core.Utils;

// ── JoinMany M:N reverse-collection resolver ──────────────────────────────
//
// Reverse-collection join (driving cache → owners-of-an-element): given a Tag
// cache (left) and a Book cache (right) where Book carries List<int> TagIds,
// each tag fans out to the SET of books whose TagIds contain it. This is M:N —
// a book has many tags, a tag has many books — so the JoinManyRightListIndex
// resolver's right-key pair dedup is unusable (it would collapse a book that
// belongs to multiple tags onto one). Instead we reuse the LeftSym fan-out
// trick: each pair carries a LeftKeySetView over the book's full tag-set
// (Reverse half), deduped on the bookKey, and the fan-out container iterates
// that set, writing the book into each of its tags' slots (non-candidate /
// non-Init'd tags are silently skipped via the inner container's NullRef check).
//
// The index is the symmetric collection index over the RIGHT cache:
//   Forward : tagId    → {bookKeys}  (CacheKeyValueListIndex<TRightKey, _, TLeftKey>)
//   Reverse : bookKey  → {tagIds}    (CacheKeyValueListIndex<TLeftKey,  _, TRightKey>)

/// <summary>
/// Resolver for an M:N reverse-collection JoinMany over a
/// <see cref="CacheCollectionSymmetricKeyValueListIndex{TKey,TValue,TIndexKey}"/> built on the
/// right cache. For each left (element) key, <c>Forward</c> maps it to the set of right (owner)
/// keys; each owner's <c>Reverse</c> set of element keys is wrapped in a
/// <see cref="LeftKeySetView{T}"/> and fanned out so one owner is written into every element's slot.
/// </summary>
/// <typeparam name="TLeftKey">Left (element) cache's key type — also the index's element key.</typeparam>
/// <typeparam name="TLeftValue">Left cache's value type.</typeparam>
/// <typeparam name="TRightCache">Right (owner) cache wrapper.</typeparam>
/// <typeparam name="TRightKey">Right cache's key type — the index's owner key.</typeparam>
/// <typeparam name="TRightValue">Right cache's value type (also the inner type of QueryResults).</typeparam>
/// <typeparam name="TFilter">Filter strategy struct over the paired non-executable builder.</typeparam>
public struct JoinManyCollectionResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TFilter>
	: IJoinManyResolver<TLeftKey, TLeftValue, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
	where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
	where TFilter : struct, IJoinFilter<
		CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> {

	// ── Fields ───────────────────────────────────────────────────────────────

	private readonly CacheCollectionSymmetricKeyValueListIndex<TRightKey, TRightValue, TLeftKey> _index;
	private readonly TRightCache _rightCache;
	private TFilter _filter;
	private readonly bool _isInner;

	static JoinManyCollectionResolver() {
		SlotCloner<QueryResults<TRightValue>>.Register(
			static (ref QueryResults<TRightValue> v) => v.CloneElements());
	}

	internal JoinManyCollectionResolver(
		CacheCollectionSymmetricKeyValueListIndex<TRightKey, TRightValue, TLeftKey> index,
		TRightCache rightCache,
		TFilter filter,
		bool isInner = false) {
		_index = index;
		_rightCache = rightCache;
		_filter = filter;
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

	// ── Fan-out containers ────────────────────────────────────────────────────

	/// <summary>
	/// Outer fan-out wrapper around a keyed inner container. ExecutePaired
	/// calls <c>Add(LeftKeySetView, rightValue)</c>; we reinterpret the view
	/// back to <see cref="PooledSet{T}"/>, iterate, and forward per-leftKey
	/// to the inner's <c>Add(leftKey, rightValue)</c>. Lefts whose slot was
	/// not pre-initialized (no outer base execute created them) silently skip
	/// (inner.Add finds NullRef and returns 0).
	/// </summary>
	private ref struct OuterFanOutContainer<TInner>
		: IJoinedResultContainer<LeftKeySetView<TLeftKey>, TRightValue>
		where TInner : struct, IJoinedKeyedResultContainer<TLeftKey, TRightValue>, allows ref struct {
		// Stored by value, not by ref — ref-to-ref-struct fields are illegal.
		// The inner container's mutating state (e.g. _totalCount) is already finalized
		// by the time we wrap (PrepareSharedBuffer was called); Add doesn't mutate it.
		// Slot writes go through the shared accessor ref which the copy still owns.
		private TInner _inner;
		public int TotalCount => 1;

		public OuterFanOutContainer(TInner inner) {
			_inner = inner;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(LeftKeySetView<TLeftKey> lefts, TRightValue result) {
			var pooled = Unsafe.As<LeftKeySetView<TLeftKey>, PooledSet<TLeftKey, DefaultKeyComparer<TLeftKey>>>(ref lefts);
			foreach (var lk in pooled)
				_inner.Add(lk, result);
			return 0;
		}
	}

	/// <summary>
	/// Inner fan-out wrapper — additionally filters by the outer query's
	/// candidate set. Lefts outside <c>candidates</c> appear in the borrowed
	/// PooledSet but must be skipped, since the candidate-narrow walk above
	/// only Init'd slots for lefts in candidates.
	/// </summary>
	private ref struct InnerFanOutContainer<TInner>
		: IJoinedResultContainer<LeftKeySetView<TLeftKey>, TRightValue>
		where TInner : struct, IJoinedKeyedResultContainer<TLeftKey, TRightValue>, allows ref struct {
		private TInner _inner;
		private ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> _candidates;
		public int TotalCount => 1;

		public InnerFanOutContainer(TInner inner, ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> candidates) {
			_inner = inner;
			_candidates = ref candidates;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(LeftKeySetView<TLeftKey> lefts, TRightValue result) {
			var pooled = Unsafe.As<LeftKeySetView<TLeftKey>, PooledSet<TLeftKey, DefaultKeyComparer<TLeftKey>>>(ref lefts);
			foreach (var lk in pooled) {
				if (!_candidates.Contains(lk))
					continue;
				_inner.Add(lk, result);
			}
			return 0;
		}
	}

	// ── Core outer execution ─────────────────────────────────────────────────

	/// <summary>
	/// Walks input lefts (elements): per-leftKey, <c>Forward</c> → owner-key set;
	/// Init the keyed buffer for THIS left; per owner, <c>Reverse</c> → that owner's
	/// element-key set, wrapped in a borrowed LeftKeySetView and emitted as one
	/// pair keyed on the owner (rightKey). The pair-set dedups owners, so a book
	/// shared by two tags yields a single pair carrying its full tag-set view;
	/// ExecutePaired walks pairs and OuterFanOutContainer iterates the set to write
	/// the owner into each of its elements' slots.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOuter<TContainer>(ref TContainer container, ReadOnlySpan<TLeftKey> leftKeys)
		where TContainer : struct, IJoinedKeyedResultContainer<TLeftKey, TRightValue>, allows ref struct {

		if (leftKeys.IsEmpty)
			return;

		var pairs = new ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>>>(leftKeys.Length);
		var handedOff = false;
		try {
			foreach (var leftKey in leftKeys) {
				var rights = _index.Forward.GetValuesUnsafe(leftKey);
				if (rights is null || rights.Count == 0)
					continue;

				container.Init(leftKey, rights.Count);

				foreach (var rightKey in rights) {
					var lefts = _index.Reverse.GetValuesUnsafe(rightKey);
					if (lefts is null)
						continue;
					var view = new LeftKeySetView<TLeftKey>(lefts);
					pairs.Add(new JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>(view, rightKey));
				}
			}

			container.PrepareSharedBuffer();

			if (!pairs.IsInitlized || pairs.Count == 0)
				return;

			var dataCache = _rightCache.Cache;
			var pairedCore = new PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>(dataCache, pairs);
			var builder = new CacheQueryBuilderCombined<
				NonExecutableQuery<TRightCache>,
				PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>,
				TRightKey, TRightValue,
				Resolvers<BaseResolver<TRightKey, TRightValue>>,
				TRightValue>(
				new NonExecutableQuery<TRightCache>(_rightCache),
				pairedCore,
				new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
				0);
			builder = _filter.Apply(builder);

			var wrapper = new OuterFanOutContainer<TContainer>(container);
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref wrapper);
		}
		finally {
			if (!handedOff && pairs.IsInitlized) pairs.Dispose();
		}
	}

	void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(
		ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		// Pool the per-left child buffer only when a disposer exists to return it (pooled execution).
		var inner = new InnerKeyedContainer<TAccessor>(accessor, cloneOnAdd, disposer.IsActive);
		ExecuteOuter(ref inner, accessor.GetKeys<TLeftKey>());
		RegisterPooledBuffer(ref disposer, inner.GetSharedBuffer());
	}

	void IJoinManyResolver<TLeftKey, TLeftValue, TRightValue>.ExecuteReverseMany<TContainer>(
		ref TContainer container, ReadOnlySpan<TLeftKey> keys) {
		ExecuteOuter(ref container, keys);
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
	/// Inner-join attach path: same Forward → owner-set / Reverse → element-set-view
	/// fan-out as outer, but operating on the candidate set. The slot is materialised
	/// via GetValueRefOrAddDefault BEFORE Init for each candidate left, the fan-out is
	/// candidate-filtered, and a post-walk <c>RetainNonEmptyManySlots</c> drops lefts
	/// whose per-left <see cref="QueryResults{TRightValue}"/> stayed empty (no owners, or
	/// the filter rejected all of them) and narrows candidates to the survivors.
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

		var pairs = new ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>>>(candidates.Count);
		var handedOff = false;
		var inner = new InnerKeyedContainer<TAccessor>(accessor, cloneOnAdd, disposer.IsActive);
		try {
			foreach (var leftKey in candidates) {
				var rights = _index.Forward.GetValuesUnsafe(leftKey);
				if (rights is null || rights.Count == 0)
					continue;

				// For inner mode, the slot doesn't pre-exist (no outer base execute ran yet).
				// Materialise it via GetValueRefOrAddDefault BEFORE Init — otherwise Init's
				// GetValueRef call returns NullRef and silently skips _totalCount tracking,
				// leaving PrepareSharedBuffer with a zero-length buffer.
				_ = accessor.GetValueRefOrAddDefault<TLeftKey, QueryResults<TRightValue>>(leftKey, out _);
				inner.Init(leftKey, rights.Count);

				foreach (var rightKey in rights) {
					var lefts = _index.Reverse.GetValuesUnsafe(rightKey);
					if (lefts is null)
						continue;
					var view = new LeftKeySetView<TLeftKey>(lefts);
					pairs.Add(new JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>(view, rightKey));
				}
			}

			if (!pairs.IsInitlized || pairs.Count == 0) {
				// No left has any right — Inner semantic narrows to empty.
				candidates.IntersectWith(ReadOnlySpan<TLeftKey>.Empty);
				return;
			}

			inner.PrepareSharedBuffer();
			RegisterPooledBuffer(ref disposer, inner.GetSharedBuffer());

			var dataCache = _rightCache.Cache;
			var pairedCore = new PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>(dataCache, pairs);
			var builder = new CacheQueryBuilderCombined<
				NonExecutableQuery<TRightCache>,
				PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>,
				TRightKey, TRightValue,
				Resolvers<BaseResolver<TRightKey, TRightValue>>,
				TRightValue>(
				new NonExecutableQuery<TRightCache>(_rightCache),
				pairedCore,
				new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
				0);
			builder = _filter.Apply(builder);

			var wrapper = new InnerFanOutContainer<InnerKeyedContainer<TAccessor>>(inner, ref candidates);
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref wrapper);

			accessor.RetainNonEmptyManySlots<TLeftKey, TRightValue>(ref candidates);
		}
		finally {
			if (!handedOff && pairs.IsInitlized) pairs.Dispose();
		}
	}

	/// <summary>
	/// Keyed inner container used during execution. Same shape as the legacy
	/// ManyResolver's container — Init tracks per-left capacity, PrepareSharedBuffer
	/// allocates the contiguous TRightValue[] partition.
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
			_sharedBuffer = _shouldPool ? ArrayPool<TRightValue>.Shared.Rent(_totalCount) : new TRightValue[_totalCount];
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
