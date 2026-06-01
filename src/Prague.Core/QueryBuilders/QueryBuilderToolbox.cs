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

	internal void UnsafeExecuteWithAccessor<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool,
		ref QueryResultsDisposer disposer)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct;

	internal void UnsafeSortResults<TFullResult>(ref QueryResults<TFullResult> results, int skip, int take)
		=> throw new InvalidOperationException("Join resolver is not sortable");

	internal void UnsafeSortResults<TKey, TFullResult>(ref ValueDictionary<TKey, TFullResult, DefaultKeyComparer<TKey>> results, int skip, int take)
		where TFullResult: struct, IJoinResult
		where TKey : notnull, IEquatable<TKey> => throw new InvalidOperationException("Join resolver is not sortable");

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

	public bool Equals(JoinedKeyPair<TJoinedKey, TKey> other) => Key.Equals(other.Key);

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
