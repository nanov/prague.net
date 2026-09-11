namespace Prague.Core;

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using Collections;

public enum RangeValueType : byte {
	None,
	Than,
	ThanOrEqual
}

public readonly struct RangeValue<TIndexKey> where TIndexKey : IComparable<TIndexKey> {
	public readonly TIndexKey Value;
	public readonly RangeValueType Type;

	public RangeValue(RangeValueType type, TIndexKey value) {
		Value = value;
		Type = type;
	}

	[Pure]
	public void Deconstruct(out RangeValueType t, out TIndexKey v) => (t, v) = (Type, Value);
}


public interface IJoinResolver {
	static abstract bool IsSorter { get; }
	bool Inner { get; }

	/// <summary>
	/// Sorter-only: did the caller ask for the bounded top-K plan (<c>SortBounded</c>) rather than the
	/// classic full sort (<c>Sort</c>)? The two plans order comparer-equal rows differently — bounded
	/// breaks ties by encounter order so consecutive pages partition the result, classic leaves them
	/// unspecified — so the choice is the caller's, never inferred. Default: no.
	/// </summary>
	bool AllowsBounded => false;

	/// <summary>
	/// Sorter-only: does this sorter order by the LEFT value itself (TResult == TLeftValue), so the
	/// bounded plan can drive it? Answers the question without handing
	/// the comparer out — erasing it to <see cref="IComparer{T}"/> boxes a struct
	/// comparer, and the gate has to be free for queries that then fall back. Default: no.
	/// </summary>
	bool OrdersByLeftValues<TLeft>() => false;

	/// <summary>
	/// Sorter-only: compare two LEFT values with the caller's comparer. Callers must have gated on
	/// <see cref="OrdersByLeftValues{TLeft}"/>. Keeping the comparison behind the resolver — which
	/// reaches the bounded containers as a struct type parameter — is what makes the bounded plan
	/// allocation-free: no <see cref="Comparison{T}"/> delegate, no box, and both the resolver call
	/// and the user comparer devirtualize per closed generic.
	/// </summary>
	int CompareLeftValues<TLeft>(TLeft a, TLeft b) => throw new InvalidOperationException("Join resolver is not sortable");

	internal void UnsafeExecuteWithAccessor<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct;

	internal void UnsafeSortResults<TFullResult>(ref QueryResults<TFullResult> results, int skip, int take)
		=> throw new InvalidOperationException("Join resolver is not sortable");

	internal void UnsafeSortResults<TKey, TFullResult>(ref ValueDictionary<TKey, TFullResult, DefaultKeyComparer<TKey>> results, int skip, int take)
		where TFullResult: struct, IJoinResult
		where TKey : notnull, IEquatable<TKey> => throw new InvalidOperationException("Join resolver is not sortable");


	/// <summary>
	/// Whether this resolver supports narrow-only inner execution
	/// (<see cref="UnsafeNarrowIndexedInner{TExecutor}"/>). JIT-folded per instantiation;
	/// the bounded top-K path probes this before committing to the bounded plan.
	/// </summary>
	static virtual bool SupportsNarrowOnly => false;

	/// <summary>Returns any values retained by a bounded inner pass, including on failure.</summary>
	internal void ReleaseNarrowedInner() { }

	/// <summary>
	/// Narrow-only inner execution: intersects the outer query's candidate set with the lefts
	/// that have a (filter-passing) right match, without materializing full joined rows.
	/// The resolver retains the checked values until fill and releases them via ReleaseNarrowedInner.
	/// Only invoked by the bounded top-K path, and only on resolvers whose
	/// <see cref="SupportsNarrowOnly"/> is true.
	/// </summary>
	internal void UnsafeNarrowIndexedInner<TExecutor>(ref TExecutor leftQuery)
		where TExecutor : struct, IUnsafeCandidatesExecutor
		=> throw new InvalidOperationException("Resolver does not support narrow-only inner execution");

	/// <summary>
	/// Bounded-path fill for an inner resolver whose membership was already decided by
	/// <see cref="UnsafeNarrowIndexedInner{TExecutor}"/>: attaches the retained, checked values to
	/// the selected page rows without re-reading the cache or re-applying the join filter.
	/// Only invoked on resolvers whose <see cref="SupportsNarrowOnly"/> is true.
	/// </summary>
	internal void UnsafeFillNarrowedInner<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct
		=> throw new InvalidOperationException("Resolver does not support narrow-only inner execution");

