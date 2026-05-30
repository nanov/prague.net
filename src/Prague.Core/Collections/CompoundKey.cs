namespace Prague.Core.Collections;

using System.Runtime.CompilerServices;

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
		var cmp = Prefix.CompareTo(other.Prefix);
		if (cmp != 0) return cmp;
		cmp = Sort.CompareTo(other.Sort);
		if (cmp != 0) return cmp;
		return Key.CompareTo(other.Key);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(CompoundKey<TPrefix, TSort, TKey> other) =>
		Prefix.CompareTo(other.Prefix) == 0 &&
		Sort.CompareTo(other.Sort) == 0 &&
		Key.Equals(other.Key);

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
		var cmp = Prefix1.CompareTo(other.Prefix1);
		if (cmp != 0) return cmp;
		cmp = Prefix2.CompareTo(other.Prefix2);
		if (cmp != 0) return cmp;
		cmp = Sort.CompareTo(other.Sort);
		if (cmp != 0) return cmp;
		return Key.CompareTo(other.Key);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Equals(CompoundKey<TPrefix1, TPrefix2, TSort, TKey> other) =>
		Prefix1.CompareTo(other.Prefix1) == 0 &&
		Prefix2.CompareTo(other.Prefix2) == 0 &&
		Sort.CompareTo(other.Sort) == 0 &&
		Key.Equals(other.Key);

	public override bool Equals(object? obj) =>
		obj is CompoundKey<TPrefix1, TPrefix2, TSort, TKey> other && Equals(other);

	public override int GetHashCode() => HashCode.Combine(Prefix1, Prefix2, Sort, Key);

	public override string ToString() => $"({Prefix1}, {Prefix2}, {Sort}, {Key})";
}
