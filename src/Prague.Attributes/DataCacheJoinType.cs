// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Specifies the type of join relationship for a foreign key.
/// </summary>
public enum DataCacheJoinType {
	/// <summary>
	///   One-to-one relationship. The foreign key property value is unique.
	///   Creates a unique index on the property.
	/// </summary>
	OneToOne,

	/// <summary>
	///   One-to-many relationship (legacy alias — named from the FK target's perspective).
	///   Multiple items can have the same foreign key value.
	///   Creates a non-symmetric many index on the property. Use <see cref="ManyToOne"/>
	///   for the canonical relationship declaration with auto-emitted JoinWith{Entity} sugar.
	/// </summary>
	OneToMany,

	/// <summary>
	///   Many-to-one relationship — canonical declaration from the FK holder's perspective.
	///   Many items in this entity can reference the same foreign-key target.
	///   Example: <c>[DataCacheForeignKey&lt;Author&gt;(ManyToOne)]</c> on <c>Book.AuthorId</c>.
	///   <list type="bullet">
	///     <item>Auto-emits <c>CacheSymmetricKeyValueListIndex&lt;TKey, TValue, TOtherKey&gt;</c> on the FK property
	///       (or upgrades an explicit <c>[DataCacheIndex(Many, Symmetric=false)]</c> with a diagnostic warning).</item>
	///     <item>Conflicts with explicit <c>[DataCacheIndex(Unique)]</c> or <c>[DataCacheIndex(Range)]</c> — diagnostic error.</item>
	///     <item>Emits forward <c>JoinWith{TOther}</c> (uses <c>JoinOneLeftSymResolver</c> Shape A) and reverse
	///       <c>JoinWith{This}</c> (uses <c>ManyResolver</c>) on the respective cache query builders.</item>
	///     <item>Must be on a non-PK property whose type matches the target's PK type.</item>
	///   </list>
	/// </summary>
	ManyToOne
}
