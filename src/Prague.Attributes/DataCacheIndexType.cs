// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   The kind of index <see cref="DataCacheIndexAttribute"/> builds on a property. Pick by the shape of the
///   query that narrows by the property, and by how many rows can share one value.
/// </summary>
/// <remarks>
///   Every index costs memory and a write per change, so index the properties queries narrow by and filter the
///   rest with <c>.Where</c>. A foreign key is declared with <see cref="DataCacheForeignKeyAttribute{T}"/>, not with
///   this attribute: the foreign key builds its own index (see <see cref="DataCacheJoinType"/>).
/// </remarks>
public enum DataCacheIndexType {
	/// <summary>
	///   Exact match where <b>many rows can share one value</b>: <c>Status</c>, <c>CustomerId</c>, <c>SportId</c>.
	///   Each value keeps the set of row keys that hold it. The default when no type is given.
	///   <para>Query: <c>With{Property}(value)</c>; several values are OR-ed.</para>
	///   <para>
	///     <c>Symmetric = true</c> adds a reverse map (row key → value) for joins driven by this index.
	///     A <see cref="DataCacheJoinType.ManyToOne"/> foreign key gets one automatically.
	///   </para>
	/// </summary>
	Many,

	/// <summary>
	///   Exact match where <b>each value belongs to at most one row</b>: <c>InvoiceNumber</c>, <c>Email</c>.
	///   Each value maps to a single row key.
	///   <para>Query: <c>With{Property}(value)</c>.</para>
	///   <para>
	///     Uniqueness is a promise about the data, not a constraint the cache enforces. If a second row arrives
	///     with a value another row already holds, the index points the value at the newer row and the older row
	///     can no longer be found through it. Data where several rows can share a value needs
	///     <see cref="Many"/>.
	///   </para>
	/// </summary>
	Unique,

	/// <summary>
	///   Sorted index for <b>ranges and ordered walks</b>: timestamps, prices, sequence numbers.
	///   <para>Query: <c>With{Property}(q =&gt; q.Gte(a).Lt(b))</c>.</para>
	///   <para>
	///     The property type must be non-nullable and <see cref="IComparable{T}"/>; a nullable type is a
	///     generator error (CACHE002). Index a computed key with a sentinel instead, such as
	///     <c>long Key =&gt; Value ?? long.MinValue</c>.
	///   </para>
	/// </summary>
	Range
}
