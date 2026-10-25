namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   The bucket a <c>JoinMany</c> family resolves for one left — the family's own index reads in the
///   family's own order (right-list: the selector then the right list index; left-symmetric: the left
///   index's reverse map, the selector, the right list index; collection: the index half) — so the frozen
///   fill's staleness window is the eager fan-out's. A struct type parameter: the call devirtualizes. Public
///   because the three sources appear in public resolver type arguments (<see cref="JoinCountResolver{TLeftKey,TLeftValue,TRightCache,TRightKey,TRightValue,TBuckets,TFilter}" />);
///   the read itself stays internal.
/// </summary>
public interface IManyBucketSource<TLeftKey, TRightKey>
	where TRightKey : notnull, IEquatable<TRightKey> {
	/// <summary>False when the left has no bucket or an empty one; <paramref name="bucket" /> is then unspecified.</summary>
	internal bool TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket);

	/// <summary>
	///   True when the bucket holds keys the left references rather than keys of the right cache's own index — a
	///   reference may name a right that is not resident, so a bucket's size is not its delivered count. Only the
	///   forward collection read (<see cref="JoinManyCollectionForwardBuckets{TLeftKey,TRightKey,TOwnerValue}" />).
	/// </summary>
	internal static virtual bool KeysAreReferences => false;
}

/// <summary>
///   The right-list family's bucket read: the selector (identity reinterprets), then the right list index.
///   Used by <see cref="JoinManyRightListIndexResolver{TLeftKey,TLeftValue,TRightCache,TRightKey,TIndexKey,TRightValue,TFilter,TSelector}" />
///   and the count resolver over the same index.
/// </summary>
public struct JoinManyRightListBuckets<TLeftKey, TRightKey, TRightValue, TIndexKey, TSelector> : IManyBucketSource<TLeftKey, TRightKey>
	where TRightKey : notnull, IEquatable<TRightKey>
	where TIndexKey : notnull, IEquatable<TIndexKey>
	where TSelector : struct, IKeySelector<TLeftKey, TIndexKey> {
	private readonly CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> _rightIndex;
	private TSelector _selector;

	internal JoinManyRightListBuckets(CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> rightIndex, TSelector selector) {
		_rightIndex = rightIndex;
		_selector = selector;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	bool IManyBucketSource<TLeftKey, TRightKey>.TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket)
		=> TryGetBucket(leftKey, out bucket);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket) {
		var indexKey = TSelector.IsIdentity ? Unsafe.As<TLeftKey, TIndexKey>(ref leftKey) : _selector.Select(leftKey);
		bucket = _rightIndex.GetValuesUnsafe(indexKey);
		return bucket is { Count: > 0 };
	}
}

/// <summary>
///   The left-symmetric family's bucket read: the left index's reverse map → lookup key → selector → the
///   right list index. Used by <see cref="JoinManyLeftSymResolver{TLeftKey,TLeftValue,TRightCache,TLookupKey,TRightIndexKey,TRightKey,TRightValue,TFilter,TSelector}" />
///   and the count resolver over the same indexes.
/// </summary>
public struct JoinManyLeftSymBuckets<TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightKey, TRightValue, TSelector> : IManyBucketSource<TLeftKey, TRightKey>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TLookupKey : notnull, IEquatable<TLookupKey>
	where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
	where TRightKey : notnull, IEquatable<TRightKey>
	where TSelector : struct, IKeySelector<TLookupKey, TRightIndexKey> {
	private readonly CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> _leftIndex;
	private readonly CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> _rightIndex;
	private TSelector _selector;

	internal JoinManyLeftSymBuckets(CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
		CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex, TSelector selector) {
		_leftIndex = leftIndex;
		_rightIndex = rightIndex;
		_selector = selector;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	bool IManyBucketSource<TLeftKey, TRightKey>.TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket)
		=> TryGetBucket(leftKey, out bucket);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket) {
		if (!_leftIndex.Reverse.TryGetValue(leftKey, out var lookupKey)) {
			bucket = null!;
			return false;
		}

		var rightIndexKey = TSelector.IsIdentity ? Unsafe.As<TLookupKey, TRightIndexKey>(ref lookupKey) : _selector.Select(lookupKey);
		bucket = _rightIndex.GetValuesUnsafe(rightIndexKey);
		return bucket is { Count: > 0 };
	}
}

/// <summary>
///   The collection family's bucket read: the index half that answers "rights for a left" (the forward half
///   for the reverse direction, the reverse half for the forward one). <typeparamref name="TOwnerValue" /> is
///   that half's phantom value type. Used by <see cref="JoinManyCollectionResolver{TLeftKey,TLeftValue,TRightCache,TRightKey,TRightValue,TOwnerValue,TFilter}" />
///   and the count resolver over the same half.
/// </summary>
public readonly struct JoinManyCollectionBuckets<TLeftKey, TRightKey, TOwnerValue> : IManyBucketSource<TLeftKey, TRightKey>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey> {
	private readonly CacheKeyValueListIndex<TRightKey, TOwnerValue, TLeftKey> _rightsIndex;

	internal JoinManyCollectionBuckets(CacheKeyValueListIndex<TRightKey, TOwnerValue, TLeftKey> rightsIndex) => _rightsIndex = rightsIndex;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	bool IManyBucketSource<TLeftKey, TRightKey>.TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket)
		=> TryGetBucket(leftKey, out bucket);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket) {
		bucket = _rightsIndex.GetValuesUnsafe(leftKey);
		return bucket is { Count: > 0 };
	}
}

/// <summary>
///   The forward collection read for <see cref="JoinCountResolver{TLeftKey,TLeftValue,TRightCache,TRightKey,TRightValue,TBuckets,TFilter}" />:
///   the index's reverse half (owner → the keys its collection references). Those keys are references, so the
///   unfiltered count checks residency per key instead of trusting the bucket's size —
///   <see cref="IManyBucketSource{TLeftKey,TRightKey}.KeysAreReferences" />.
/// </summary>
public readonly struct JoinManyCollectionForwardBuckets<TLeftKey, TRightKey, TOwnerValue> : IManyBucketSource<TLeftKey, TRightKey>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey> {
	private readonly JoinManyCollectionBuckets<TLeftKey, TRightKey, TOwnerValue> _inner;

	internal JoinManyCollectionForwardBuckets(CacheKeyValueListIndex<TRightKey, TOwnerValue, TLeftKey> rightsIndex) => _inner = new(rightsIndex);

	static bool IManyBucketSource<TLeftKey, TRightKey>.KeysAreReferences => true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	bool IManyBucketSource<TLeftKey, TRightKey>.TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket)
		=> _inner.TryGetBucket(leftKey, out bucket);
}
