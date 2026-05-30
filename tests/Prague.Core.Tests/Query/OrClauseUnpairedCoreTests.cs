namespace Prague.Core.Tests.Query;

using Prague.Core;
using Prague.Core.Collections;
using NUnit.Framework;

// Minimal POCO satisfying the constraints required by InMemoryDataCache<TKey, TValue>.
internal sealed class MinimalValue : ICacheEquatable<MinimalValue>, ICacheClonable<MinimalValue> {
	public int Id { get; init; }
	public int Country { get; init; }
	public int City { get; init; }

	public bool CacheEquals(MinimalValue? other) =>
		other is not null && other.Id == Id && other.Country == Country && other.City == City;
	public int CacheGetHashCode() => HashCode.Combine(Id, Country, City);
	public MinimalValue Clone() => new() { Id = Id, Country = Country, City = City };
}

[TestFixture]
public class OrClauseUnpairedCoreTests {
	private InMemoryDataCache<int, MinimalValue> _cache = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, MinimalValue>();
	}

	// ── End-to-end: .Or(b1, b2) extension on the combined builder ────────────
	//
	// Setup:
	//   Country index 12 → keys {1, 2, 3, 4}
	//   City    index  1 → keys {1, 5}
	//   City    index  2 → keys {2, 6}
	//
	// Query: UseIndex(Country, 12).Or(b => b.UseIndex(City, 1), b => b.UseIndex(City, 2))
	// Expected: {1,2,3,4} ∩ ({1,5} ∪ {2,6}) = {1, 2}

	[Test]
	public void Or_EndToEnd_NarrowsBeforeOr_IntersectsWithUnion() {
		var cache = new InMemoryDataCache<int, MinimalValue>();
		// CacheKeyValueListIndex: 1:N (many keys per index value) — required for Country and City
		// where multiple rows share the same Country or City value.
		var countryIndex = cache.CacheKeyValueListIndex<int>(static (_, v) => v.Country);
		var cityIndex    = cache.CacheKeyValueListIndex<int>(static (_, v) => v.City);

		// Country=12, city=9 → keys {3,4} won't survive the Or intersection
		cache.AddOrUpdate(1, new MinimalValue { Id = 1, Country = 12, City = 1 });
		cache.AddOrUpdate(2, new MinimalValue { Id = 2, Country = 12, City = 2 });
		cache.AddOrUpdate(3, new MinimalValue { Id = 3, Country = 12, City = 9 });
		cache.AddOrUpdate(4, new MinimalValue { Id = 4, Country = 12, City = 9 });
		// City=1/2 but wrong country → keys 5,6 not in country-12 set
		cache.AddOrUpdate(5, new MinimalValue { Id = 5, Country = 99, City = 1 });
		cache.AddOrUpdate(6, new MinimalValue { Id = 6, Country = 99, City = 2 });

		// {1,2,3,4} ∩ ({1,5} ∪ {2,6}) = {1,2}
		var result = cache.Query()
			.UseIndex(countryIndex, 12)
			.Or(
				b => b.UseIndex(cityIndex, 1),
				b => b.UseIndex(cityIndex, 2))
			.Execute();

		var keys = result.Select(x => x.Id).OrderBy(x => x).ToList();
		Assert.That(keys, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void Or_TArg_PassesStateToBothBranches() {
		var cache = new InMemoryDataCache<int, MinimalValue>();
		var countryIndex = cache.CacheKeyValueListIndex<int>(static (_, v) => v.Country);
		var cityIndex    = cache.CacheKeyValueListIndex<int>(static (_, v) => v.City);

		cache.AddOrUpdate(1, new MinimalValue { Id = 1, Country = 12, City = 1 });
		cache.AddOrUpdate(2, new MinimalValue { Id = 2, Country = 12, City = 2 });
		cache.AddOrUpdate(3, new MinimalValue { Id = 3, Country = 12, City = 9 });
		cache.AddOrUpdate(4, new MinimalValue { Id = 4, Country = 12, City = 9 });
		cache.AddOrUpdate(5, new MinimalValue { Id = 5, Country = 99, City = 1 });
		cache.AddOrUpdate(6, new MinimalValue { Id = 6, Country = 99, City = 2 });

		var state = (CityIndex: cityIndex, CityA: 1, CityB: 2);

		// {1,2,3,4} ∩ ({1,5} ∪ {2,6}) = {1,2}
		var result = cache.Query()
			.UseIndex(countryIndex, 12)
			.Or(
				static (b, s) => b.UseIndex(s.CityIndex, s.CityA),
				static (b, s) => b.UseIndex(s.CityIndex, s.CityB),
				state)
			.Execute();

		var keys = result.Select(x => x.Id).OrderBy(x => x).ToList();
		Assert.That(keys, Is.EqualTo(new[] { 1, 2 }));
	}

	// ── Edge-case: _first == true on outer (no prior UseIndex), branches seed the result ──
	//
	// Setup:
	//   City index 1 → keys {1, 5}
	//   City index 2 → keys {2, 6}
	//
	// Query: .Or(b => b.UseIndex(City, 1), b => b.UseIndex(City, 2))   [no prior UseIndex on outer]
	// _first == true → UnionWith path → result is the union: {1,5} ∪ {2,6} = {1,2,5,6}

	[Test]
	public void Or_OuterFirstTrue_SeedsFromBranchesUnion() {
		var cache = new InMemoryDataCache<int, MinimalValue>();
		var cityIndex = cache.CacheKeyValueListIndex<int>(static (_, v) => v.City);

		cache.AddOrUpdate(1, new MinimalValue { Id = 1, Country = 12, City = 1 });
		cache.AddOrUpdate(2, new MinimalValue { Id = 2, Country = 12, City = 2 });
		cache.AddOrUpdate(5, new MinimalValue { Id = 5, Country = 99, City = 1 });
		cache.AddOrUpdate(6, new MinimalValue { Id = 6, Country = 99, City = 2 });

		// No prior narrowing on outer; result is just the union: {1,5} ∪ {2,6} = {1,2,5,6}
		var result = cache.Query()
			.Or(
				b => b.UseIndex(cityIndex, 1),
				b => b.UseIndex(cityIndex, 2))
			.Execute();

		var keys = result.Select(x => x.Id).OrderBy(x => x).ToList();
		Assert.That(keys, Is.EqualTo(new[] { 1, 2, 5, 6 }));
	}

	// ── Edge-case: nested Or (3-way union via nesting) ───────────────────────────
	//
	// Setup:
	//   Country index 12 → keys {1, 2, 3, 4}
	//   City index  1    → keys {1, 5}
	//   City index  2    → keys {2, 6}
	//   City index  3    → keys {3, 7}
	//
	// Query: UseIndex(Country, 12).Or(b1=City1, b2=b2.Or(City2, City3))
	// Expected: {1,2,3,4} ∩ ({1,5} ∪ ({2,6} ∪ {3,7})) = {1,2,3,4} ∩ {1,5,2,6,3,7} = {1,2,3}

	[Test]
	public void Or_NestedOr_ThreeWayUnion() {
		var cache = new InMemoryDataCache<int, MinimalValue>();
		var countryIndex = cache.CacheKeyValueListIndex<int>(static (_, v) => v.Country);
		var cityIndex    = cache.CacheKeyValueListIndex<int>(static (_, v) => v.City);

		cache.AddOrUpdate(1, new MinimalValue { Id = 1, Country = 12, City = 1 });
		cache.AddOrUpdate(2, new MinimalValue { Id = 2, Country = 12, City = 2 });
		cache.AddOrUpdate(3, new MinimalValue { Id = 3, Country = 12, City = 3 });
		cache.AddOrUpdate(4, new MinimalValue { Id = 4, Country = 12, City = 9 });
		cache.AddOrUpdate(5, new MinimalValue { Id = 5, Country = 99, City = 1 });
		cache.AddOrUpdate(6, new MinimalValue { Id = 6, Country = 99, City = 2 });
		cache.AddOrUpdate(7, new MinimalValue { Id = 7, Country = 99, City = 3 });

		// outer {1,2,3,4} ∩ ({1,5} ∪ ({2,6} ∪ {3,7})) = {1,2,3,4} ∩ {1,2,3,5,6,7} = {1,2,3}
		var result = cache.Query()
			.UseIndex(countryIndex, 12)
			.Or(
				b => b.UseIndex(cityIndex, 1),
				b => b.Or(
					c => c.UseIndex(cityIndex, 2),
					c => c.UseIndex(cityIndex, 3)))
			.Execute();

		var keys = result.Select(x => x.Id).OrderBy(x => x).ToList();
		Assert.That(keys, Is.EqualTo(new[] { 1, 2, 3 }));
	}

	// ── Edge-case: no-op branch — one branch returns its builder unchanged ────────
	//
	// Setup:
	//   Country index 12 → keys {1, 2, 3, 4}
	//   City index  1    → keys {1, 5}
	//
	// Query: UseIndex(Country, 12).Or(b => b.UseIndex(City, 1), b => b  /* no-op */)
	// The no-op branch's Candidates stays uninitialized; OrWith treats it as ∅ in the union.
	// Expected: {1,2,3,4} ∩ ({1,5} ∪ ∅) = {1,2,3,4} ∩ {1,5} = {1}

	[Test]
	public void Or_OneBranchNoOp_OuterIntersectsWithOtherOnly() {
		var cache = new InMemoryDataCache<int, MinimalValue>();
		var countryIndex = cache.CacheKeyValueListIndex<int>(static (_, v) => v.Country);
		var cityIndex    = cache.CacheKeyValueListIndex<int>(static (_, v) => v.City);

		cache.AddOrUpdate(1, new MinimalValue { Id = 1, Country = 12, City = 1 });
		cache.AddOrUpdate(2, new MinimalValue { Id = 2, Country = 12, City = 2 });
		cache.AddOrUpdate(3, new MinimalValue { Id = 3, Country = 12, City = 9 });
		cache.AddOrUpdate(4, new MinimalValue { Id = 4, Country = 12, City = 9 });
		cache.AddOrUpdate(5, new MinimalValue { Id = 5, Country = 99, City = 1 });

		// outer {1,2,3,4} ∩ ({1,5} ∪ ∅) = {1,2,3,4} ∩ {1,5} = {1}
		var result = cache.Query()
			.UseIndex(countryIndex, 12)
			.Or(
				b => b.UseIndex(cityIndex, 1),
				b => b /* no-op — Candidates stays uninitialized */)
			.Execute();

		var keys = result.Select(x => x.Id).OrderBy(x => x).ToList();
		Assert.That(keys, Is.EqualTo(new[] { 1 }));
	}
}

#region Negative cases — must NOT compile (verify manually until an analyzer rule lands)

// To verify: uncomment the relevant snippet, run `dotnet build tests/Prague.Core.Tests`,
// confirm a compile error, then re-comment.
//
// Setup shared across all snippets:
//   cache        — InMemoryDataCache<int, MinimalValue>
//   countryIndex — CacheKeyValueListIndex<int> (1:N, keyed on MinimalValue.Country)
//   cityIndex    — CacheKeyValueListIndex<int> (1:N, keyed on MinimalValue.City)
//
// These helpers are intentionally never called — they exist solely as compile-time sentinels.

file static class OrNegativeCompileCases {
	private static (
		InMemoryDataCache<int, MinimalValue> cache,
		CacheKeyValueListIndex<int, MinimalValue, int> countryIndex,
		CacheKeyValueListIndex<int, MinimalValue, int> cityIndex
	) BuildSetup() {
		var cache = new InMemoryDataCache<int, MinimalValue>();
		var countryIndex = cache.CacheKeyValueListIndex<int>(static (_, v) => v.Country);
		var cityIndex    = cache.CacheKeyValueListIndex<int>(static (_, v) => v.City);
		return (cache, countryIndex, cityIndex);
	}

	/// <summary>
	/// Case 1: <c>Sort(...).Or(...)</c> must NOT compile.
	/// <para>
	/// <c>Sort</c> transitions the builder discriminator to <c>SortedQuery&lt;TDiscriminator&gt;</c>.
	/// <c>SortedQuery</c> implements only <c>IBaseJoinable</c> — it does NOT implement
	/// <c>IOrCapable</c>, so no <c>Or</c> extension matches and the call is unreachable.
	/// </para>
	/// Observed error: CS0411 — type arguments for Or&lt;…&gt; cannot be inferred from the usage,
	/// because no Or overload accepts a CacheQueryBuilderCombined&lt;SortedQuery&lt;…&gt;,…&gt;
	/// receiver (SortedQuery does not implement IOrCapable).
	/// </summary>
	private static void Negative_OrAfterSort() {
		// var (cache, countryIndex, cityIndex) = BuildSetup();
		// var comparer = Comparer<MinimalValue>.Create((a, b) => a.Id.CompareTo(b.Id));
		// _ = cache.Query()
		//     .UseIndex(countryIndex, 12)
		//     .Sort(comparer)
		//     .Or(b => b, b => b);
		// Verified: CS0411 — type arguments for Or<…> cannot be inferred from the usage.
		// Sort wraps the discriminator in SortedQuery<TDiscriminator>, which does not implement
		// IOrCapable; no Or overload accepts SortedQuery<…> as the receiver discriminator.
	}

	/// <summary>
	/// Case 2: <c>Where</c> inside an Or branch must NOT compile.
	/// <para>
	/// Branch builders carry <c>NarrowOnlyQuery&lt;TCache&gt;</c> as their discriminator.
	/// <c>NarrowOnlyQuery</c> deliberately does NOT implement <c>IBaseFilterable</c>,
	/// so the <c>Where</c> extension (constrained on <c>IBaseFilterable</c>) is unreachable.
	/// </para>
	/// Observed error: CS0315 — NarrowOnlyQuery&lt;…&gt; cannot be used as TDiscriminator
	/// in Where&lt;…&gt; because there is no boxing conversion from NarrowOnlyQuery&lt;…&gt;
	/// to IBaseFilterable.
	/// </summary>
	private static void Negative_WhereInBranch() {
		// var (cache, countryIndex, cityIndex) = BuildSetup();
		// _ = cache.Query()
		//     .UseIndex(countryIndex, 12)
		//     .Or(
		//         b => b.Where(v => true),   // NarrowOnlyQuery<TCache> does not implement IBaseFilterable
		//         b => b)
		//     .Execute();
		// Verified: CS0315 — NarrowOnlyQuery<…> cannot be used as TDiscriminator in Where<…>
		// because there is no boxing conversion from NarrowOnlyQuery<…> to IBaseFilterable.
		// NarrowOnlyQuery deliberately omits IBaseFilterable to block Where inside Or branches.
	}

	/// <summary>
	/// Case 3: <c>Sort</c> inside an Or branch must NOT compile.
	/// <para>
	/// Branch builders carry <c>NarrowOnlyQuery&lt;TCache&gt;</c> as their discriminator.
	/// <c>NarrowOnlyQuery</c> deliberately does NOT implement <c>ISortable</c>,
	/// so the <c>Sort</c> extension (constrained on <c>IBaseFilterable</c>) is unreachable.
	/// </para>
	/// Observed error: CS0315 — NarrowOnlyQuery&lt;…&gt; cannot be used as TDiscriminator
	/// in Sort&lt;…&gt; because there is no boxing conversion from NarrowOnlyQuery&lt;…&gt;
	/// to IBaseFilterable (Sort is gated on IBaseFilterable, not ISortable directly).
	/// </summary>
	private static void Negative_SortInBranch() {
		// var (cache, countryIndex, cityIndex) = BuildSetup();
		// var comparer = Comparer<MinimalValue>.Create((a, b) => a.Id.CompareTo(b.Id));
		// _ = cache.Query()
		//     .UseIndex(countryIndex, 12)
		//     .Or(
		//         b => b.Sort(comparer),     // NarrowOnlyQuery<TCache> does not implement ISortable
		//         b => b)                    // (Sort is constrained on IBaseFilterable, which NarrowOnlyQuery lacks)
		//     .Execute();
		// Verified: CS0315 — NarrowOnlyQuery<…> cannot be used as TDiscriminator in Sort<…>
		// because there is no boxing conversion from NarrowOnlyQuery<…> to IBaseFilterable.
		// Sort requires TDiscriminator : IBaseFilterable; NarrowOnlyQuery deliberately omits it.
	}

	/// <summary>
	/// Case 4: <c>Execute</c> inside an Or branch must NOT compile.
	/// <para>
	/// Branch builders carry <c>NarrowOnlyQuery&lt;TCache&gt;</c> as their discriminator.
	/// <c>NarrowOnlyQuery</c> deliberately does NOT implement <c>IExecutableQuery</c>,
	/// so all <c>Execute</c> / <c>ExecutePooled</c> extensions are unreachable.
	/// </para>
	/// Observed error: CS0315 — NarrowOnlyQuery&lt;…&gt; cannot be used as TDiscriminator
	/// in Execute&lt;…&gt; because there is no boxing conversion from NarrowOnlyQuery&lt;…&gt;
	/// to IExecutableQuery.
	/// </summary>
	private static void Negative_ExecuteInBranch() {
		// var (cache, countryIndex, cityIndex) = BuildSetup();
		// _ = cache.Query()
		//     .UseIndex(countryIndex, 12)
		//     .Or(
		//         b => { b.Execute(); return b; },  // NarrowOnlyQuery<TCache> does not implement IExecutableQuery
		//         b => b)
		//     .Execute();
		// Verified: CS0315 — NarrowOnlyQuery<…> cannot be used as TDiscriminator in Execute<…>
		// because there is no boxing conversion from NarrowOnlyQuery<…> to IExecutableQuery.
		// NarrowOnlyQuery deliberately omits IExecutableQuery to block Execute inside Or branches.
	}
}

#endregion
