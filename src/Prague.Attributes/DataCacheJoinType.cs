// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   How many rows of each side a foreign key relates. Declared on the property that holds the target's key,
///   through <see cref="DataCacheForeignKeyAttribute{T}"/>; the generator builds the index and the
///   <c>JoinWith{T}()</c> methods from it.
/// </summary>
/// <remarks>
///   Choose by the data, not by the join you want to write: ask how many rows of <b>this</b> entity can hold the
///   same target key. One at most is <see cref="OneToOne"/>. Several is <see cref="ManyToOne"/>, whatever the
///   join is used for. A <c>Game</c> holding a <c>SportTypeId</c> is <see cref="ManyToOne"/>, because many games
///   share one sport type, even though each game joins to exactly one sport type.
/// </remarks>
public enum DataCacheJoinType {
	/// <summary>
	///   <b>At most one row of this entity holds each target key</b>: a <c>UserProfile.UserId</c> next to
	///   <c>User</c>, or a key shared by two caches that describe the same thing.
	///   <para>
	///     On the primary key, the join reads the target's key directly and builds no index. On another
	///     property it builds a symmetric <see cref="DataCacheIndexType.Unique"/> index. Both caches get
	///     <c>JoinWith{Other}()</c> with a single <c>TRight?</c> slot.
	///   </para>
	///   <para>
	///     This is a promise about the data, and the cache does not enforce it. If several rows hold the same
	///     target key, the unique index keeps one of them per key: <c>JoinWith{Holder}()</c> returns one holder,
	///     and a filtered or inner <c>JoinWith{Target}()</c> can leave holders without their target. Such data
	///     is <see cref="ManyToOne"/>.
	///   </para>
	/// </summary>
	OneToOne,

	/// <summary>
	///   Legacy name for the same relationship as <see cref="ManyToOne"/>, read from the target's side ("one
	///   target, many holders"). New code should use <see cref="ManyToOne"/>.
	///   <para>
	///     On the holder's property it builds a non-symmetric <see cref="DataCacheIndexType.Many"/> index and
	///     emits only the target's <c>JoinWith{Holder}()</c>: the holder gets no <c>JoinWith{Target}()</c>.
	///     In the dual form on the target's primary key,
	///     <c>[DataCacheForeignKey&lt;Holder&gt;(OneToMany, nameof(Holder.TargetId))]</c>, it is equivalent to
	///     <see cref="ManyToOne"/> on <c>Holder.TargetId</c>.
	///   </para>
	/// </summary>
	OneToMany,

	/// <summary>
	///   <b>Many rows of this entity can hold the same target key</b>; each row points at one target. The
	///   canonical declaration, from the holder's side. Example: <c>[DataCacheForeignKey&lt;Author&gt;(ManyToOne)]</c>
	///   on <c>Book.AuthorId</c>.
	///   <list type="bullet">
	///     <item>Builds a symmetric <see cref="DataCacheIndexType.Many"/> index on the property, so
	///       <c>With{Property}(key)</c> works as a plain filter too. An explicit <c>[DataCacheIndex]</c> next to it is
	///       a diagnostic (CACHE041 for <c>Unique</c> or <c>Range</c>, CACHE042 for a non-symmetric <c>Many</c>).</item>
	///     <item>The holder gets <c>JoinWith{Target}()</c> with a single <c>TRight?</c> slot. Every holder gets its
	///       target, however many share it. The target gets <c>JoinWith{Holder}()</c> with a
	///       <c>QueryResults&lt;THolder&gt;</c> slot.</item>
	///     <item>On a <c>List&lt;TKey&gt;</c> property, both directions get a <c>QueryResults</c> slot.</item>
	///     <item>The property type must match the target's primary key type. When the target's key is a compound
	///       key, or otherwise not the property's value, use the selector form
	///       <c>[DataCacheForeignKey&lt;T, TSelector&gt;(ManyToOne)]</c> with a <c>readonly struct</c>
	///       <c>IForeignKeySelector&lt;TFk, TPk&gt;</c> that builds the target key.</item>
	///   </list>
	/// </summary>
	ManyToOne
}
