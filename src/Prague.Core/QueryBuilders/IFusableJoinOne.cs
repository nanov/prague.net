namespace Prague.Core;

using System.Diagnostics.CodeAnalysis;

/// <summary>
///   A <c>JoinOne</c> resolver's right lookup as one point read per left (frozen pipeline design §7.1),
///   implemented explicitly by the four <c>JoinOne</c> families. The eager resolvers build a pair set
///   over every left and run one paired bulk read; the pipeline instead calls
///   <see cref="TryLookupRight" /> for each row right after the row is added, so a join costs 1–3 hashes
///   per left and no scratch set. The lookups are the eager resolver's own reads in the same order — PK
///   to PK: the selector then the right store; right-unique: the right index then the store; left-unique:
///   the left index's reverse map, the selector, the store; left-symmetric: the reverse map, the optional
///   right index, the store — so the staleness window is the eager one.
/// </summary>
internal interface IFusableJoinOne<TLeftKey, TLeftValue, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey> {
	/// <summary>True when the resolver has no filter callback (<see cref="NoFilter{TBuilder}" />): a filter is a builder lambda over the paired core and cannot become a point probe. Read once at build.</summary>
	bool CanFuse { get; }

	/// <summary>One right lookup for one left; false when the left has no right (outer: the slot stays default; inner: the row is dropped).</summary>
	bool TryLookupRight(TLeftKey leftKey, TLeftValue leftValue, [MaybeNullWhen(false)] out TRightValue right);
}
