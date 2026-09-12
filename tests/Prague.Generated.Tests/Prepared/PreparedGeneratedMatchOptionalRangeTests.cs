namespace Prague.Generated.Tests.Prepared;

using Prague.Core;
using Prague.Generated.Tests.Fixtures.Entities;
using Prague.Generated.Tests.Fixtures.Enums;
using NUnit.Framework;
using static PreparedParity;

// The generated optional-bounds range twins (`WithXxx(from:, to:, fromInclusive, toInclusive)`, argument
// selectors and bound values) against the eager generated range builder, and `Match` on a generated
// cache with generated WithXxx inside the arms. Eager twins: a C# `if` per bound combination over the
// eager WithXxx range builder, and a C# `switch` over eager WithXxx reassignments.
[TestFixture]
public class PreparedGeneratedMatchOptionalRangeTests {
	public enum Pick { Department, Brand, Featured, None }

	private ProductListingCache _cache = null!;

	[SetUp]
	public void SetUp() => _cache = LoadListings();

	private static readonly (int? from, int? to)[] Bounds = [(10, 20), (10, null), (null, 20), (null, null), (45, 45), (30, 10)];

	private CacheQueryBuilderCombined<Prague.Core.TypeSystem.ExecutableQuery<ProductListingCache>, CacheQueryBuilderCoreCombined<string, ProductListing>, string, ProductListing,
			Resolvers<BaseResolver<string, ProductListing>>, ProductListing>
		EagerFeaturedOrder(int? from, int? to, bool fromInclusive, bool toInclusive) {
		var q = _cache.Query();
		if (from is null && to is null) return q;
		if (to is null) return fromInclusive ? q.WithFeaturedOrder((rb, f) => rb.Gte(f), from!.Value) : q.WithFeaturedOrder((rb, f) => rb.Gt(f), from!.Value);
		if (from is null) return toInclusive ? q.WithFeaturedOrder((rb, t) => rb.Lte(t), to.Value) : q.WithFeaturedOrder((rb, t) => rb.Lt(t), to.Value);
		var b = (from: from.Value, to: to.Value);
		return (fromInclusive, toInclusive) switch {
			(true, true) => q.WithFeaturedOrder(static (rb, b) => rb.Gte(b.from).Lte(b.to), b),
			(true, false) => q.WithFeaturedOrder(static (rb, b) => rb.Gte(b.from).Lt(b.to), b),
			(false, true) => q.WithFeaturedOrder(static (rb, b) => rb.Gt(b.from).Lte(b.to), b),
			(false, false) => q.WithFeaturedOrder(static (rb, b) => rb.Gt(b.from).Lt(b.to), b),
		};
	}

	// ── Optional range ────────────────────────────────────────────────────────────

	[TestCase(true, true)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(false, false)]
	public void OptionalRange_Parameterized_EveryBoundCombination_LikeEager(bool fromInclusive, bool toInclusive) {
		var prepared = _cache.Prepare<(int? From, int? To)>().WithFeaturedOrder(static a => a.From, static a => a.To, fromInclusive, toInclusive).Build();
		foreach (var (from, to) in Bounds) {
			AssertSame(EagerFeaturedOrder(from, to, fromInclusive, toInclusive).Execute(), prepared.Execute((from, to)));
			Assert.That(prepared.Count((from, to)), Is.EqualTo(EagerFeaturedOrder(from, to, fromInclusive, toInclusive).Count()));
		}
	}

	[Test]
	public void OptionalRange_Bound_EveryBoundCombination_LikeEager() {
		foreach (var (from, to) in Bounds) {
			var prepared = _cache.Prepare().WithFeaturedOrder(from, to, fromInclusive: false).Build();
			AssertSame(EagerFeaturedOrder(from, to, false, true).Execute(), prepared.Execute());
		}
	}

	[Test]
	public void OptionalRange_DateTimeKey_AfterMany_BeforeWhere_LikeEager() {
		var day = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var prepared = _cache.Prepare<(DateTime? From, DateTime? To)>()
			.WithIsPublished(true)
			.WithReleaseDate(static a => a.From, static a => a.To, toInclusive: false)
			.Where(static p => p.IsFeatured)
			.Build();
		foreach (var (from, to) in new (DateTime? From, DateTime? To)[] { (day.AddDays(20), day.AddDays(80)), (null, day.AddDays(80)), (day.AddDays(250), null), (null, null) }) {
			var eager = _cache.Query().WithIsPublished(true);
			if (from is not null && to is not null) eager = eager.WithReleaseDate(static (rb, b) => rb.Gte(b.from).Lt(b.to), (from: from.Value, to: to.Value));
			else if (from is not null) eager = eager.WithReleaseDate((rb, f) => rb.Gte(f), from.Value);
			else if (to is not null) eager = eager.WithReleaseDate((rb, t) => rb.Lt(t), to.Value);
			AssertSame(eager.Where(static p => p.IsFeatured).Execute(), prepared.Execute((from, to)));
		}
	}

	// ── Match ─────────────────────────────────────────────────────────────────────

	[TestCase(Pick.Department)]
	[TestCase(Pick.Brand)]
	[TestCase(Pick.Featured)]
	[TestCase(Pick.None)]
	public void Match_GeneratedWith_InArms_EveryArmAndDefault_LikeEagerSwitch(Pick pick) {
		var prepared = _cache.Prepare<(Pick Pick, long Id, int Min)>()
			.WithChannelId(SalesChannel.Web)
			.Match(static a => a.Pick, m => m
				.Case(Pick.Department, b => b.WithDepartmentId(static a => a.Id))
				.Case(Pick.Brand, b => b.WithBrandId(static a => a.Id).Where(static p => p.IsPublished))
				.Case(Pick.Featured, b => b.WithFeaturedOrder(static a => (int?)a.Min, static a => null))
				.Default(b => b.WithHasDiscount(true)))
			.Build();
		var args = (pick, Id: 3L, Min: 25);

		var eager = _cache.Query().WithChannelId(SalesChannel.Web);
		switch (pick) {
			case Pick.Department: eager = eager.WithDepartmentId(args.Id); break;
			case Pick.Brand: eager = eager.WithBrandId(args.Id).Where(static p => p.IsPublished); break;
			case Pick.Featured: eager = eager.WithFeaturedOrder((rb, min) => rb.Gte(min), args.Min); break;
			default: eager = eager.WithHasDiscount(true); break;
		}

		AssertSame(eager.Execute(), prepared.Execute(args));
	}

	[Test]
	public void Match_InsideOrBranch_AndBeforeSortBounded_GeneratedWith_LikeEager() {
		var orMatch = _cache.Prepare<(Pick Pick, long Id)>()
			.Or(b => b.WithCategoryId(static a => a.Id), b => b.Match(static a => a.Pick, m => m
				.Case(Pick.Department, c => c.WithDepartmentId(static a => a.Id))
				.Case(Pick.Brand, c => c.WithBrandId(static a => a.Id))
				.Default()))
			.SortBounded(ProductListingCache.ByDateAscComparer)
			.Build();
		foreach (var args in new[] { (Pick.Department, 2L), (Pick.Brand, 2L), (Pick.None, 2L) }) {
			var eager = _cache.Query().Or(b => b.WithCategoryId(args.Item2), b => args.Item1 switch {
				Pick.Department => b.WithDepartmentId(args.Item2),
				Pick.Brand => b.WithBrandId(args.Item2),
				_ => b,
			});
			AssertSame(eager.SortBounded(ProductListingCache.ByDateAscComparer).Execute(3, 7), orMatch.Execute(args, 3, 7));
		}
	}
}
