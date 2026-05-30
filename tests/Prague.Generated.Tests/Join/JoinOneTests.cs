namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

[DataCache]
public partial class PkEntityA {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
}

[DataCache]
public partial class PkEntityB {
	[DataCacheKey] public int Id { get; set; }
	public string Description { get; set; } = string.Empty;

	[DataCacheIndex(DataCacheIndexType.Many)]
	public string Status { get; set; } = string.Empty;
}

[TestFixture]
public class JoinOneTests {
	private DataCacheRegistry _registry = null!;
	private PkEntityACache _aCache = null!;
	private PkEntityBCache _bCache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<PkEntityACache>()
			.Register<PkEntityBCache>()
			.Build();
		_aCache = _registry.GetCache<PkEntityACache>();
		_bCache = _registry.GetCache<PkEntityBCache>();

		// Seed: A has {1, 2, 3}; B has {1, 2} (A's id=3 has no matching B)
		// Status: B.id=1 -> "active", B.id=2 -> "inactive"
		_aCache.AddOrUpdate(new PkEntityA { Id = 1, Name = "A1" });
		_aCache.AddOrUpdate(new PkEntityA { Id = 2, Name = "A2" });
		_aCache.AddOrUpdate(new PkEntityA { Id = 3, Name = "A3" });
		_bCache.AddOrUpdate(new PkEntityB { Id = 1, Description = "B1", Status = "active" });
		_bCache.AddOrUpdate(new PkEntityB { Id = 2, Description = "B2", Status = "inactive" });
	}

	[Test]
	public void JoinOne_PkToPk_MatchExists_ReturnsRight() {
		var results = _aCache.Cache.Query().JoinOne(_bCache).Execute();
		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Description, Is.EqualTo("B1"));
		Assert.That(byId[2].Right, Is.Not.Null);
		Assert.That(byId[2].Right!.Description, Is.EqualTo("B2"));
	}

	[Test]
	public void JoinOne_PkToPk_NoMatch_ReturnsNullRight() {
		var results = _aCache.Cache.Query().JoinOne(_bCache).Execute();
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[3].Right, Is.Null);
	}

	[Test]
	public void JoinOne_WithFilter_NarrowsRightCandidates() {
		var results = _aCache.Cache
			.Query()
			.JoinOne(_bCache,
				q => q.UseIndex(_bCache.StatusIndex, "active"))
			.Execute();
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null, "A id=1 should still find B id=1 (Status=active)");
		Assert.That(byId[2].Right, Is.Null, "A id=2's B match was filtered out by Status filter");
		Assert.That(byId[3].Right, Is.Null, "A id=3 had no B match to begin with");
	}

	[Test]
	public void JoinOne_WithFilterDroppingAll_AllLeftsHaveNullRight() {
		var results = _aCache.Cache
			.Query()
			.JoinOne(_bCache,
				q => q.UseIndex(_bCache.StatusIndex, "nonexistent-status"))
			.Execute();
		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results.All(r => r.Right == null), Is.True);
	}

	[Test]
	public void JoinOne_WithFilter_UsesCodegenWithExtension() {
		// WithStatus is the codegen-emitted extension that wraps UseIndex(StatusIndex, ...).
		// It's callable when the discriminator carries the cache wrapper type —
		// which is exactly what the new interface-based JoinOne provides.
		var results = _aCache.Cache
			.Query()
			.JoinOne(_bCache,
				q => q.WithStatus("active"))
			.Execute();
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Status, Is.EqualTo("active"));
		Assert.That(byId[2].Right, Is.Null, "B id=2 has Status=inactive — filtered out");
		Assert.That(byId[3].Right, Is.Null, "A id=3 had no B match");
	}

	[Test]
	public void JoinOne_WithFilterAndArg_PassesArgToFilter() {
		// Use a static lambda + TArg to filter without closure allocation.
		var status = "active";
		var results = _aCache.Cache
			.Query()
			.JoinOne(_bCache, static (q, s) => q.WithStatus(s), status)
			.Execute();
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right, Is.Not.Null);
		Assert.That(byId[1].Right!.Status, Is.EqualTo("active"));
		Assert.That(byId[2].Right, Is.Null, "B id=2 has Status=inactive — filtered out");
		Assert.That(byId[3].Right, Is.Null, "A id=3 had no B match");
	}

	[Test]
	public void JoinOne_CombinedWithSort_OnLeft() {
		var results = _aCache.Cache
			.Query()
			.Sort(new PkEntityAIdDescComparer())
			.JoinOne(_bCache)
			.Execute();
		Assert.That(results[0].Left.Id, Is.EqualTo(3));
		Assert.That(results[2].Left.Id, Is.EqualTo(1));
	}

	[Test]
	public void JoinOne_CombinedWithUseIndex_OnLeft() {
		// Use the key index on A to restrict to {1, 2}; then join. A id=3 should not appear.
		var keys = new List<int> { 1, 2 };
		var results = _aCache.Cache
			.Query()
			.UseIndex(_aCache.Cache.KeyIndex, keys)
			.JoinOne(_bCache)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.All(r => r.Left.Id != 3), Is.True);
	}

	private class PkEntityAIdDescComparer : IComparer<PkEntityA> {
		public int Compare(PkEntityA? x, PkEntityA? y) => y!.Id.CompareTo(x!.Id);
	}
}
