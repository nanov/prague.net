namespace Prague.Core.Collections;

using System.Runtime.CompilerServices;

/// <summary>
///   Null-tolerant component helpers shared by every <c>CompoundKey</c> arity.
/// </summary>
internal static class CompoundKeyComponent {
	/// <summary>
	///   Null-tolerant component comparison: seek keys are built with default! halves
	///   meaning "from the very start of this prefix", so a null component sorts as
	///   negative infinity. Stored keys never carry nulls; either side of the compare
	///   may be the receiver in the tree's searches. The null checks fold away for
	///   value-typed components.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Compare<T>(T a, T b) where T : IComparable<T> {
		if (a is null) return b is null ? 0 : -1;
		if (b is null) return 1;
		return a.CompareTo(b);
	}

	/// <summary>
	///   Null-tolerant component equality — the Equals counterpart of <see cref="Compare{T}" />.
	///   Without it a null-bearing key compares equal (0) yet throws on Equals, breaking the
	///   comparer/equality agreement the tree's composite ordering relies on.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool AreEqual<T>(T a, T b) where T : IEquatable<T> {
		if (a is null) return b is null;
		return b is not null && a.Equals(b);
	}
}

/// <summary>
///   A compound key for use in a B+ tree that provides lexicographic ordering.
///   First compares by the prefix (equality/filter field), then by the sort key,
///   then by the entity key as tiebreaker.
///   This enables compound index queries like: seek to (prefix), walk in sort order, take K.
/// </summary>
public readonly struct CompoundKey<TPrefix, TSort, TKey>
	: IComparable<CompoundKey<TPrefix, TSort, TKey>>, IEquatable<CompoundKey<TPrefix, TSort, TKey>>
	where TPrefix : IComparable<TPrefix>
	where TSort : IComparable<TSort>
	where TKey : IComparable<TKey>, IEquatable<TKey> {
	public readonly TPrefix Prefix;
	public readonly TSort Sort;
	public readonly TKey Key;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public CompoundKey(TPrefix prefix, TSort sort, TKey key) {
		Prefix = prefix;
		Sort = sort;
		Key = key;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int CompareTo(CompoundKey<TPrefix, TSort, TKey> other) {
		var cmp = CompoundKeyComponent.Compare(Prefix, other.Prefix);
		if (cmp != 0) return cmp;
		cmp = CompoundKeyComponent.Compare(Sort, other.Sort);
		if (cmp != 0) return cmp;
		return CompoundKeyComponent.Compare(Key, other.Key);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(CompoundKey<TPrefix, TSort, TKey> other) =>
		CompoundKeyComponent.Compare(Prefix, other.Prefix) == 0 &&
		CompoundKeyComponent.Compare(Sort, other.Sort) == 0 &&
		CompoundKeyComponent.AreEqual(Key, other.Key);

	public override bool Equals(object? obj) =>
		obj is CompoundKey<TPrefix, TSort, TKey> other && Equals(other);

	public override int GetHashCode() => HashCode.Combine(Prefix, Sort, Key);

	public override string ToString() => $"({Prefix}, {Sort}, {Key})";
}

/// <summary>
///   A compound key with two prefix fields for multi-field equality filters.
///   Ordering: Prefix1, Prefix2, Sort, Key.
/// </summary>
public readonly struct CompoundKey<TPrefix1, TPrefix2, TSort, TKey>
	: IComparable<CompoundKey<TPrefix1, TPrefix2, TSort, TKey>>,
		IEquatable<CompoundKey<TPrefix1, TPrefix2, TSort, TKey>>
	where TPrefix1 : IComparable<TPrefix1>
	where TPrefix2 : IComparable<TPrefix2>
	where TSort : IComparable<TSort>
	where TKey : IComparable<TKey>, IEquatable<TKey> {
	public readonly TPrefix1 Prefix1;
	public readonly TPrefix2 Prefix2;
	public readonly TSort Sort;
	public readonly TKey Key;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public CompoundKey(TPrefix1 prefix1, TPrefix2 prefix2, TSort sort, TKey key) {
		Prefix1 = prefix1;
		Prefix2 = prefix2;
		Sort = sort;
		Key = key;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int CompareTo(CompoundKey<TPrefix1, TPrefix2, TSort, TKey> other) {
		var cmp = CompoundKeyComponent.Compare(Prefix1, other.Prefix1);
		if (cmp != 0) return cmp;
		cmp = CompoundKeyComponent.Compare(Prefix2, other.Prefix2);
		if (cmp != 0) return cmp;
		cmp = CompoundKeyComponent.Compare(Sort, other.Sort);
		if (cmp != 0) return cmp;
		return CompoundKeyComponent.Compare(Key, other.Key);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(CompoundKey<TPrefix1, TPrefix2, TSort, TKey> other) =>
		CompoundKeyComponent.Compare(Prefix1, other.Prefix1) == 0 &&
		CompoundKeyComponent.Compare(Prefix2, other.Prefix2) == 0 &&
		CompoundKeyComponent.Compare(Sort, other.Sort) == 0 &&
		CompoundKeyComponent.AreEqual(Key, other.Key);

	public override bool Equals(object? obj) =>
		obj is CompoundKey<TPrefix1, TPrefix2, TSort, TKey> other && Equals(other);

	public override int GetHashCode() => HashCode.Combine(Prefix1, Prefix2, Sort, Key);

	public override string ToString() => $"({Prefix1}, {Prefix2}, {Sort}, {Key})";
}