	void UnsafeExecuteIndexedInner<TAccessor, TExecutor>(
		ref TAccessor accessor,
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool isFirst,
		ref QueryResultsDisposer disposer)
		where TExecutor : struct, IUnsafeCandidatesExecutor
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct;

	void PrepareIndexedInner<TExecutor>(
		ref TExecutor leftQuery,
		bool cloneOnAdd,
		bool shouldPool,
		ref QueryResultsDisposer disposer) where TExecutor : struct, IUnsafeCandidatesExecutor;

	static abstract void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult;

	/// <summary>
	/// Does this resolver family implement the per-left point lookup of
	/// <see cref="IFusableJoinOne{TLeftKey,TLeftValue,TRightValue}" /> (the four <c>JoinOne</c> families)?
	/// JIT-folded per instantiation; the frozen pipeline's chain walkers skip everything else.
	/// </summary>
	static virtual bool SupportsFusedLookup => false;

	/// <summary>
	/// Decided once at build: can this resolver's right be looked up per left with no filter callback
	/// (<see cref="IFusableJoinOne{TLeftKey,TLeftValue,TRightValue}.CanFuse" />)? Default: no.
	/// </summary>
	bool CanFuse => false;

	/// <summary>
	/// Does this family's <b>inner</b> execution emit its rows in an order the fused per-left fill cannot
	/// reproduce? True for the left-symmetric join alone: its pair set is keyed by the lookup key, so the
	/// fan-out creates the rows grouped by right — several lefts of one bucket together — while the fused
	/// pass keeps the seed's order. The two agree as sets, never as sequences, so the planner leaves such a
	/// chain to the replay unless <see cref="FrozenOptions.FuseSymmetricInnerJoins" /> is set. An
	/// <i>outer</i> left-symmetric join is unaffected: its rows already exist, the fan-out only fills them.
	/// JIT-folded per instantiation.
	/// </summary>
	static virtual bool FusedInnerRegroups => false;

	/// <summary>
	/// Is this a <c>JoinMany</c> family (right-list, left-symmetric, collection)? The frozen pipeline admits
	/// such a chain (design §7.2 as implemented): without a filter callback (<see cref="CanFuse" />) and with
	/// no classic sorter before it, the resolver fills its slots in the pass's fill walk through
	/// <see cref="UnsafeFillFusedRows{TAccessor}" /> — the frozen per-left fill of <c>JoinManyFusedFill</c>,
	/// one append-only buffer, no pair set — and narrows through <see cref="UnsafeNarrowFused{TKey,TValue}" />;
	/// otherwise its own two-pass fan-out runs after the pass over the rows the pass formed
	/// (<see cref="UnsafeExecuteWithAccessor{TAccessor}" />), an inner one then dropping the rows whose
	/// slot stayed empty (<see cref="UnsafePruneEmptyManySlots{TAccessor}" />). JIT-folded per instantiation;
	/// decided at build, never per row.
	/// </summary>
	static virtual bool IsMany => false;

	/// <summary>
	/// The inner <c>JoinMany</c> narrowing of the frozen pipeline: after <see cref="UnsafeExecuteWithAccessor{TAccessor}" />
	/// filled this resolver's slots, drop the rows whose slot holds no right — the eager
	/// <c>RetainNonEmptyManySlots</c> rule (a left without a right, or whose rights the filter rejected, is
	/// neither emitted nor counted). Only invoked on inner resolvers whose <see cref="IsMany" /> is true.
	/// </summary>
	internal void UnsafePruneEmptyManySlots<TAccessor>(ref TAccessor accessor)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct
		=> throw new InvalidOperationException("Resolver is not a JoinMany");

