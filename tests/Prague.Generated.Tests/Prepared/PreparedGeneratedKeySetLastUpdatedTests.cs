namespace Prague.Generated.Tests.Prepared;

using Prague.Core;
using Prague.Generated.Tests.Indexing;
using Prague.Generated.Tests.Models;
using NUnit.Framework;
using static PreparedParity;

// Key-set (has-value / has-not-value) and global last-updated emission of the prepared surface, eager vs
// prepared from the same inputs.
[TestFixture]
public class PreparedGeneratedKeySetLastUpdatedTests {
	private UserWithOptionalFieldsCache _users = null!;
	private ProfileWithOptionalFieldsCache _profiles = null!;
	private DataCacheRegistry _registry = null!;
	private TestProductCache _products = null!;

	private const long BaseTs = 1_700_000_000_000;

	[SetUp]
	public void SetUp() {
		_users = new UserWithOptionalFieldsCache();
		for (var i = 0; i < 60; i++)
			_users.AddOrUpdate(new UserWithOptionalFields {
				Id = i, Username = $"u{i}", Email = i % 3 == 0 ? null : $"u{i}@x", Nickname = i % 4 == 0 ? $"n{i}" : null,
				Score = i % 5 == 0 ? null : i, VerifiedAt = i % 2 == 0 ? DateTime.UnixEpoch.AddDays(i) : null,
				Status = i % 2 == 0 ? "Active" : "Inactive"
			});

		_profiles = new ProfileWithOptionalFieldsCache();
		for (var i = 0; i < 60; i++)
			_profiles.AddOrUpdate(new ProfileWithOptionalFields {
				Id = i, Name = $"p{i}", Category = i % 2 == 0 ? "X" : "Y", Phone = i % 3 == 0 ? null : "555", Avatar = i % 4 == 0 ? null : "a", Rank = i % 5 == 0 ? null : i,
				CompletedAt = i % 2 == 0 ? null : DateTime.UnixEpoch
			});

		_registry = new DataCacheRegistryBuilder().Register<TestProductCache>().Build();
		_products = _registry.GetCache<TestProductCache>();
		for (var i = 0; i < 50; i++)
			_products.AddOrUpdate(new TestProduct { ProductId = i, Name = $"p{i}", Price = i, Category = i % 2 == 0 ? "A" : "B" }, BaseTs + i * 1000);
	}

	private static string UserRow(UserWithOptionalFields u) => u.Id.ToString();
	private static string ProfileRow(ProfileWithOptionalFields p) => p.Id.ToString();
	private static string ProductRow(TestProduct p) => p.ProductId.ToString();

	// The eager wrapper emits UpdatedAfter(after) only (the eager after-until form is the Core UseIndex);
	// the window (after, until] is the difference of two eager after-only queries.
	private int[] EagerWindow(long after, long untilInclusive) {
		using var from = _products.Query().UpdatedAfter(after).Execute();
		using var beyond = _products.Query().UpdatedAfter(untilInclusive).Execute();
		var excluded = new HashSet<int>();
		for (var i = 0; i < beyond.Count; i++) excluded.Add(beyond[i].ProductId);
		var ids = new List<int>();
		for (var i = 0; i < from.Count; i++)
			if (!excluded.Contains(from[i].ProductId)) ids.Add(from[i].ProductId);
		ids.Sort();
		return ids.ToArray();
	}

	private static int[] Ids(QueryResults<TestProduct> rows) {
		using (rows) {
			var ids = new int[rows.Count];
			for (var i = 0; i < rows.Count; i++) ids[i] = rows[i].ProductId;
			Array.Sort(ids);
			return ids;
		}
	}

	[Test]
	public void HasValue_KeySet_LikeEager() {
		AssertSame(_users.Query().WithEmail().Execute(), _users.Prepare().WithEmail().Build().Execute(), UserRow);
		AssertSame(_users.Query().WithScore().WithNickname().Execute(), _users.Prepare().WithScore().WithNickname().Build().Execute(), UserRow);
		AssertSame(_users.Query().WithVerifiedAt().WithStatus("Active").Execute(), _users.Prepare().WithVerifiedAt().WithStatus("Active").Build().Execute(), UserRow);
	}

