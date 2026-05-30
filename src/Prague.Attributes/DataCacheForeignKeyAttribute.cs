// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Marks a property as a foreign key reference to another cache entity.
///   The referenced type must have the [DataCache] attribute.
///   <para>Two declaration forms are supported:</para>
///   <list type="bullet">
///     <item>
///       <b>Direct form</b> — placed on the FK property itself. Property type must equal
///       <typeparamref name="T"/>'s PK type.
///       Example: <c>[DataCacheForeignKey&lt;Author&gt;(ManyToOne)]</c> on <c>Book.AuthorId</c>.
///     </item>
///     <item>
///       <b>Dual form</b> — placed on a property of the "anchor" side (typically the PK of the
///       referenced entity), with <paramref name="rightProperty"/> naming the FK property on
///       <typeparamref name="T"/>. Use <c>nameof(T.SomeProperty)</c> for compile-time safety.
///       Example: <c>[DataCacheForeignKey&lt;Book&gt;(OneToMany, nameof(Book.AuthorId))]</c> on <c>Author.Id</c>.
///       Produces the same indexes and JoinWith{Entity} sugar as the direct form on
///       <c>Book.AuthorId</c>.
///     </item>
///   </list>
/// </summary>
/// <typeparam name="T">
///   Direct form: the referenced cache entity. Dual form: the entity that owns the FK property.
/// </typeparam>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DataCacheForeignKeyAttribute<T> : Attribute
	where T : class {
	/// <summary>Direct form — FK lives on the property this attribute is attached to.</summary>
	/// <param name="joinType">The cardinality of the relationship.</param>
	public DataCacheForeignKeyAttribute(DataCacheJoinType joinType) {
		JoinType = joinType;
	}

	/// <summary>
	///   Dual form — declares a FK that lives on a property of <typeparamref name="T"/>.
	///   Codegen treats this identically to placing the direct form on
	///   <c><typeparamref name="T"/>.{<paramref name="rightProperty"/>}</c> with the inverted cardinality.
	/// </summary>
	/// <param name="joinType">
	///   The cardinality from the anchor side. <c>OneToMany</c> declared here is equivalent to
	///   <c>ManyToOne</c> on the FK property.
	/// </param>
	/// <param name="rightProperty">Name of the FK property on <typeparamref name="T"/>.</param>
	public DataCacheForeignKeyAttribute(DataCacheJoinType joinType, string rightProperty) {
		JoinType = joinType;
		RightProperty = rightProperty;
	}

	/// <summary>The cardinality of the relationship as seen from this declaration site.</summary>
	public DataCacheJoinType JoinType { get; }

	/// <summary>
	///   Non-null only for the dual form — the FK lives on
	///   <typeparamref name="T"/>.{<see cref="RightProperty"/>}.
	/// </summary>
	public string? RightProperty { get; }
}

/// <summary>
///   Selector-based foreign key — for cases where the source property type doesn't directly
///   match the referenced entity's PK type. <typeparamref name="TSelector"/> is a static-abstract
///   struct that converts <c>TFkProperty → TTargetPk</c> at join time.
///   <para>
///   Canonical use case: compound-PK entity whose key is a tuple, joined to another entity whose
///   PK is one component of that tuple.
///   </para>
///   <para>v1 only supports <see cref="DataCacheJoinType.OneToOne"/>. Other cardinalities yield
///   a diagnostic error (CACHE047).</para>
///   <para>v1 only supports placement on the PK property (where <c>[DataCacheKey]</c> is). Other
///   placements yield CACHE048.</para>
/// </summary>
/// <typeparam name="T">The referenced cache entity (direct form) or the FK-owner (dual form).</typeparam>
/// <typeparam name="TSelector">
///   A <c>readonly struct</c> implementing <see cref="IForeignKeySelector{TFk,TPk}"/> for the
///   appropriate source/target types. Codegen validates the type arguments line up (CACHE045/046).
/// </typeparam>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DataCacheForeignKeyAttribute<T, TSelector> : Attribute
	where T : class
	where TSelector : struct {
	// Note: TSelector is NOT constrained to IForeignKeySelector<,> at the C# level (we'd need TFk/TPk
	// type params to express that, which would balloon the user-facing surface). Codegen validates
	// that TSelector implements IForeignKeySelector<TFkPropType, TTargetPkType> (CACHE045/046).
	/// <summary>Direct form — selector applies to the property this attribute is attached to.</summary>
	public DataCacheForeignKeyAttribute(DataCacheJoinType joinType) {
		JoinType = joinType;
	}

	/// <summary>Dual form — selector applies to <typeparamref name="T"/>.{<paramref name="rightProperty"/>}.</summary>
	public DataCacheForeignKeyAttribute(DataCacheJoinType joinType, string rightProperty) {
		JoinType = joinType;
		RightProperty = rightProperty;
	}

	public DataCacheJoinType JoinType { get; }
	public string? RightProperty { get; }
}

/// <summary>
///   Static-abstract selector for foreign-key value transformation. Implementations should be
///   <c>readonly struct</c>s so the JIT devirtualizes the <see cref="Select"/> call per closed generic.
/// </summary>
/// <typeparam name="TFk">Source type — usually the FK property type.</typeparam>
/// <typeparam name="TPk">Target type — usually the referenced entity's PK type.</typeparam>
public interface IForeignKeySelector<TFk, TPk> {
	static abstract TPk Select(TFk fk);
}