	/// <summary>
	/// The frozen pipeline's fused fill (design §7.1): writes this resolver's right slot of every row in
	/// <paramref name="accessor" /> with one point lookup per row — the right on a hit (cloned when
	/// <paramref name="cloneOnAdd" />, as the paired walk's add would), the slot's default on a miss — and,
	/// for an inner join, drops the rows without a right (<see cref="IUnsafeValueAccessor.PruneNullSlots{TRightValue}" />).
	/// Returns true when rows were dropped. One call per resolver per execution: the per-row work is the
	/// resolver's own lookups and the slot write, no chain dispatch. Only invoked on resolvers whose
	/// <see cref="SupportsFusedLookup" /> is true — and, for a fused <c>JoinMany</c> (<see cref="IsMany" />,
	/// <see cref="CanFuse" />), the frozen per-left fill: it rents the slots' buffer (pooled when the
	/// <paramref name="disposer" /> is active, and registered with it) sized by <paramref name="sizeHint" />,
	/// the previous execution's total, which it writes back. A <c>JoinOne</c> ignores those three.
	/// </summary>
	internal bool UnsafeFillFusedRows<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer, ref int sizeHint)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct
		=> throw new InvalidOperationException("Resolver has no fused lookup");

	/// <summary>
	/// The inner narrowing of the frozen pipeline (design §7.1, the eager <c>CountCoreJoined</c> rule):
	/// keeps, in order, the keys that have a right — moving <paramref name="values" /> in lockstep when
	/// given (empty for a count) — and returns the survivor count. <typeparamref name="TKey" /> is the
	/// chain's left key type; the resolver reinterprets it to its own. Only invoked on inner resolvers
	/// whose <see cref="SupportsFusedLookup" /> is true, or fused <c>JoinMany</c>s (a left keeps its row when
	/// its bucket holds a right the store has).
	/// </summary>
	internal int UnsafeNarrowFused<TKey, TValue>(Span<TKey> keys, Span<TValue> values)
		where TKey : notnull, IEquatable<TKey>
		=> throw new InvalidOperationException("Resolver has no fused lookup");
}
public interface IJoinResolver<TLeftKey, TLeftValue> : IJoinResolver
	where TLeftKey : notnull, IEquatable<TLeftKey> {



}

/// <summary>
/// Interface for unified join resolvers that can handle both One and Many joins.
/// </summary>
public interface IJoinResolver<TLeftKey, TLeftValue, TRightValue> : IJoinResolver<TLeftKey, TLeftValue>, ICloner<TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey> where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	static abstract void CloneValue(ref TRightValue value);
}

/// <summary>
/// Interface for Many join resolvers that need keyed container initialization.
/// </summary>
internal interface IJoinManyResolver<TLeftKey, TLeftValue, TInnerValue>
	: IJoinResolver<TLeftKey, TLeftValue, QueryResults<TInnerValue>>
	where TLeftKey : notnull, IEquatable<TLeftKey> where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
	internal void ExecuteReverseMany<TContainer>(ref TContainer container, ReadOnlySpan<TLeftKey> keys)
		where TContainer : struct, IJoinedKeyedResultContainer<TLeftKey, TInnerValue>, allows ref struct;
}

public interface ILeftValueAccessor<TLeftValue> {
	ReadOnlySpan<TLeftValue> Values { get; }
}

public interface IUnsafeValueAccessor {
	ref TRightValue GetValueRef<TKey, TRightValue>(TKey key) where TKey : IEquatable<TKey>;
	ref TRightValue GetValueRefOrAddDefault<TKey, TRightValue>(TKey key, out bool exists) where  TKey : IEquatable<TKey>;

	ReadOnlySpan<TKey> GetKeys<TKey>() where TKey : IEquatable<TKey>;

	/// <summary>
	/// Inner-join post-walk cleanup: drops result-map entries where THIS accessor's
	/// slot is null/default (i.e., this resolver didn't match the key — either it
	/// never wrote to that slot, or the slot was created earlier by a prior chained
	/// resolver), then narrows <paramref name="candidates"/> to the surviving keys.
	/// Single struct-dispatched pass; zero heap allocation.
	/// </summary>
	internal void RetainNonNullSlots<TKey, TRightValue>(ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates) where TKey : IEquatable<TKey>;

	/// <summary>
	/// JoinMany analog of <see cref="RetainNonNullSlots"/>: drops result-map entries
	/// where THIS accessor's slot is an empty <see cref="QueryResults{TInnerValue}"/>
	/// (Count == 0 — no rights matched OR filter rejected them all), then narrows
	/// <paramref name="candidates"/> to surviving keys. Used by InnerJoinMany.
	/// </summary>
	internal void RetainNonEmptyManySlots<TKey, TInnerValue>(ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates) where TKey : IEquatable<TKey>;