	[Test]
	public void HasValue_KeySet_WithParameterizedLane_LikeEager() {
		var prepared = _users.Prepare<string>().WithEmail().WithStatus(static s => s).Where(static u => u.Id > 10).Build();
		foreach (var status in new[] { "Active", "Inactive", "Missing" })
			AssertSame(_users.Query().WithEmail().WithStatus(status).Where(static u => u.Id > 10).Execute(), prepared.Execute(status), UserRow);
	}

	[Test]
	public void HasNotValue_KeySet_LikeEager() {
		AssertSame(_profiles.Query().WithoutPhone().Execute(), _profiles.Prepare().WithoutPhone().Build().Execute(), ProfileRow);
		AssertSame(_profiles.Query().WithoutRank().WithoutAvatar().Execute(), _profiles.Prepare().WithoutRank().WithoutAvatar().Build().Execute(), ProfileRow);
		AssertSame(_profiles.Query().WithoutCompletedAt().Execute(), _profiles.Prepare().WithoutCompletedAt().Build().Execute(), ProfileRow);
	}

	[Test]
	public void UpdatedAfter_Long_Bound_LikeEager() {
		AssertSame(_products.Query().UpdatedAfter(BaseTs + 25_500).Execute(), _products.Prepare().UpdatedAfter(BaseTs + 25_500).Build().Execute(), ProductRow);
		Assert.That(Ids(_products.Prepare().UpdatedAfter(BaseTs + 10_000, BaseTs + 20_000).Build().Execute()), Is.EqualTo(EagerWindow(BaseTs + 10_000, BaseTs + 20_000)));
		Assert.That(EagerWindow(BaseTs + 10_000, BaseTs + 20_000), Has.Length.EqualTo(10));
	}

	[Test]
	public void UpdatedAfter_DateTimeOffset_Bound_LikeEager() {
		var after = DateTimeOffset.FromUnixTimeMilliseconds(BaseTs + 30_000);
		AssertSame(_products.Query().UpdatedAfter(after).Execute(), _products.Prepare().UpdatedAfter(after).Build().Execute(), ProductRow);
		var until = DateTimeOffset.FromUnixTimeMilliseconds(BaseTs + 40_000);
		Assert.That(Ids(_products.Prepare().UpdatedAfter(after, until).Build().Execute()), Is.EqualTo(EagerWindow(after.ToUnixTimeMilliseconds(), until.ToUnixTimeMilliseconds())));
		Assert.That(Ids(_products.Prepare().UpdatedAfter(after.UtcDateTime, until.UtcDateTime).Build().Execute()), Is.EqualTo(EagerWindow(after.ToUnixTimeMilliseconds(), until.ToUnixTimeMilliseconds())));
	}

	[Test]
	public void UpdatedAfter_Parameterized_ReusedAcrossMutations_LikeEager() {
		var prepared = _products.Prepare<long>().UpdatedAfter(static since => since).Build();
		var window = _products.Prepare<(long From, long To)>().UpdatedAfter(static a => a.From, static a => a.To).Build();
		foreach (var since in new[] { BaseTs, BaseTs + 24_500, BaseTs + 48_500 }) {
			AssertSame(_products.Query().UpdatedAfter(since).Execute(), prepared.Execute(since), ProductRow);
			Assert.That(Ids(window.Execute((since, since + 10_000))), Is.EqualTo(EagerWindow(since, since + 10_000)));
		}

		_products.AddOrUpdate(new TestProduct { ProductId = 3, Name = "p3'", Price = 3, Category = "A" }, BaseTs + 100_000);
		AssertSame(_products.Query().UpdatedAfter(BaseTs + 48_500).Execute(), prepared.Execute(BaseTs + 48_500), ProductRow);
		Assert.That(prepared.Count(BaseTs + 48_500), Is.EqualTo(_products.Query().UpdatedAfter(BaseTs + 48_500).Count()));
	}
}
