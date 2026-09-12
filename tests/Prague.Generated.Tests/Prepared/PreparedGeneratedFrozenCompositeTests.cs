namespace Prague.Generated.Tests.Prepared;

using Prague.Core;
using Prague.Generated.Tests.Fixtures.Entities;
using Prague.Generated.Tests.Fixtures.Enums;
using NUnit.Framework;
using static PreparedParity;
using EagerListings = Prague.Core.CacheQueryBuilderCombined<Prague.Core.TypeSystem.ExecutableQuery<Prague.Generated.Tests.Fixtures.Entities.ProductListingCache>,
	Prague.Core.CacheQueryBuilderCoreCombined<string, Prague.Generated.Tests.Fixtures.Entities.ProductListing>, string, Prague.Generated.Tests.Fixtures.Entities.ProductListing,
	Prague.Core.Resolvers<Prague.Core.BaseResolver<string, Prague.Generated.Tests.Fixtures.Entities.ProductListing>>, Prague.Generated.Tests.Fixtures.Entities.ProductListing>;

// Generated WithXxx inside prepared Or / If / IfElse / Match under BuildFrozen() (stage 3, step 4): the
// composites take the pipeline — an If / Match arm is chosen at bind, an Or probes as the OR of its
// branches' ANDs and, as the first narrowing, seeds the store walk kept to the branch union — and the
// rows are eager's byte for byte, with the same Count. No codegen change: the generated extensions
// record the same narrowers the hand-written UseIndex does.
[TestFixture]
public class PreparedGeneratedFrozenCompositeTests {
	public enum Pick { Department, Brand, Featured, None }

	// The Or-first sequence assertions opt out of the free seed (the default: the union's order) to compare
	// against the eager store-walk sequence; the default is pinned on set and Count.
	private static readonly FrozenOptions EagerOrder = new() { PreserveEagerOrder = true };

	private ProductListingCache _cache = null!;

	[SetUp]
	public void SetUp() => _cache = LoadListings();

