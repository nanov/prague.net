namespace Prague.Core;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Prague.Core.Collections;
using TypeSystem;
using Utils;

// ── Filter strategy interface and structs ────────────────────────────────────

/// <summary>
/// Zero-virtual-dispatch filter strategy for <see cref="JoinOneResolver{TLeftKey,TLeftValue,TRightCache,TRightKey,TRightValue,TFilter}"/>.
/// The JIT devirtualizes <see cref="Apply"/> per closed generic — no virtual dispatch overhead.
/// </summary>
public interface IJoinFilter<TBuilder> {
	TBuilder Apply(TBuilder q);
}

/// <summary>
/// Identity filter — no narrowing applied. <see cref="Apply"/> returns <paramref name="q"/> unchanged;
/// the JIT elides the call entirely in release builds.
/// </summary>
public readonly struct NoFilter<TBuilder> : IJoinFilter<TBuilder> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TBuilder Apply(TBuilder q) => q;
}

/// <summary>
/// Filter strategy that wraps a <see cref="Func{TBuilder,TBuilder}"/> and applies it at execute time.
/// </summary>
public readonly struct JoinFilter<TBuilder> : IJoinFilter<TBuilder> {
	private readonly Func<TBuilder, TBuilder> _filter;

	public JoinFilter(Func<TBuilder, TBuilder> filter) => _filter = filter;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TBuilder Apply(TBuilder q) => _filter(q);
}