	/// <summary>This accessor's slot of the row at <paramref name="index" /> — the rows in <see cref="GetKeys{TKey}" />'s order. The fused JoinOne fill's per-row write.</summary>
	internal ref TRightValue GetSlotAt<TRightValue>(int index);

	/// <summary>Drops the rows whose slot here is null / default (an inner fused join's misses), keeping the order of the rest.</summary>
	internal void PruneNullSlots<TRightValue>();

	/// <summary>Drops the rows whose slot here is an empty <see cref="QueryResults{TInnerValue}" /> (an inner <c>JoinMany</c>'s lefts without a right after the frozen pipeline's post-pass fill), keeping the order of the rest.</summary>
	internal void PruneEmptyManySlots<TInnerValue>();
}
public interface IUnsafeValueAccessor<TLeftKey> : IUnsafeValueAccessor
	where TLeftKey : IEquatable<TLeftKey> {
	ReadOnlySpan<TLeftKey> Keys { get; }

	ref TRightValue GetValueRef<TRightValue>(TLeftKey key);
	ref TRightValue GetValueRefOrAddDefault<TRightValue>(TLeftKey key, out bool exists);
}
/// <summary>
/// Accessor interface for getting references to value slots by key.
/// </summary>
public interface IValueAccessor<TLeftKey, TRightValue>
	where TLeftKey : IEquatable<TLeftKey> {
	ReadOnlySpan<TLeftKey> Keys { get; }

	ref TRightValue GetValueRef(TLeftKey key);
	ref TRightValue GetValueRefOrAddDefault(TLeftKey key, out bool exists);
}


internal static class JoinedKeyPair {
	public static JoinedKeyPair<TJoinedKey, TKey> Create<TJoinedKey, TKey>(TJoinedKey joinedKey, TKey key)
		where TJoinedKey : notnull where TKey : notnull =>
		new(joinedKey, key);

	public static JoinedKeyPair<TJoinedKey, TKey> Create<TJoinedKey, TKey>(TKey key)
		where TJoinedKey : notnull where TKey : notnull =>
		new(default!, key);
}

internal struct JoinedKeyPair<TJoinedKey, TKey> : IEquatable<JoinedKeyPair<TJoinedKey, TKey>>
	where TJoinedKey : notnull
	where TKey : notnull {
	public TJoinedKey JoinedKey;
	public TKey Key;

	public JoinedKeyPair(TJoinedKey joinedKey, TKey key) {
		JoinedKey = joinedKey;
		Key = key;
	}

	// TKey is constrained to notnull only, so Key.Equals(other.Key) binds to object.Equals(object) and
	// boxes the key on every call — measured 24 B per comparison for an int key, on the pair set every
	// JoinMany probes. EqualityComparer<TKey>.Default is a JIT intrinsic: for a TKey implementing
	// IEquatable<TKey> it becomes a direct call to the typed Equals with no box and no dispatch.
	public bool Equals(JoinedKeyPair<TJoinedKey, TKey> other) => EqualityComparer<TKey>.Default.Equals(Key, other.Key);

	public override bool Equals([NotNullWhen(true)] object? obj) =>
		obj is JoinedKeyPair<TJoinedKey, TKey> other && Equals(other);

	public override int GetHashCode() => Key.GetHashCode();

	public static IntoTrait Into = new();

	public static IntoTrait IntoKeyed(TJoinedKey key) => new(key);

	public static IntoJoinKeyTrait IntoJoinKey = new ();

	public struct IntoJoinKeyTrait : IInto<JoinedKeyPair<TJoinedKey, TKey>, TJoinedKey> {

		public IntoJoinKeyTrait() {
		}

		public TJoinedKey Into(JoinedKeyPair<TJoinedKey, TKey> i) => i.JoinedKey;

		public JoinedKeyPair<TJoinedKey, TKey> From(TJoinedKey into) => throw new InvalidOperationException();
	}

	public struct IntoTrait : IInto<TKey, JoinedKeyPair<TJoinedKey, TKey>> {
		private readonly TJoinedKey _key = default!;

		public IntoTrait(TJoinedKey key) {
			_key = key;
		}

		public JoinedKeyPair<TJoinedKey, TKey> Into(TKey from) => new(_key, from);

		public TKey From(JoinedKeyPair<TJoinedKey, TKey> into) => into.Key;
	}
}