	[Test]
	public void Or_First_AndAfterWith_Bound_GeneratedWith_FrozenIsPipeline_LikeEager() {
		var first = _cache.Prepare().Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4)).BuildFrozen(EagerOrder);
		var after = _cache.Prepare().WithChannelId(SalesChannel.Web).Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4)).BuildFrozen();
		var twoOp = _cache.Prepare().Or(b => b.WithDepartmentId(1).WithIsPublished(true), b => b.WithBrandId(2)).Where(static p => p.IsFeatured).BuildFrozen(EagerOrder);
		Assert.Multiple(() => {
			Assert.That(first.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(after.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(twoOp.Plan.Executor, Is.EqualTo("Pipeline"));
		});
		foreach (var (skip, take) in new[] { (0, int.MaxValue), (3, 7), (0, 0) }) {
			AssertSame(_cache.Query().Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4)).Execute(skip, take), first.Execute(skip, take));
			AssertSame(_cache.Query().WithChannelId(SalesChannel.Web).Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4)).ExecutePooled(skip, take), after.ExecutePooled(skip, take));
			AssertSame(_cache.Query().Or(b => b.WithDepartmentId(1).WithIsPublished(true), b => b.WithBrandId(2)).Where(static p => p.IsFeatured).ExecuteCloned(skip, take), twoOp.ExecuteCloned(skip, take));
		}

		Assert.That(first.Count(), Is.EqualTo(_cache.Query().Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4)).Count()));
		// The default: the union in branch order — same rows, same Count.
		var union = _cache.Prepare().Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4)).BuildFrozen();
		using (var eager = _cache.Query().Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4)).Execute())
		using (var rows = union.Execute())
			Assert.That(rows.Select(static p => p.CompositeId), Is.EquivalentTo(eager.Select(static p => p.CompositeId)));
		Assert.That(union.Count(), Is.EqualTo(_cache.Query().Or(b => b.WithDepartmentId(1), b => b.WithDepartmentId(4)).Count()));
		Assert.That(twoOp.Count(), Is.EqualTo(_cache.Query().Or(b => b.WithDepartmentId(1).WithIsPublished(true), b => b.WithBrandId(2)).Where(static p => p.IsFeatured).Count()));
	}

	[Test]
	public void Or_Parameterized_MixedFamilies_AndNested_FrozenIsPipeline_LikeEager() {
		var brands = new long[] { 0, 4 };
		var mixed = _cache.Prepare<(long A, long B, int Min)>()
			.WithIsPublished(true)
			.Or(b => b.WithDepartmentId(static a => a.A), b => b.WithFeaturedOrder(static (rb, a) => rb.Gte(a.Min)).WithCategoryId(static a => a.B))
			.BuildFrozen(EagerOrder);
		var multi = _cache.Prepare().Or(b => b.WithFeaturedOrder(static rb => rb.Gte(40)), b => b.WithBrandId(brands).WithIsPublished(true)).BuildFrozen(EagerOrder);
		var nested = _cache.Prepare().Or(b => b.WithDepartmentId(2), b => b.Or(c => c.WithBrandId(1), c => c.WithListingStatus(ListingStatus.Archived))).BuildFrozen(EagerOrder);
		Assert.Multiple(() => {
			Assert.That(mixed.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(multi.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(nested.Plan.Executor, Is.EqualTo("Pipeline"), "a nested Or that is its branch's only narrower flattens");
		});
		foreach (var args in new[] { (1L, 2L, 0), (5L, 10L, 30), (99L, 3L, 45) }) {
			AssertSame(
				_cache.Query().WithIsPublished(true).Or(b => b.WithDepartmentId(args.Item1), b => b.WithFeaturedOrder(static (rb, a) => rb.Gte(a), args.Item3).WithCategoryId(args.Item2)).Execute(),
				mixed.Execute(args));
			Assert.That(mixed.Count(args), Is.EqualTo(_cache.Query().WithIsPublished(true).Or(b => b.WithDepartmentId(args.Item1), b => b.WithFeaturedOrder(static (rb, a) => rb.Gte(a), args.Item3).WithCategoryId(args.Item2)).Count()));
		}

		AssertSame(_cache.Query().Or(b => b.WithFeaturedOrder(static rb => rb.Gte(40)), b => b.WithBrandId(brands).WithIsPublished(true)).Execute(), multi.Execute());
		AssertSame(_cache.Query().Or(b => b.WithDepartmentId(2), b => b.Or(c => c.WithBrandId(1), c => c.WithListingStatus(ListingStatus.Archived))).Execute(), nested.Execute());
		Assert.That(nested.Count(), Is.EqualTo(_cache.Query().Or(b => b.WithDepartmentId(2), b => b.Or(c => c.WithBrandId(1), c => c.WithListingStatus(ListingStatus.Archived))).Count()));
	}

	[Test]
	public void If_IfElse_GeneratedWith_And_Where_InBranches_FrozenIsPipeline_BothOutcomes_LikeEager() {
		var ifWhere = _cache.Prepare<(bool On, long Dept, int Min)>()
			.WithIsPublished(true)
			.If(static a => a.On, b => b.WithDepartmentId(static a => a.Dept).WithFeaturedOrder(static (rb, a) => rb.Gte(a.Min)).Where(static v => v.IsFeatured))
			.BuildFrozen();
		var ifElse = _cache.Prepare<(bool Featured, long Dept)>()
			.IfElse(static a => a.Featured, b => b.WithIsFeatured(true).WithDepartmentId(static a => a.Dept), b => b.WithHasDiscount(true))
			.BuildFrozen();
		Assert.Multiple(() => {
			Assert.That(ifWhere.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(ifElse.Plan.Executor, Is.EqualTo("Pipeline"));
		});
		foreach (var args in new[] { (true, 3L, 10), (false, 3L, 10), (true, 6L, 0) }) {
			// The eager builder is a struct that owns its candidate set: one terminal per instance.
			var (on, dept, min) = args;
			AssertSame(EagerIf(on, dept, min).Execute(), ifWhere.Execute(args));
			Assert.That(ifWhere.Count(args), Is.EqualTo(EagerIf(on, dept, min).Count()));
		}

		foreach (var args in new[] { (true, 2L), (false, 2L) }) {
			var eager = _cache.Query();
			eager = args.Item1 ? eager.WithIsFeatured(true).WithDepartmentId(args.Item2) : eager.WithHasDiscount(true);
			AssertSame(eager.ExecutePooledCloned(), ifElse.ExecutePooledCloned(args));
		}
	}

	private EagerListings EagerIf(bool on, long dept, int min) {
		var q = _cache.Query().WithIsPublished(true);
		return on ? q.WithDepartmentId(dept).WithFeaturedOrder(static (rb, a) => rb.Gte(a), min).Where(static v => v.IsFeatured) : q;
	}

	[Test]
	public void If_WithOrInside_And_OrWithIfInside_FrozenIsPipeline_LikeEager() {
		var ifOr = _cache.Prepare<(bool On, long A, long B)>()
			.If(static a => a.On, b => b.Or(c => c.WithDepartmentId(static a => a.A), c => c.WithDepartmentId(static a => a.B)))
			.BuildFrozen(EagerOrder);
		var orIf = _cache.Prepare<(bool On, long A, long B)>()
			.Or(b => b.If(static a => a.On, c => c.WithDepartmentId(static a => a.A)), b => b.WithCategoryId(static a => a.B))
			.BuildFrozen(EagerOrder);
		Assert.Multiple(() => {
			Assert.That(ifOr.Plan.Executor, Is.EqualTo("Pipeline"));
			Assert.That(orIf.Plan.Executor, Is.EqualTo("Pipeline"));
		});
		foreach (var args in new[] { (true, 1L, 5L), (false, 1L, 5L) }) {
			var eagerIfOr = _cache.Query();
			if (args.Item1) eagerIfOr = eagerIfOr.Or(c => c.WithDepartmentId(args.Item2), c => c.WithDepartmentId(args.Item3));
			AssertSame(eagerIfOr.Execute(), ifOr.Execute(args));

			var (on, a, b2) = args;
			AssertSame(_cache.Query().Or(b => on ? b.WithDepartmentId(a) : b, b => b.WithCategoryId(b2)).Execute(), orIf.Execute(args));
			Assert.That(orIf.Count(args), Is.EqualTo(_cache.Query().Or(b => on ? b.WithDepartmentId(a) : b, b => b.WithCategoryId(b2)).Count()));
		}
	}

	[TestCase(Pick.Department)]
	[TestCase(Pick.Brand)]
	[TestCase(Pick.Featured)]
	[TestCase(Pick.None)]
	public void Match_GeneratedWith_InArms_EveryArmAndDefault_FrozenIsPipeline_LikeEagerSwitch(Pick pick) {
		var frozen = _cache.Prepare<(Pick Pick, long Id, int Min)>()
			.WithChannelId(SalesChannel.Web)
			.Match(static a => a.Pick, m => m
				.Case(Pick.Department, b => b.WithDepartmentId(static a => a.Id))
				.Case(Pick.Brand, b => b.WithBrandId(static a => a.Id).Where(static p => p.IsPublished))
				.Case(Pick.Featured, b => b.WithFeaturedOrder(static a => (int?)a.Min, static a => null))
				.Default(b => b.WithHasDiscount(true)))
			.BuildFrozen();
		Assert.That(frozen.Plan.Executor, Is.EqualTo("Pipeline"));
		var args = (pick, Id: 3L, Min: 25);

		AssertSame(EagerMatch(pick, args.Id, args.Min).Execute(), frozen.Execute(args));
		AssertSame(EagerMatch(pick, args.Id, args.Min).ExecutePooled(2, 5), frozen.ExecutePooled(args, 2, 5));
		Assert.That(frozen.Count(args), Is.EqualTo(EagerMatch(pick, args.Id, args.Min).Count()));
		Assert.That(frozen.Explain(), Does.Contain("match#0").And.Contain(pick switch { Pick.Department => "arm 0", Pick.Brand => "arm 1", Pick.Featured => "arm 2", _ => "arm 3" }));
	}

	private EagerListings EagerMatch(Pick pick, long id, int min) {
		var q = _cache.Query().WithChannelId(SalesChannel.Web);
		return pick switch {
			Pick.Department => q.WithDepartmentId(id),
			Pick.Brand => q.WithBrandId(id).Where(static p => p.IsPublished),
			Pick.Featured => q.WithFeaturedOrder((rb, m) => rb.Gte(m), min),
			_ => q.WithHasDiscount(true),
		};
	}

	[Test]
	public void Match_InsideOrBranch_BeforeSortBounded_GeneratedWith_FrozenIsBoundedPipeline_LikeEager() {
		var orMatch = _cache.Prepare<(Pick Pick, long Id)>()
			.Or(b => b.WithCategoryId(static a => a.Id), b => b.Match(static a => a.Pick, m => m
				.Case(Pick.Department, c => c.WithDepartmentId(static a => a.Id))
				.Case(Pick.Brand, c => c.WithBrandId(static a => a.Id))
				.Default()))
			.SortBounded(ProductListingCache.ByDateAscComparer)
			.BuildFrozen(EagerOrder);
		Assert.That(orMatch.Plan.Executor, Is.EqualTo("Pipeline"));
		Assert.That(orMatch.Explain(), Does.Contain("sort: bounded"));
		foreach (var args in new[] { (Pick.Department, 2L), (Pick.Brand, 2L), (Pick.None, 2L) }) {
			var (pick, id) = args;
			AssertSame(EagerOrMatch(pick, id).SortBounded(ProductListingCache.ByDateAscComparer).Execute(3, 7), orMatch.Execute(args, 3, 7));
			AssertSame(EagerOrMatch(pick, id).SortBounded(ProductListingCache.ByDateAscComparer).Execute(), orMatch.Execute(args));
			Assert.That(orMatch.Count(args), Is.EqualTo(EagerOrMatch(pick, id).SortBounded(ProductListingCache.ByDateAscComparer).Count()));
		}
	}

	private EagerListings EagerOrMatch(Pick pick, long id)
		=> _cache.Query().Or(b => b.WithCategoryId(id), b => pick switch {
			Pick.Department => b.WithDepartmentId(id),
			Pick.Brand => b.WithBrandId(id),
			_ => b,
		});
}
