namespace Prague.Generated.Tests.Prepared;

using Prague.Core;
using Prague.Generated.Tests.Fixtures.Entities;
using Prague.Generated.Tests.Fixtures.Enums;
using NUnit.Framework;
using static PreparedParity;

// The generated prepared surface against the generated eager one, from the same inputs: every emitted
// WithXxx overload family (bound, argument selector, multi-value, range bound and parameterized, key
// property), constant and parameterized Where, Sort / SortBounded pages, Count, and reuse of one built
// command across arguments and cache mutations. The prepared WithXxx forwards to the hand-written
// prepared UseIndex over the same wrapper index field the eager WithXxx reads, so any divergence here is
// a codegen bug, not an engine one.
[TestFixture]
public class PreparedGeneratedDifferentialTests {
	private ProductListingCache _cache = null!;

	private readonly struct ByFeaturedOrderThenId : IComparer<ProductListing> {
		public int Compare(ProductListing? x, ProductListing? y) {
			var c = (x?.FeaturedOrder ?? 0).CompareTo(y?.FeaturedOrder ?? 0);
			return c != 0 ? c : (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
		}
	}

	[SetUp]
	public void SetUp() => _cache = LoadListings();

	// ── Unique ────────────────────────────────────────────────────────────────────

	[Test]
	public void Unique_Bound_LikeEager() {
		var prepared = _cache.Prepare().WithId(42).Build();
		AssertSame(_cache.Query().WithId(42).Execute(), prepared.Execute());
		AssertSame(_cache.Query().WithId(-1).Execute(), _cache.Prepare().WithId(-1).Build().Execute());
	}

	[Test]
	public void Unique_Parameterized_ReusedWithThreeArgs_LikeEager() {
		var prepared = _cache.Prepare<long>().WithId(static id => id).Build();
		foreach (var id in new long[] { 7, 299, 5000 })
			AssertSame(_cache.Query().WithId(id).Execute(), prepared.Execute(id));
	}

	[Test]
	public void KeyProperty_Bound_LikeEager() {
		var prepared = _cache.Prepare().WithCompositeId("P7_2").Build();
		AssertSame(_cache.Query().WithCompositeId("P7_2").Execute(), prepared.Execute());
		var parameterized = _cache.Prepare<TextArg>().WithCompositeId(static a => a.Text).Build();
		AssertSame(_cache.Query().WithCompositeId("P10_2").Execute(), parameterized.Execute(new TextArg("P10_2")));
	}

	// ── Many ──────────────────────────────────────────────────────────────────────

	[Test]
	public void Many_Bound_TwoLanes_LikeEager() {
		var prepared = _cache.Prepare().WithDepartmentId(3).WithChannelId(SalesChannel.Store).Build();
		AssertSame(_cache.Query().WithDepartmentId(3).WithChannelId(SalesChannel.Store).Execute(), prepared.Execute());
	}

	[Test]
	public void Many_Parameterized_TupleArgs_LikeEager() {
		var prepared = _cache.Prepare<(long Dept, SalesChannel Channel)>()
			.WithDepartmentId(static a => a.Dept)
			.WithChannelId(static a => a.Channel)
			.Build();
		foreach (var (dept, channel) in new[] { (1L, SalesChannel.Web), (4L, SalesChannel.Kiosk), (6L, SalesChannel.Mobile) })
			AssertSame(_cache.Query().WithDepartmentId(dept).WithChannelId(channel).Execute(), prepared.Execute((dept, channel)));
	}

	// ── Multi-value ───────────────────────────────────────────────────────────────

	[Test]
	public void MultiValue_Array_And_Memory_LikeEager() {
		var values = new long[] { 1, 3, 5 };
		AssertSame(_cache.Query().WithDepartmentId(values).Execute(), _cache.Prepare().WithDepartmentId(values).Build().Execute());
		AssertSame(_cache.Query().WithDepartmentId(values).Execute(), _cache.Prepare().WithDepartmentId(values.AsMemory()).Build().Execute());
		AssertSame(_cache.Query().WithDepartmentId(Array.Empty<long>()).Execute(), _cache.Prepare().WithDepartmentId(Array.Empty<long>()).Build().Execute());
	}

	[Test]
	public void MultiValue_Unique_Array_LikeEager() {
		var ids = new long[] { 2, 4, 8, 16, 9999 };
		AssertSame(_cache.Query().WithId(ids).Execute(), _cache.Prepare().WithId(ids).Build().Execute());
	}

	[Test]
	public void MultiValue_Parameterized_Memory_LikeEager() {
		var prepared = _cache.Prepare<ValuesArg>().WithDepartmentId(static a => a.Values.AsMemory()).Build();
		foreach (var set in new[] { new long[] { 0 }, new long[] { 2, 6 }, new long[] { 1, 2, 3, 4, 5, 6 } })
			AssertSame(_cache.Query().WithDepartmentId(set).Execute(), prepared.Execute(new ValuesArg(set)));
	}

	// ── Range ─────────────────────────────────────────────────────────────────────

	[Test]
	public void Range_Bound_LikeEager() {
		var prepared = _cache.Prepare().WithFeaturedOrder(static rb => rb.Gte(10).Lt(20)).Build();
		AssertSame(_cache.Query().WithFeaturedOrder(static rb => rb.Gte(10).Lt(20)).Execute(), prepared.Execute());

		var from = new DateTime(2020, 3, 1, 0, 0, 0, DateTimeKind.Utc);
		var dates = _cache.Prepare().WithReleaseDate(rb => rb.Gt(from)).Build();
		AssertSame(_cache.Query().WithReleaseDate(rb => rb.Gt(from)).Execute(), dates.Execute());
	}

	[Test]
	public void Range_Parameterized_LikeEager() {
		var prepared = _cache.Prepare<(int Lo, int Hi)>().WithFeaturedOrder(static (rb, a) => rb.Gte(a.Lo).Lt(a.Hi)).Build();
		foreach (var bounds in new (int Lo, int Hi)[] { (0, 5), (10, 40), (45, 100) })
			AssertSame(_cache.Query().WithFeaturedOrder(static (rb, a) => rb.Gte(a.Lo).Lt(a.Hi), bounds).Execute(), prepared.Execute(bounds));
	}

	[Test]
	public void Range_And_Many_Intersect_LikeEager() {
		var prepared = _cache.Prepare<int>().WithIsFeatured(true).WithActiveVariantCount(static (rb, min) => rb.Gte(min)).Build();
		AssertSame(_cache.Query().WithIsFeatured(true).WithActiveVariantCount(static (rb, min) => rb.Gte(min), 12).Execute(), prepared.Execute(12));
	}

	// ── Where ─────────────────────────────────────────────────────────────────────

	[Test]
	public void Where_Constant_LikeEager() {
		var prepared = _cache.Prepare().WithDepartmentId(2).Where(static v => v.IsFeatured).Where(static v => v.IsPublished).Build();
		AssertSame(_cache.Query().WithDepartmentId(2).Where(static v => v.IsFeatured).Where(static v => v.IsPublished).Execute(), prepared.Execute());
	}

	[Test]
	public void Where_Parameterized_LikeEager() {
		var prepared = _cache.Prepare<int>().WithDepartmentId(2).Where(static (v, in min) => v.FeaturedOrder > min).Build();
		foreach (var min in new[] { 0, 25, 49 })
			AssertSame(_cache.Query().WithDepartmentId(2).Where(v => v.FeaturedOrder > min).Execute(), prepared.Execute(min));
	}

	// ── Sort ──────────────────────────────────────────────────────────────────────

	[Test]
	public void Sort_Pages_LikeEager() {
		var cmp = new ByFeaturedOrderThenId();
		var prepared = _cache.Prepare().WithChannelId(SalesChannel.Web).Sort(cmp).Build();
		AssertSame(_cache.Query().WithChannelId(SalesChannel.Web).Sort(cmp).Execute(), prepared.Execute());
		AssertSame(_cache.Query().WithChannelId(SalesChannel.Web).Sort(cmp).Execute(5, 10), prepared.Execute(skip: 5, take: 10));
	}

	[Test]
	public void SortBounded_Pages_Parameterized_LikeEager() {
		var cmp = new ByFeaturedOrderThenId();
		var prepared = _cache.Prepare<SalesChannel>().WithChannelId(static c => c).SortBounded(cmp).Build();
		foreach (var channel in new[] { SalesChannel.Web, SalesChannel.Store }) {
			AssertSame(_cache.Query().WithChannelId(channel).SortBounded(cmp).Execute(0, 10), prepared.Execute(channel, 0, 10));
			AssertSame(_cache.Query().WithChannelId(channel).SortBounded(cmp).Execute(10, 10), prepared.Execute(channel, 10, 10));
			AssertSame(_cache.Query().WithChannelId(channel).SortBounded(cmp).Execute(70, 10), prepared.Execute(channel, 70, 10));
		}
	}

	// ── Count ─────────────────────────────────────────────────────────────────────

	[Test]
	public void Count_LikeEager() {
		var prepared = _cache.Prepare<long>().WithDepartmentId(static d => d).Where(static v => v.HasDiscount).Build();
		foreach (var dept in new long[] { 0, 3, 6, 99 })
			Assert.That(prepared.Count(dept), Is.EqualTo(_cache.Query().WithDepartmentId(dept).Where(static v => v.HasDiscount).Count()));
		Assert.That(_cache.Prepare().Build().Count(), Is.EqualTo(_cache.Query().Count()));
	}

	// ── Reuse across mutations ────────────────────────────────────────────────────

	[Test]
	public void Reuse_AcrossMutations_TracksTheCache_LikeEager() {
		var prepared = _cache.Prepare<long>().WithDepartmentId(static d => d).Build();
		AssertSame(_cache.Query().WithDepartmentId(3).Execute(), prepared.Execute(3));

		_cache.AddOrUpdate(MakeListing(ListingCount + 3)); // new row in department 3
		AssertSame(_cache.Query().WithDepartmentId(3).Execute(), prepared.Execute(3));

		var moved = MakeListing(10); // department 3 → 5
		moved.DepartmentId = 5;
		_cache.AddOrUpdate(moved);
		AssertSame(_cache.Query().WithDepartmentId(3).Execute(), prepared.Execute(3));
		AssertSame(_cache.Query().WithDepartmentId(5).Execute(), prepared.Execute(5));

		_cache.Remove(MakeListing(17).CompositeId);
		AssertSame(_cache.Query().WithDepartmentId(3).Execute(), prepared.Execute(3));
	}
}
