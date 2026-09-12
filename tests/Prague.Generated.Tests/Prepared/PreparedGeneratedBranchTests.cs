namespace Prague.Generated.Tests.Prepared;

using Prague.Core;
using Prague.Generated.Tests.Fixtures.Entities;
using Prague.Generated.Tests.Fixtures.Enums;
using NUnit.Framework;
using static PreparedParity;

// Generated WithXxx inside prepared Or and If / IfElse branches. The branch discriminators
// (PreparedNarrowOnly / PreparedConditionalBranch) carry the enclosing builder's wrapper as their
// ICacheCarrier, which is what the emitted WithXxx constraints bind on, so every emitted overload family
// is callable inside a branch: bound, argument selector, multi-value, range. Eager twins: the eager Or
// with the same branch lambdas, and a C# `if` around a type-preserving eager reassignment.
[TestFixture]
public class PreparedGeneratedBranchTests {
	private ProductListingCache _cache = null!;

	[SetUp]
	public void SetUp() => _cache = LoadListings();

	// ── Or ────────────────────────────────────────────────────────────────────────

	[Test]
	public void Or_Bound_GeneratedWith_InBranches_LikeEager() {
		var prepared = _cache.Prepare()
			.WithChannelId(SalesChannel.Web)
			.Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4))
			.Build();
		AssertSame(_cache.Query().WithChannelId(SalesChannel.Web).Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4)).Execute(), prepared.Execute());
	}

	[Test]
	public void Or_AsFirstNarrowing_MixedFamilies_InBranches_LikeEager() {
		var brands = new long[] { 0, 4 };
		var prepared = _cache.Prepare()
			.Or(b => b.WithFeaturedOrder(static rb => rb.Gte(40)), b => b.WithBrandId(brands).WithIsPublished(true))
			.Build();
		AssertSame(_cache.Query().Or(b => b.WithFeaturedOrder(static rb => rb.Gte(40)), b => b.WithBrandId(brands).WithIsPublished(true)).Execute(), prepared.Execute());
	}

	[Test]
	public void Or_Parameterized_GeneratedWith_InBranches_LikeEager() {
		var prepared = _cache.Prepare<(long A, long B, int Min)>()
			.WithIsPublished(true)
			.Or(b => b.WithDepartmentId(static a => a.A), b => b.WithCategoryId(static a => a.B).WithFeaturedOrder(static (rb, a) => rb.Gte(a.Min)))
			.Build();
		foreach (var args in new[] { (1L, 2L, 0), (5L, 10L, 30), (99L, 3L, 45) })
			AssertSame(
				_cache.Query().WithIsPublished(true).Or(b => b.WithDepartmentId(args.Item1), b => b.WithCategoryId(args.Item2).WithFeaturedOrder(static (rb, a) => rb.Gte(a), args.Item3)).Execute(),
				prepared.Execute(args));
	}

	[Test]
	public void Or_Nested_GeneratedWith_LikeEager() {
		var prepared = _cache.Prepare()
			.Or(b => b.WithDepartmentId(2), b => b.Or(c => c.WithBrandId(1), c => c.WithListingStatus(ListingStatus.Archived)))
			.Build();
		AssertSame(_cache.Query().Or(b => b.WithDepartmentId(2), b => b.Or(c => c.WithBrandId(1), c => c.WithListingStatus(ListingStatus.Archived))).Execute(), prepared.Execute());
	}

	// ── If ────────────────────────────────────────────────────────────────────────

	[Test]
	public void If_Bound_GeneratedWith_InBranch_LikeEager() {
		var prepared = _cache.Prepare<bool>().WithChannelId(SalesChannel.Web).If(static on => on, b => b.WithIsFeatured(true)).Build();
		foreach (var on in new[] { true, false }) {
			var eager = _cache.Query().WithChannelId(SalesChannel.Web);
			if (on) eager = eager.WithIsFeatured(true);
			AssertSame(eager.Execute(), prepared.Execute(on));
		}
	}

	[Test]
	public void If_Parameterized_GeneratedWith_And_Where_InBranch_LikeEager() {
		var prepared = _cache.Prepare<(bool On, long Dept, int Min)>()
			.WithIsPublished(true)
			.If(static a => a.On, b => b.WithDepartmentId(static a => a.Dept).WithFeaturedOrder(static (rb, a) => rb.Gte(a.Min)).Where(static v => v.IsFeatured))
			.Build();
		foreach (var args in new[] { (true, 3L, 10), (false, 3L, 10), (true, 6L, 0) }) {
			var eager = _cache.Query().WithIsPublished(true);
			if (args.Item1) eager = eager.WithDepartmentId(args.Item2).WithFeaturedOrder(static (rb, a) => rb.Gte(a), args.Item3).Where(static v => v.IsFeatured);
			AssertSame(eager.Execute(), prepared.Execute(args));
		}
	}

	[Test]
	public void IfElse_GeneratedWith_OnBothBranches_LikeEager() {
		var prepared = _cache.Prepare<(bool Featured, long Dept)>()
			.IfElse(static a => a.Featured, b => b.WithIsFeatured(true).WithDepartmentId(static a => a.Dept), b => b.WithHasDiscount(true))
			.Build();
		foreach (var args in new[] { (true, 2L), (false, 2L) }) {
			var eager = _cache.Query();
			eager = args.Item1 ? eager.WithIsFeatured(true).WithDepartmentId(args.Item2) : eager.WithHasDiscount(true);
			AssertSame(eager.Execute(), prepared.Execute(args));
		}
	}

	[Test]
	public void If_WithOrInside_And_OrWithIfInside_GeneratedWith_LikeEager() {
		var ifOr = _cache.Prepare<(bool On, long A, long B)>()
			.If(static a => a.On, b => b.Or(c => c.WithDepartmentId(static a => a.A), c => c.WithDepartmentId(static a => a.B)))
			.Build();
		var orIf = _cache.Prepare<(bool On, long A, long B)>()
			.Or(b => b.If(static a => a.On, c => c.WithDepartmentId(static a => a.A)), b => b.WithCategoryId(static a => a.B))
			.Build();
		foreach (var args in new[] { (true, 1L, 5L), (false, 1L, 5L) }) {
			var eagerIfOr = _cache.Query();
			if (args.Item1) eagerIfOr = eagerIfOr.Or(c => c.WithDepartmentId(args.Item2), c => c.WithDepartmentId(args.Item3));
			AssertSame(eagerIfOr.Execute(), ifOr.Execute(args));

			var eagerOrIf = _cache.Query().Or(b => args.Item1 ? b.WithDepartmentId(args.Item2) : b, b => b.WithCategoryId(args.Item3));
			AssertSame(eagerOrIf.Execute(), orIf.Execute(args));
		}
	}

	[Test]
	public void Branches_ThenSortBounded_Pages_LikeEager() {
		var prepared = _cache.Prepare<long>()
			.Or(b => b.WithDepartmentId(static d => d), b => b.WithBrandId(static d => d))
			.SortBounded(ProductListingCache.ByDateAscComparer)
			.Build();
		foreach (var d in new long[] { 1, 4 })
			AssertSame(_cache.Query().Or(b => b.WithDepartmentId(d), b => b.WithBrandId(d)).SortBounded(ProductListingCache.ByDateAscComparer).Execute(3, 7), prepared.Execute(d, 3, 7));
	}
}