/// <summary>
/// Filter strategy that holds a <typeparamref name="TArg"/> user-state value alongside the filter
/// delegate. Pass a <c>static</c> lambda to guarantee zero closure allocation:
/// <code>.JoinOne(_bCache, arg, static (q, a) => q.WithStatus(a))</code>
/// </summary>
public readonly struct JoinFilterWithArg<TBuilder, TArg> : IJoinFilter<TBuilder> {
	private readonly Func<TBuilder, TArg, TBuilder> _filter;
	private readonly TArg _arg;

	public JoinFilterWithArg(Func<TBuilder, TArg, TBuilder> filter, TArg arg) {
		_filter = filter;
		_arg = arg;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TBuilder Apply(TBuilder q) => _filter(q, _arg);
}

// ── Key-selector strategy interface and structs ──────────────────────────────

/// <summary>
/// Zero-virtual-dispatch key-selector strategy. Each <c>JoinOne</c> resolver carries a
/// <c>TSelector : struct, IKeySelector&lt;TIn, TOut&gt;</c> generic parameter that determines
/// how the "left side" key is transformed into the "right side" key (or intermediate index
/// key) for the join. The static-abstract <see cref="IsIdentity"/> flag lets the JIT fold
/// away the selector call in the identity case — old non-selector overloads pay zero cost.
/// </summary>
public interface IKeySelector<TIn, TOut> {
	/// <summary>
	/// <c>true</c> if this selector is the identity function (input == output, no transformation).
	/// JIT-specialized per closed generic — the resolver branches on this and the dead path is folded.
	/// </summary>
	static abstract bool IsIdentity { get; }

	/// <summary>Apply the selector to an input key.</summary>
	TOut Select(TIn input);
}

/// <summary>
/// Identity selector — <see cref="Select"/> returns its input unchanged.
/// Only legal when <typeparamref name="T"/> is both input AND output; the extension call site
/// enforces the type-equality. Used as the default selector for all <c>JoinOne</c> overloads
/// that don't take an explicit selector. The JIT elides <see cref="Select"/> entirely.
/// </summary>
public readonly struct IdentitySelector<T> : IKeySelector<T, T> {
	public static bool IsIdentity => true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Select(T input) => input;
}

/// <summary>
/// Selector strategy that wraps a <see cref="Func{TIn, TOut}"/> and applies it per key.
/// </summary>
public readonly struct KeySelector<TIn, TOut> : IKeySelector<TIn, TOut> {
	private readonly Func<TIn, TOut> _selector;

	public KeySelector(Func<TIn, TOut> selector) => _selector = selector;

	public static bool IsIdentity => false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TOut Select(TIn input) => _selector(input);
}

/// <summary>
/// Selector strategy that holds a <typeparamref name="TArg"/> user-state value alongside the
/// selector delegate. Pass a <c>static</c> lambda to guarantee zero closure allocation:
/// <code>.JoinOne(static (lk, a) => lk + a, offset, rightCache)</code>
/// </summary>
public readonly struct KeySelectorWithArg<TIn, TArg, TOut> : IKeySelector<TIn, TOut> {
	private readonly Func<TIn, TArg, TOut> _selector;
	private readonly TArg _arg;

	public KeySelectorWithArg(Func<TIn, TArg, TOut> selector, TArg arg) {
		_selector = selector;
		_arg = arg;
	}

	public static bool IsIdentity => false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TOut Select(TIn input) => _selector(input, _arg);
}

// ── Unified resolver ─────────────────────────────────────────────────────────

/// <summary>
/// Resolver for PK-to-PK outer-joins: left.PK == right.PK, with an optional lazily-applied
/// filter strategy struct on the right cache. The cache wrapper is stored directly; the filter
/// is called at execute time, after the left-keys restriction has been applied.
/// <para>
/// Three strategy structs are available for <typeparamref name="TFilter"/>:
/// <list type="bullet">
///   <item><see cref="NoFilter{TBuilder}"/> — identity, no narrowing.</item>
///   <item><see cref="JoinFilter{TBuilder}"/> — wraps <c>Func&lt;TBuilder, TBuilder&gt;</c>.</item>
///   <item><see cref="JoinFilterWithArg{TBuilder, TArg}"/> — wraps func + arg (zero-alloc with static lambdas).</item>
/// </list>
/// Each strategy is a <c>struct</c> constrained to <see cref="IJoinFilter{TBuilder}"/>;
/// the JIT devirtualizes <see cref="IJoinFilter{TBuilder}.Apply"/> per closed generic.
/// </para>
/// Indexed-inner semantics are Phase 2 and throw <see cref="NotSupportedException"/> if reached.
/// </summary>
public struct JoinOneResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TFilter, TSelector>
	: IJoinResolver<TLeftKey, TLeftValue, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>, IComparable<TRightKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
	where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
	where TFilter : struct, IJoinFilter<
		CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>
	where TSelector : struct, IKeySelector<TLeftKey, TRightKey> {

	// ── Fields ───────────────────────────────────────────────────────────────

	internal readonly TRightCache Cache;
	internal TFilter Filter;
	internal TSelector Selector;
	private readonly bool _isInner;

	internal JoinOneResolver(TRightCache cache, TFilter filter, TSelector selector, bool isInner = false) {
		Cache = cache;
		Filter = filter;
		Selector = selector;
		_isInner = isInner;
	}

	// ── Static / property values ─────────────────────────────────────────────

	public static bool IsSorter { get; } = false;
	public bool Inner => _isInner;

	// ── Clone / CloneValue ───────────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult {
		ref var item = ref value.TUnsafeGetValAt<TRightValue>(index);
		item = item.Clone();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneValue(ref TRightValue value) => Cloner<TRightValue>.Clone(ref value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref TRightValue value) => CloneValue(ref value);

	// ── Container bridges ─────────────────────────────────────────────────────

	private ref struct UnsafeResolverContainer<TAccessor> : IJoinedResultContainer<TLeftKey, TRightValue>
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {
		private readonly bool _cloneOnAdd;
		private TAccessor _accessor;
		public int TotalCount => 1;

		public UnsafeResolverContainer(TAccessor accessor, bool cloneOnAdd) {
			_accessor = accessor;
			_cloneOnAdd = cloneOnAdd;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(TLeftKey foreignKey, TRightValue result) {
			// GetValueRefOrAddDefault: outer path always finds an existing slot (slot was created by
			// the outer base execute via rc.Add); inner path creates the slot here (outer base execute
			// runs AFTER the inner resolver and fills in the Left side for the survivor set).
			ref var valueRef = ref _accessor.GetValueRefOrAddDefault<TLeftKey, TRightValue>(foreignKey, out _);
			valueRef = _cloneOnAdd ? result.Clone() : result;
			return 0;
		}
	}

	// ── Core execution loop ──────────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteReverse<TContainer>(ref TContainer container, ReadOnlySpan<TLeftKey> leftKeys)
		where TContainer : struct, IJoinedResultContainer<TLeftKey, TRightValue>, allows ref struct {

		if (leftKeys.IsEmpty)
			return;

		// ── Build paired candidates: (leftKey, rightKey = Selector.Select(leftKey)) ─
		// Identity path: TLeftKey == TRightKey enforced by call-site signature; the cast is
		// zero-cost, and `IsIdentity` is JIT-folded so the selector-call branch disappears.
		// Selector path: per-key delegate invocation.
		var pairs = new ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>(leftKeys.Length);
		var handedOff = false;
		try {
			if (TSelector.IsIdentity) {
				foreach (var lk in leftKeys) {
					ref var leftKeyRef = ref Unsafe.AsRef(in lk);
					var rightKey = Unsafe.As<TLeftKey, TRightKey>(ref leftKeyRef);
					pairs.Add(new JoinedKeyPair<TLeftKey, TRightKey>(lk, rightKey));
				}
			}
			else {
				foreach (var lk in leftKeys)
					pairs.Add(new JoinedKeyPair<TLeftKey, TRightKey>(lk, Selector.Select(lk)));
			}

			if (!pairs.IsInitlized || pairs.Count == 0)
				return;

			// ── Wrap in paired core + combined builder ──────────────────────────────
			var dataCache = Cache.Cache;
			var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>(dataCache, pairs);
			var builder = new CacheQueryBuilderCombined<
				NonExecutableQuery<TRightCache>,
				PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>,
				TRightKey, TRightValue,
				Resolvers<BaseResolver<TRightKey, TRightValue>>,
				TRightValue>(
				new NonExecutableQuery<TRightCache>(Cache),
				pairedCore,
				new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
				0);

			// ── Apply filter (executor-agnostic) and execute paired ────────────────
			builder = Filter.Apply(builder);
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref container);
		}
		finally {
			if (!handedOff && pairs.IsInitlized)
				pairs.Dispose();
		}
	}

	void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(
		ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		var container = new UnsafeResolverContainer<TAccessor>(accessor, cloneOnAdd);
		ExecuteReverse(ref container, accessor.GetKeys<TLeftKey>());
	}

	// ── IndexedInner: single-pass right-attach + narrow ──────────────────────

	/// <summary>
	/// Ensures the outer-query's left candidate set is initialized before the chain's
	/// <c>Init(candidates.Count)</c> sizes <c>_results</c>. Calling <c>GetCandidates</c>
	/// triggers the executor's auto-populate-from-leftCache when no prior <c>UseIndex</c>
	/// narrowed the set; subsequent narrowing happens in <see cref="UnsafeExecuteIndexedInner"/>
	/// once we know which lefts actually have a right match.
	/// </summary>
	void IJoinResolver.PrepareIndexedInner<TExecutor>(
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool shouldPool,
		ref QueryResultsDisposer disposer) {
		_ = leftQuery.GetCandidates<TLeftKey>();
	}

	/// <summary>
	/// Inner-join attach path. <c>_results</c> is empty on entry (the outer base execute
	/// hasn't run yet). For each candidate, builds a <c>JoinedKeyPair</c>, runs the paired
	/// core which looks up <c>rightCache</c> and calls <c>container.Add</c> only on matches.
	/// Container's <c>Add</c> uses <c>GetValueRefOrAddDefault</c> to create the slot in
	/// <c>_results</c> with the right value written; the <c>Left</c> slot stays default until
	/// the outer base execute fills it after this method returns. Finally we narrow
	/// <c>leftQuery.Candidates</c> to the keys that produced a match — so the outer base
	/// execute only walks the survivor set.
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

		// ── 1. Seed paired set via bulk KeyIndex.IntersectValues (no resolver-side iteration) ──
		// Identity: reinterpret candidates as ValueSet<TRightKey, DefaultKeyComparer<TRightKey>> + pairs as
		// ValueSet<JoinedKeyPair<TRightKey,TRightKey>, DefaultKeyComparer<JoinedKeyPair<TRightKey,TRightKey>>> via Unsafe.As (TLeftKey == TRightKey
		// enforced by call-site signature). Single bulk call walks the cache's hash table
		// once per candidate, emitting pairs only for matches.
		// Selector: struct-dispatched selector variant — JIT-devirtualizes per closed generic.
		var pairs = new ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>(candidates.Count);
		var handedOff = false;
		try {
			var keyIndex = Cache.Cache.KeyIndex;
			if (TSelector.IsIdentity) {
				ref var candidatesAsRight = ref Unsafe.As<ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>>, ValueSet<TRightKey, DefaultKeyComparer<TRightKey>>>(ref candidates);
				ref var pairsAsRight = ref Unsafe.As<
					ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>,
					ValueSet<JoinedKeyPair<TRightKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TRightKey, TRightKey>>>>(ref pairs);
				keyIndex.IntersectValues(ref candidatesAsRight, ref pairsAsRight, add: true);
			}
			else {
				keyIndex.IntersectValues<TLeftKey, TSelector>(ref candidates, Selector, ref pairs, add: true);
			}

			if (!pairs.IsInitlized || pairs.Count == 0) {
				// Empty intersect: no left has a right match — narrow candidates to empty so
				// the outer base-execute drops everything (Inner semantic).
				candidates.IntersectWith(ReadOnlySpan<TLeftKey>.Empty);
				return;
			}

			// ── 2. Wrap pairs in paired core + combined builder ───────────────────
			var dataCache = Cache.Cache;
			var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>(dataCache, pairs);
			var builder = new CacheQueryBuilderCombined<
				NonExecutableQuery<TRightCache>,
				PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>,
				TRightKey, TRightValue,
				Resolvers<BaseResolver<TRightKey, TRightValue>>,
				TRightValue>(
				new NonExecutableQuery<TRightCache>(Cache),
				pairedCore,
				new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
				0);

			// ── 3. Apply filter strategy (executor-agnostic — works on paired core) ─
			builder = Filter.Apply(builder);

			// ── 4. Paired execute writes (leftKey, rightValue) directly into _results
			// for matched-and-filter-passed lefts. Misses (right-cache or predicate)
			// don't touch _results — slot stays default.
			var container = new UnsafeResolverContainer<TAccessor>(accessor, cloneOnAdd);
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref container);

			// ── 5. Post-walk: drop _results entries where THIS resolver's slot is
			// null (= miss or stale-from-prior-chained-resolver), then narrow
			// candidates to surviving keys. Single struct-dispatched pass. ──
			accessor.RetainNonNullSlots<TLeftKey, TRightValue>(ref candidates);
		}
		finally {
			if (!handedOff && pairs.IsInitlized)
				pairs.Dispose();
		}
	}

}

// ── Left-symmetric-index resolver ────────────────────────────────────────────

/// <summary>
/// Resolver for outer joins driven by a symmetric list index on the LEFT side.
/// <para>
/// <b>Shape A (no explicit right index — uses KeyIndex):</b>
/// <c>.JoinOne(leftIndex, rightCache, [filter], [arg])</c> —
/// <c>TLookupKey == TRightKey</c>; the left-index value IS the right primary key.
/// </para>
/// <para>
/// <b>Shape B (explicit right index):</b>
/// <c>.JoinOne(leftIndex, rightCache, rightIndex, [filter], [arg])</c> —
/// <c>rightIndex</c> must be unique (1:1 semantics).
/// </para>
/// Each left entity's index value (<typeparamref name="TLookupKey"/>) is used to look up
/// the right entity; multiple left entities sharing the same <typeparamref name="TLookupKey"/>
/// all receive the same right value (or <c>null</c> on miss/filter-out).
/// </summary>
public struct JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue, TFilter, TSelector>
	: IJoinResolver<TLeftKey, TLeftValue, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>, IComparable<TRightKey>
	where TLookupKey : notnull, IEquatable<TLookupKey>
	where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
	where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
	where TFilter : struct, IJoinFilter<
		CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>
	where TSelector : struct, IKeySelector<TLookupKey, TRightIndexKey> {

	// ── Fields ───────────────────────────────────────────────────────────────

	internal readonly CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> LeftIndex;
	internal readonly TRightCache RightCache;

	/// <summary>
	/// Null for Shape A (TRightIndexKey == TRightKey via call site; selector emits TRightKey directly).
	/// Non-null for Shape B (unique right-side index translating TRightIndexKey → TRightKey).
	/// </summary>
	[SuppressMessage("Design", "CA1051")]
	internal readonly CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey>? RightIndex;

	internal TFilter Filter;
	internal TSelector Selector;
	private readonly bool _isInner;

	internal JoinOneLeftSymResolver(
		CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
		TRightCache rightCache,
		CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey>? rightIndex,
		TFilter filter,
		TSelector selector,
		bool isInner = false) {
		LeftIndex = leftIndex;
		RightCache = rightCache;
		RightIndex = rightIndex;
		Filter = filter;
		Selector = selector;
		_isInner = isInner;
	}

	// ── Static / property values ─────────────────────────────────────────────

	public static bool IsSorter { get; } = false;
	public bool Inner => _isInner;

	// ── Clone / CloneValue ───────────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult {
		ref var item = ref value.TUnsafeGetValAt<TRightValue>(index);
		item = item.Clone();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneValue(ref TRightValue value) => Cloner<TRightValue>.Clone(ref value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref TRightValue value) => CloneValue(ref value);

	// ── Container bridges ─────────────────────────────────────────────────────

	/// <summary>
	/// Outer fan-out: receives <c>(set-of-lefts, rightValue)</c> per pair (where the set
	/// is borrowed directly from the symmetric index's storage), iterates the set, and
	/// writes the right value into each existing <c>_results</c> slot via
	/// <c>GetValueRef</c>. Lefts not in <c>_results</c> are silently skipped — that's the
	/// outer-attach contract (slots are pre-allocated by the outer base-execute).
	/// </summary>
	private ref struct OuterFanOutContainer<TAccessor>
		: IJoinedResultContainer<LeftKeySetView<TLeftKey>, TRightValue>
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {
		private TAccessor _accessor;
		private readonly bool _cloneOnAdd;
		public int TotalCount => 1;

		public OuterFanOutContainer(TAccessor accessor, bool cloneOnAdd) {
			_accessor = accessor;
			_cloneOnAdd = cloneOnAdd;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(LeftKeySetView<TLeftKey> lefts, TRightValue result) {
			// Reinterpret the public view back to the internal PooledSet — single-field
			// struct wrapper, identical layout, zero cost.
			var pooled = Unsafe.As<LeftKeySetView<TLeftKey>, PooledSet<TLeftKey, DefaultKeyComparer<TLeftKey>>>(ref lefts);
			foreach (var lk in pooled) {
				ref var slot = ref _accessor.GetValueRef<TLeftKey, TRightValue>(lk);
				if (!Unsafe.IsNullRef(in slot))
					slot = _cloneOnAdd ? result.Clone() : result;
			}
			return 0;
		}
	}

	/// <summary>
	/// Inner fan-out: receives <c>(set-of-lefts, rightValue)</c>, filters by the outer
	/// query's candidate set (so we only emit lefts that are still in scope), and creates
	/// new slots in <c>_results</c> for survivors via <c>GetValueRefOrAddDefault</c>.
	/// The outer base-execute runs AFTER us and fills in the Left side for the slots we
	/// created; lefts dropped here are absent from <c>_results</c> and from the narrowed
	/// candidate set, satisfying the inner-join semantic.
	/// </summary>
	private ref struct InnerFanOutContainer<TAccessor>
		: IJoinedResultContainer<LeftKeySetView<TLeftKey>, TRightValue>
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {
		private TAccessor _accessor;
		private ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> _candidates;
		private readonly bool _cloneOnAdd;
		public int TotalCount => 1;

		public InnerFanOutContainer(TAccessor accessor, ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> candidates, bool cloneOnAdd) {
			_accessor = accessor;
			_candidates = ref candidates;
			_cloneOnAdd = cloneOnAdd;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(LeftKeySetView<TLeftKey> lefts, TRightValue result) {
			var pooled = Unsafe.As<LeftKeySetView<TLeftKey>, PooledSet<TLeftKey, DefaultKeyComparer<TLeftKey>>>(ref lefts);
			foreach (var lk in pooled) {
				if (!_candidates.Contains(lk))
					continue;
				ref var slot = ref _accessor.GetValueRefOrAddDefault<TLeftKey, TRightValue>(lk, out _);
				slot = _cloneOnAdd ? result.Clone() : result;
			}
			return 0;
		}
	}

	// ── Pair seeding (dispatch by shape × identity/selector) ──────────────────
	//
	// All paths emit JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey> via the index's bulk
	// primitives — no resolver-side iteration. The pair's JoinedKey is a borrowed
	// reference to the index's internal PooledSet<TLeftKey, DefaultKeyComparer<TLeftKey>>, so multiple lefts sharing
	// the same lookupKey/rightKey collapse into a single pair naturally (ValueSet dedups
	// on .Key). At emission, FanOut iterates the borrowed set.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SeedPairsFromSpan(
		ReadOnlySpan<TLeftKey> input,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>>> pairs) {
		if (RightIndex is null) {
			// Shape A: TRightIndexKey == TRightKey at extension call site.
			if (TSelector.IsIdentity) {
				// TLookupKey == TRightKey at extension call site — reinterpret the
				// pair-set's .Key type-arg between TRightKey and TLookupKey (identical
				// closed type, zero cost).
				ref var pairsAsLookup = ref Unsafe.As<
					ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>>>,
					ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TLookupKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TLookupKey>>>>(ref pairs);
				LeftIndex.IntersectValues(input, ref pairsAsLookup, add: true);
			}
			else {
				// Selector emits TRightIndexKey == TRightKey at call site — same reinterpret.
				ref var pairsAsRight = ref Unsafe.As<
					ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>>>,
					ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightIndexKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightIndexKey>>>>(ref pairs);
				LeftIndex.IntersectValues<TRightIndexKey, TSelector>(input, Selector, ref pairsAsRight, add: true);
			}
		}
		else {
			// Shape B: RightIndex translates the intermediate key to TRightKey.
			if (TSelector.IsIdentity) {
				// Identity at call site: TLookupKey == TRightIndexKey, RightIndex is
				// keyed by TRightIndexKey. We reinterpret RightIndex's first type-arg
				// (the index key) to TLookupKey, which matches the bulk method's signature.
				var rightIndexAsLookup = Unsafe.As<CacheKeyValueIndex<TRightKey, TRightValue, TLookupKey>>(RightIndex);
				LeftIndex.IntersectValuesVia<TRightKey, TRightValue>(input, rightIndexAsLookup, ref pairs, add: true);
			}
			else {
				LeftIndex.IntersectValuesVia<TRightIndexKey, TSelector, TRightKey, TRightValue>(
					input, Selector, RightIndex, ref pairs, add: true);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SeedPairsFromSet(
		ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> input,
		ref ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>>> pairs) {
		if (RightIndex is null) {
			if (TSelector.IsIdentity) {
				ref var pairsAsLookup = ref Unsafe.As<
					ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>>>,
					ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TLookupKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TLookupKey>>>>(ref pairs);
				LeftIndex.IntersectValues(ref input, ref pairsAsLookup, add: true);
			}
			else {
				ref var pairsAsRight = ref Unsafe.As<
					ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>>>,
					ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightIndexKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightIndexKey>>>>(ref pairs);
				LeftIndex.IntersectValues<TRightIndexKey, TSelector>(ref input, Selector, ref pairsAsRight, add: true);
			}
		}
		else {
			if (TSelector.IsIdentity) {
				var rightIndexAsLookup = Unsafe.As<CacheKeyValueIndex<TRightKey, TRightValue, TLookupKey>>(RightIndex);
				LeftIndex.IntersectValuesVia<TRightKey, TRightValue>(ref input, rightIndexAsLookup, ref pairs, add: true);
			}
			else {
				LeftIndex.IntersectValuesVia<TRightIndexKey, TSelector, TRightKey, TRightValue>(
					ref input, Selector, RightIndex, ref pairs, add: true);
			}
		}
	}

	// ── Core execution loop ──────────────────────────────────────────────────

	void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(
		ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		var leftKeys = accessor.GetKeys<TLeftKey>();
		if (leftKeys.IsEmpty)
			return;

		var pairs = new ValueSet<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>, DefaultKeyComparer<JoinedKeyPair<LeftKeySetView<TLeftKey>, TRightKey>>>(leftKeys.Length);
		var handedOff = false;
		try {
			SeedPairsFromSpan(leftKeys, ref pairs);
			if (pairs.Count == 0)
				return;

			var pairedCore = new PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>(RightCache.Cache, pairs);
			var builder = new CacheQueryBuilderCombined<
				NonExecutableQuery<TRightCache>,
				PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>,
				TRightKey, TRightValue,
				Resolvers<BaseResolver<TRightKey, TRightValue>>,
				TRightValue>(
				new NonExecutableQuery<TRightCache>(RightCache),
				pairedCore,
				new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
				0);

			builder = Filter.Apply(builder);

			var wrapper = new OuterFanOutContainer<TAccessor>(accessor, cloneOnAdd);
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref wrapper);
		}
		finally {
			if (!handedOff && pairs.IsInitlized)
				pairs.Dispose();
		}
	}

	// ── IndexedInner ────────────────────────────────────────────────────────

	void IJoinResolver.PrepareIndexedInner<TExecutor>(
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool shouldPool,
		ref QueryResultsDisposer disposer) {
		_ = leftQuery.GetCandidates<TLeftKey>();
	}

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
		try {
			SeedPairsFromSet(ref candidates, ref pairs);
			if (pairs.Count == 0) {
				candidates.IntersectWith(ReadOnlySpan<TLeftKey>.Empty);
				return;
			}

			var pairedCore = new PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>(RightCache.Cache, pairs);
			var builder = new CacheQueryBuilderCombined<
				NonExecutableQuery<TRightCache>,
				PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>,
				TRightKey, TRightValue,
				Resolvers<BaseResolver<TRightKey, TRightValue>>,
				TRightValue>(
				new NonExecutableQuery<TRightCache>(RightCache),
				pairedCore,
				new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
				0);

			builder = Filter.Apply(builder);

			var wrapper = new InnerFanOutContainer<TAccessor>(accessor, ref candidates, cloneOnAdd);
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref wrapper);
		}
		finally {
			if (!handedOff && pairs.IsInitlized)
				pairs.Dispose();
		}

		// Post-walk: drop _results entries where THIS resolver's slot is null
		// (stale slots from prior chained resolvers — fan-out container's Add
		// touched only the lefts that matched here) and narrow candidates to the
		// surviving keys. Single struct-dispatched pass; zero per-pair miss
		// callback needed (which would be expensive for fan-out — one pair maps
		// to many lefts).
		accessor.RetainNonNullSlots<TLeftKey, TRightValue>(ref candidates);
	}

}

// ── Right-unique-index resolver ───────────────────────────────────────────────

/// <summary>
/// Resolver for outer joins driven by a <b>unique index on the RIGHT side</b>.
/// <para>
/// Call shape: <c>.JoinOne(rightCache, rightUniqueIndex, [filter], [arg])</c>
/// </para>
/// <para>
/// Each left entity's primary key is used to look up the right entity via
/// <paramref name="RightIndex"/> — a <see cref="CacheKeyValueIndex{TKey,TValue,TIndexKey}"/>
/// keyed by <typeparamref name="TLeftKey"/> that returns the corresponding
/// <typeparamref name="TRightKey"/>.  This is the natural Book → BookInfo pattern where
/// <c>BookInfo.BookId</c> has a <c>[DataCacheIndex(DataCacheIndexType.Unique)]</c> attribute.
/// </para>
/// Indexed-inner semantics are Phase 2 and throw <see cref="NotSupportedException"/> if reached.
/// </summary>
public struct JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue, TFilter, TSelector>
	: IJoinResolver<TLeftKey, TLeftValue, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>, IComparable<TRightKey>
	where TIndexKey : notnull, IEquatable<TIndexKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
	where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
	where TFilter : struct, IJoinFilter<
		CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>
	where TSelector : struct, IKeySelector<TLeftKey, TIndexKey> {

	// ── Fields ───────────────────────────────────────────────────────────────

	private readonly TRightCache _rightCache;

	/// <summary>
	/// A unique index on the right side keyed by <typeparamref name="TIndexKey"/>.
	/// With identity selector, <typeparamref name="TIndexKey"/> == <typeparamref name="TLeftKey"/>
	/// and the index acts as a direct leftKey → rightKey map. With explicit selector, the selector
	/// maps leftKey → indexKey before lookup.
	/// </summary>
	private readonly CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> _rightIndex;

	private TFilter _filter;
	private TSelector _selector;
	private readonly bool _isInner;

	internal JoinOneRightUniqueIndexResolver(
		TRightCache rightCache,
		CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
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
		ref var item = ref value.TUnsafeGetValAt<TRightValue>(index);
		item = item.Clone();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneValue(ref TRightValue value) => Cloner<TRightValue>.Clone(ref value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref TRightValue value) => CloneValue(ref value);

	// ── Container bridges ─────────────────────────────────────────────────────

	private ref struct UnsafeResolverContainer<TAccessor> : IJoinedResultContainer<TLeftKey, TRightValue>
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {
		private readonly bool _cloneOnAdd;
		private TAccessor _accessor;
		public int TotalCount => 1;

		public UnsafeResolverContainer(TAccessor accessor, bool cloneOnAdd) {
			_accessor = accessor;
			_cloneOnAdd = cloneOnAdd;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(TLeftKey foreignKey, TRightValue result) {
			// Outer path finds an existing slot (created by the outer base execute);
			// Inner path creates the slot here (outer base execute runs AFTER and fills Left).
			ref var valueRef = ref _accessor.GetValueRefOrAddDefault<TLeftKey, TRightValue>(foreignKey, out _);
			valueRef = _cloneOnAdd ? result.Clone() : result;
			return 0;
		}
	}

// ── Core execution loop ──────────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteReverse<TContainer>(ref TContainer container, ReadOnlySpan<TLeftKey> leftKeys)
		where TContainer : struct, IJoinedResultContainer<TLeftKey, TRightValue>, allows ref struct {

		if (leftKeys.IsEmpty)
			return;

		// ── 1. Build paired candidates ─────────────────────────────────────────
		// Identity path (TIndexKey == TLeftKey at call site): use the bulk
		// IntersectValues which produces pairs in one pass.
		// Selector path: per-leftKey TryGetValue; selector outputs TIndexKey then
		// pair is constructed as (leftKey, rightKey).
		var pairs = new ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>(leftKeys.Length);
		var handedOff = false;
		try {
			if (TSelector.IsIdentity) {
				// TIndexKey == TLeftKey at call site — reinterpret the span and the pair-set
				// using Unsafe.As (valid: identical types at JIT closure time, both fields are
				// laid out identically). Lets us reuse the native bulk IntersectValues.
				ref var pairsAsIndex = ref Unsafe.As<
					ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>,
					ValueSet<JoinedKeyPair<TIndexKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TRightKey>>>>(ref pairs);
				var indexKeys = MemoryMarshal.CreateReadOnlySpan(
					ref Unsafe.As<TLeftKey, TIndexKey>(ref MemoryMarshal.GetReference(leftKeys)),
					leftKeys.Length);
				_rightIndex.IntersectValues(indexKeys, ref pairsAsIndex, add: true);
			}
			else {
				// Bulk via strategy-struct dispatch. Selector is devirtualized per closed
				// generic; pairs are built directly with TLeftKey as JoinedKey (preserves
				// caller identity through the join), TRightKey as Key (right cache PK).
				_rightIndex.IntersectValues<TLeftKey, TSelector>(leftKeys, _selector, ref pairs, add: true);
			}

			if (!pairs.IsInitlized || pairs.Count == 0)
				return;

			// ── 2. Construct paired core + wrap in CacheQueryBuilderCombined ──────
			// The filter callback's TBuilder is the PAIRED combined builder, so UseIndex
			// and Where calls target PairedCacheQueryBuilderCoreCombined directly — no
			// intermediate dict or adapter.
			var dataCache = _rightCache.Cache;
			var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>(dataCache, pairs);
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

			// ── 3. Apply the user filter strategy ─────────────────────────────────
			// NoFilter.Apply() is JIT-elided; JoinFilter/JoinFilterWithArg call the
			// user lambda which may call UseIndex (intersecting pairs further) and/or Where
			// (setting a value predicate on the paired core).
			builder = _filter.Apply(builder);

			// ── 4. Native paired Execute — no dict, no per-emit adapter lookup ────
			// PairedCacheQueryBuilderCoreCombined.ExecutePaired writes directly to container
			// via InMemoryDataCache.TryGet<TForeignKey=TLeftKey, TContainer>: looks up by
			// pair.Key (rightKey) and calls container.Add(pair.JoinedKey=leftKey, rightValue).
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref container);
		}
		finally {
			if (!handedOff && pairs.IsInitlized)
				pairs.Dispose();
		}
	}

	void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(
		ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		var container = new UnsafeResolverContainer<TAccessor>(accessor, cloneOnAdd);
		ExecuteReverse(ref container, accessor.GetKeys<TLeftKey>());
	}

	// ── IndexedInner ────────────────────────────────────────────────────────

	/// <summary>
	/// Triggers the outer-executor's auto-populate-from-leftCache when no prior
	/// <c>UseIndex</c> narrowed the candidate set — see <see cref="JoinOneResolver{TLeftKey,TLeftValue,TRightCache,TRightKey,TRightValue,TFilter,TSelector}.PrepareIndexedInner"/>.
	/// </summary>
	void IJoinResolver.PrepareIndexedInner<TExecutor>(
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool shouldPool,
		ref QueryResultsDisposer disposer) {
		_ = leftQuery.GetCandidates<TLeftKey>();
	}

	void IJoinResolver.UnsafeExecuteIndexedInner<TAccessor, TExecutor>(
		ref TAccessor accessor,
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool isFirst,
		ref QueryResultsDisposer disposer) {
		ref var candidates = ref leftQuery.GetCandidates<TLeftKey>();
		if (!candidates.IsInitlized || candidates.Count == 0)
			return;

		// ── 1. Seed pairs via bulk _rightIndex.IntersectValues (no resolver iteration) ──
		var pairs = new ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>(candidates.Count);
		var handedOff = false;
		try {
			if (TSelector.IsIdentity) {
				ref var candidatesAsIndex = ref Unsafe.As<ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>>, ValueSet<TIndexKey, DefaultKeyComparer<TIndexKey>>>(ref candidates);
				ref var pairsAsIndex = ref Unsafe.As<
					ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>,
					ValueSet<JoinedKeyPair<TIndexKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TIndexKey, TRightKey>>>>(ref pairs);
				_rightIndex.IntersectValues(ref candidatesAsIndex, ref pairsAsIndex, add: true);
			}
			else {
				_rightIndex.IntersectValues<TLeftKey, TSelector>(ref candidates, _selector, ref pairs, add: true);
			}

			if (!pairs.IsInitlized || pairs.Count == 0) {
				candidates.IntersectWith(ReadOnlySpan<TLeftKey>.Empty);
				return;
			}

			// ── 2. Wrap pairs in paired core + apply filter + execute ───────────────
			var dataCache = _rightCache.Cache;
			var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>(dataCache, pairs);
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
			builder = _filter.Apply(builder);

			// Walk pairs; container writes matched (leftKey, rightValue) to _results.
			// Misses (right-cache or predicate) leave THIS resolver's slot at default.
			var container = new UnsafeResolverContainer<TAccessor>(accessor, cloneOnAdd);
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref container);

			// Post-walk: drop _results entries where THIS resolver's slot is null
			// (miss / stale-from-prior-chained), then narrow candidates to survivors.
			accessor.RetainNonNullSlots<TLeftKey, TRightValue>(ref candidates);
		}
		finally {
			if (!handedOff && pairs.IsInitlized)
				pairs.Dispose();
		}
	}

}

// ── Left-unique-index resolver ────────────────────────────────────────────────

/// <summary>
/// Resolver for outer joins driven by a <b>symmetric unique index on the LEFT side</b>.
/// <para>
/// Call shape: <c>.JoinOne(leftSymUniqueIndex, rightCache, [filter], [arg])</c>
/// </para>
/// <para>
/// Each left entity carries a 1:1 FK to a right entity, exposed as
/// <c>[DataCacheIndex(DataCacheIndexType.Unique, Symmetric = true)] TRightKey Foo</c>.
/// Codegen emits this as <see cref="CacheSymmetricUniqueIndex{TKey,TValue,TIndexKey}"/>
/// whose <c>Reverse</c> property gives the <c>TLeftKey → TRightKey</c> direction needed
/// here. Each input <typeparamref name="TLeftKey"/> maps to at most one
/// <typeparamref name="TRightKey"/> (bijective by definition of the unique index) — no
/// fan-out side-map is required, unlike the symmetric-list-index variant.
/// </para>
/// Indexed-inner semantics are Phase 2 and throw <see cref="NotSupportedException"/> if reached.
/// </summary>
public struct JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue, TFilter, TSelector>
	: IJoinResolver<TLeftKey, TLeftValue, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>, IComparable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>, IComparable<TRightKey>
	where TIndexKey : notnull, IEquatable<TIndexKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
	where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
	where TFilter : struct, IJoinFilter<
		CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>
	where TSelector : struct, IKeySelector<TIndexKey, TRightKey> {

	// ── Fields ───────────────────────────────────────────────────────────────

	internal readonly TRightCache RightCache;

	/// <summary>
	/// Symmetric unique index on the LEFT cache, indexed by <typeparamref name="TIndexKey"/>.
	/// Its <see cref="CacheSymmetricUniqueIndex{TKey,TValue,TIndexKey}.Reverse"/> maps
	/// <typeparamref name="TLeftKey"/> → <typeparamref name="TIndexKey"/>; the selector (or
	/// identity for the legacy <typeparamref name="TIndexKey"/> == <typeparamref name="TRightKey"/>
	/// case) translates to the final right primary key.
	/// </summary>
	internal readonly CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> LeftIndex;

	internal TFilter Filter;
	internal TSelector Selector;
	private readonly bool _isInner;

	internal JoinOneLeftUniqueIndexResolver(
		CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
		TRightCache rightCache,
		TFilter filter,
		TSelector selector,
		bool isInner = false) {
		LeftIndex = leftIndex;
		RightCache = rightCache;
		Filter = filter;
		Selector = selector;
		_isInner = isInner;
	}

	// ── Static / property values ─────────────────────────────────────────────

	public static bool IsSorter { get; } = false;
	public bool Inner => _isInner;

	// ── Clone / CloneValue ───────────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult {
		ref var item = ref value.TUnsafeGetValAt<TRightValue>(index);
		item = item.Clone();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneValue(ref TRightValue value) => Cloner<TRightValue>.Clone(ref value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clone(ref TRightValue value) => CloneValue(ref value);

	// ── Container bridges ─────────────────────────────────────────────────────

	private ref struct UnsafeResolverContainer<TAccessor> : IJoinedResultContainer<TLeftKey, TRightValue>
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct {
		private readonly bool _cloneOnAdd;
		private TAccessor _accessor;
		public int TotalCount => 1;

		public UnsafeResolverContainer(TAccessor accessor, bool cloneOnAdd) {
			_accessor = accessor;
			_cloneOnAdd = cloneOnAdd;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(TLeftKey foreignKey, TRightValue result) {
			// Outer path finds an existing slot (created by the outer base execute);
			// Inner path creates the slot here (outer base execute runs AFTER and fills Left).
			ref var valueRef = ref _accessor.GetValueRefOrAddDefault<TLeftKey, TRightValue>(foreignKey, out _);
			valueRef = _cloneOnAdd ? result.Clone() : result;
			return 0;
		}
	}

// ── Core execution loop ──────────────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteReverse<TContainer>(ref TContainer container, ReadOnlySpan<TLeftKey> leftKeys)
		where TContainer : struct, IJoinedResultContainer<TLeftKey, TRightValue>, allows ref struct {

		if (leftKeys.IsEmpty)
			return;

		// ── 1. Build paired candidates: LeftIndex.Reverse maps TLeftKey → TIndexKey (1:1).
		// Identity path (TIndexKey == TRightKey at call site): bulk-intersect via the native
		// IntersectValues which writes pairs directly. Selector path: per-leftKey TryGetValue
		// on Reverse, then Selector.Select transforms TIndexKey → TRightKey.
		var pairs = new ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>(leftKeys.Length);
		var handedOff = false;
		try {
			if (TSelector.IsIdentity) {
				// TIndexKey == TRightKey — reinterpret the pair set so it's keyed by TIndexKey
				// (valid at JIT closure time, identical layouts).
				ref var pairsAsIndex = ref Unsafe.As<
					ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>,
					ValueSet<JoinedKeyPair<TLeftKey, TIndexKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TIndexKey>>>>(ref pairs);
				LeftIndex.Reverse.IntersectValues(leftKeys, ref pairsAsIndex, add: true);
			}
			else {
				// Bulk chain via strategy-struct dispatch:
				//   leftKey → Reverse.TryGetValue → idx (TIndexKey) → Selector.Select → rk (TRightKey)
				// Reverse's TIndexKey is TLeftKey (it's the inverse of the original index), and
				// its TKey is the original TIndexKey — matches the tail selector's input.
				// Devirtualized per closed generic; one call replaces the per-leftKey foreach.
				LeftIndex.Reverse.IntersectValuesChain<TRightKey, TSelector>(leftKeys, Selector, ref pairs, add: true);
			}

			if (!pairs.IsInitlized || pairs.Count == 0)
				return;

			// ── 2. Construct paired core + wrap in CacheQueryBuilderCombined ──────
			var dataCache = RightCache.Cache;
			var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>(dataCache, pairs);
			var builder = new CacheQueryBuilderCombined<
				NonExecutableQuery<TRightCache>,
				PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>,
				TRightKey, TRightValue,
				Resolvers<BaseResolver<TRightKey, TRightValue>>,
				TRightValue>(
				new NonExecutableQuery<TRightCache>(RightCache),
				pairedCore,
				new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
				0);

			// ── 3. Apply the user filter strategy (executor-agnostic). ────────────
			builder = Filter.Apply(builder);

			// ── 4. Native paired Execute — direct write to the outer container.
			// ExecutePaired calls container.Add(pair.JoinedKey=leftKey, rightValue) per emit.
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref container);
		}
		finally {
			if (!handedOff && pairs.IsInitlized)
				pairs.Dispose();
		}
	}

	void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(
		ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer) {
		var container = new UnsafeResolverContainer<TAccessor>(accessor, cloneOnAdd);
		ExecuteReverse(ref container, accessor.GetKeys<TLeftKey>());
	}

	// ── IndexedInner ────────────────────────────────────────────────────────

	void IJoinResolver.PrepareIndexedInner<TExecutor>(
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool shouldPool,
		ref QueryResultsDisposer disposer) {
		_ = leftQuery.GetCandidates<TLeftKey>();
	}

	void IJoinResolver.UnsafeExecuteIndexedInner<TAccessor, TExecutor>(
		ref TAccessor accessor,
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool isFirst,
		ref QueryResultsDisposer disposer) {
		ref var candidates = ref leftQuery.GetCandidates<TLeftKey>();
		if (!candidates.IsInitlized || candidates.Count == 0)
			return;

		// ── 1. Seed pairs via bulk LeftIndex.Reverse intersect (no resolver iteration) ──
		// Identity (TIndexKey == TRightKey at call site): reinterpret pairs and call
		// the native ref-ValueSet IntersectValues on Reverse. Selector: bulk chain
		// (leftKey → Reverse.TryGetValue → idx → Selector.Select → rightKey).
		var pairs = new ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>(candidates.Count);
		var handedOff = false;
		try {
			if (TSelector.IsIdentity) {
				ref var pairsAsIndex = ref Unsafe.As<
					ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>,
					ValueSet<JoinedKeyPair<TLeftKey, TIndexKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TIndexKey>>>>(ref pairs);
				LeftIndex.Reverse.IntersectValues(ref candidates, ref pairsAsIndex, add: true);
			}
			else {
				LeftIndex.Reverse.IntersectValuesChain<TRightKey, TSelector>(ref candidates, Selector, ref pairs, add: true);
			}

			if (!pairs.IsInitlized || pairs.Count == 0) {
				candidates.IntersectWith(ReadOnlySpan<TLeftKey>.Empty);
				return;
			}

			// ── 2. Wrap pairs + apply filter + execute ───────────────────────────────
			var dataCache = RightCache.Cache;
			var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>(dataCache, pairs);
			var builder = new CacheQueryBuilderCombined<
				NonExecutableQuery<TRightCache>,
				PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>,
				TRightKey, TRightValue,
				Resolvers<BaseResolver<TRightKey, TRightValue>>,
				TRightValue>(
				new NonExecutableQuery<TRightCache>(RightCache),
				pairedCore,
				new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
				0);
			builder = Filter.Apply(builder);

			// Walk pairs; container writes matched (leftKey, rightValue) to _results.
			// Misses (right-cache or predicate) leave THIS resolver's slot at default.
			var container = new UnsafeResolverContainer<TAccessor>(accessor, cloneOnAdd);
			handedOff = true;
			Unsafe.AsRef(in builder._leftQuery).ExecutePaired(ref container);

			// Post-walk: drop _results entries where THIS resolver's slot is null
			// (miss / stale-from-prior-chained), then narrow candidates to survivors.
			accessor.RetainNonNullSlots<TLeftKey, TRightValue>(ref candidates);
		}
		finally {
			if (!handedOff && pairs.IsInitlized)
				pairs.Dispose();
		}
	}

}
