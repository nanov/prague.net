namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;

// Every test builds the same query twice — once through the eager builder, once through the
// prepared builder — from the same inputs and asserts the result sequences are identical. The
// prepared path replays into the eager core, so any divergence here is a recorder bug, not an
// engine one.
[TestFixture]
public class PreparedQueryDifferentialTests {
	internal sealed class PqItem : ICacheEquatable<PqItem>, ICacheClonable<PqItem> {
		public int Id { get; init; }
		public int Code { get; init; }
		public int Group { get; init; }
		public bool Flag { get; init; }

		public bool CacheEquals(PqItem? other)
			=> other is not null && other.Id == Id && other.Code == Code && other.Group == Group && other.Flag == Flag;

		public int CacheGetHashCode() => HashCode.Combine(Id, Code, Group, Flag);

		public PqItem Clone() => new() { Id = Id, Code = Code, Group = Group, Flag = Flag };
	}

	private const int N = 240;

	private InMemoryDataCache<int, PqItem> _cache = null!;
	private CacheUniqueIndex<int, PqItem, int> _byCode = null!;
	private CacheKeyValueListIndex<int, PqItem, int> _byGroup = null!;

	[SetUp]
	public void SetUp() {
		_cache = new InMemoryDataCache<int, PqItem>();
		_byCode = _cache.AddKeyValueIndex<int>(static (_, v) => v.Code);
		_byGroup = _cache.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		for (var i = 0; i < N; i++)
			_cache.AddOrUpdate(i, Make(i));
	}

	private static PqItem Make(int i) => new() { Id = i, Code = 1000 + i, Group = i % 7, Flag = i % 3 == 0 };

	internal static void AssertSame(QueryResults<PqItem> eager, QueryResults<PqItem> prepared) {
		try {
			Assert.Multiple(() => {
				Assert.That(prepared.Count, Is.EqualTo(eager.Count), "Count");
				Assert.That(prepared.TotalCount, Is.EqualTo(eager.TotalCount), "TotalCount");
				Assert.That(prepared.Truncated, Is.EqualTo(eager.Truncated), "Truncated");
			});
			var eagerIds = new int[eager.Count];
			var preparedIds = new int[prepared.Count];
			for (var i = 0; i < eager.Count; i++) eagerIds[i] = eager[i].Id;
			for (var i = 0; i < prepared.Count; i++) preparedIds[i] = prepared[i].Id;
			Assert.That(preparedIds, Is.EqualTo(eagerIds).AsCollection, "row sequence");
		} finally {
			eager.Dispose();
			prepared.Dispose();
		}
	}

	// ── Bound values ──────────────────────────────────────────────────────────────

	[Test]
	public void NoNarrowing_ReturnsEveryRow_LikeEager() {
		var prepared = _cache.Prepare().Build();
		AssertSame(_cache.Query().Execute(), prepared.Execute());
	}

	[Test]
	public void UniqueIndex_BoundValue_LikeEager() {
		var prepared = _cache.Prepare().UseIndex(_byCode, 1042).Build();
		AssertSame(_cache.Query().UseIndex(_byCode, 1042).Execute(), prepared.Execute());
	}

	[Test]
	public void ListIndex_BoundValue_WithTwoWheres_LikeEager() {
		var prepared = _cache.Prepare()
			.UseIndex(_byGroup, 3)
			.Where(static v => v.Flag)
			.Where(static v => v.Id > 20)
			.Build();
		var eager = _cache.Query()
			.UseIndex(_byGroup, 3)
			.Where(static v => v.Flag)
			.Where(static v => v.Id > 20)
			.Execute();
		AssertSame(eager, prepared.Execute());
	}

	[Test]
	public void WhereOnly_NoIndex_LikeEager() {
		var prepared = _cache.Prepare().Where(static v => !v.Flag).Build();
		AssertSame(_cache.Query().Where(static v => !v.Flag).Execute(), prepared.Execute());
	}

	[Test]
	public void TwoIndexes_Intersect_LikeEager() {
		// Unique then list: the unique hit either survives the group intersection or does not.
		var prepared = _cache.Prepare().UseIndex(_byCode, 1014).UseIndex(_byGroup, 0).Build();
		AssertSame(_cache.Query().UseIndex(_byCode, 1014).UseIndex(_byGroup, 0).Execute(), prepared.Execute());

		var preparedMiss = _cache.Prepare().UseIndex(_byCode, 1014).UseIndex(_byGroup, 1).Build();
		AssertSame(_cache.Query().UseIndex(_byCode, 1014).UseIndex(_byGroup, 1).Execute(), preparedMiss.Execute());
	}

	[Test]
	public void NoMatch_IsEmpty_LikeEager() {
		var prepared = _cache.Prepare().UseIndex(_byCode, -1).Build();
		AssertSame(_cache.Query().UseIndex(_byCode, -1).Execute(), prepared.Execute());
	}

	[Test]
	public void SkipTake_LikeEager() {
		var prepared = _cache.Prepare().UseIndex(_byGroup, 2).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, 2).Execute(skip: 5, take: 10), prepared.Execute(skip: 5, take: 10));
	}

	[Test]
	public void ExecuteVariants_AllMatchEager() {
		var prepared = _cache.Prepare().UseIndex(_byGroup, 4).Where(static v => v.Flag).Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, 4).Where(static v => v.Flag).ExecutePooled(), prepared.ExecutePooled());
		AssertSame(_cache.Query().UseIndex(_byGroup, 4).Where(static v => v.Flag).ExecuteCloned(), prepared.ExecuteCloned());
		AssertSame(_cache.Query().UseIndex(_byGroup, 4).Where(static v => v.Flag).ExecutePooledCloned(), prepared.ExecutePooledCloned());
	}

	[Test]
	public void Count_LikeEager() {
		var prepared = _cache.Prepare().UseIndex(_byGroup, 5).Where(static v => v.Flag).Build();
		Assert.Multiple(() => {
			Assert.That(prepared.Count(), Is.EqualTo(_cache.Query().UseIndex(_byGroup, 5).Where(static v => v.Flag).Count()));
			Assert.That(_cache.Prepare().Build().Count(), Is.EqualTo(_cache.Query().Count()));
			Assert.That(_cache.Prepare().UseIndex(_byCode, -1).Build().Count(), Is.EqualTo(0));
		});
	}

	// ── Parameterized ─────────────────────────────────────────────────────────────

	[Test]
	public void UniqueIndex_Parameterized_SameCommandThreeArgs_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byCode, static code => code)
			.Build();

		foreach (var code in new[] { 1000, 1123, 1239, -5 })
			AssertSame(_cache.Query().UseIndex(_byCode, code).Execute(), prepared.Execute(code));
	}

	[Test]
	public void ListIndex_ParameterizedTuple_WithWhereAndPaging_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, (int group, int skip)>()
			.UseIndex(_byGroup, static a => a.group)
			.Where(static v => v.Id % 2 == 0)
			.Build();

		for (var group = 0; group < 7; group++) {
			var args = (group, skip: group);
			AssertSame(
				_cache.Query().UseIndex(_byGroup, group).Where(static v => v.Id % 2 == 0).Execute(skip: args.skip, take: 8),
				prepared.Execute(args, skip: args.skip, take: 8));
			Assert.That(prepared.Count(args), Is.EqualTo(_cache.Query().UseIndex(_byGroup, group).Where(static v => v.Id % 2 == 0).Count()));
		}
	}

	[Test]
	public void MixedBoundAndParameterized_LikeEager() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byGroup, static g => g)
			.UseIndex(_byCode, 1021) // 21 % 7 == 0
			.Build();
		AssertSame(_cache.Query().UseIndex(_byGroup, 0).UseIndex(_byCode, 1021).Execute(), prepared.Execute(0));
		AssertSame(_cache.Query().UseIndex(_byGroup, 3).UseIndex(_byCode, 1021).Execute(), prepared.Execute(3));
	}

	// ── Reuse ─────────────────────────────────────────────────────────────────────

	[Test]
	public void Reuse_AcrossMutations_TracksTheLiveCache() {
		var prepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Build();

		AssertSame(_cache.Query().UseIndex(_byGroup, 6).Execute(), prepared.Execute(6));

		_cache.Remove(6);
		_cache.Remove(13);
		_cache.AddOrUpdate(N + 1, new PqItem { Id = N + 1, Code = 5000, Group = 6 });
		_cache.AddOrUpdate(20, new PqItem { Id = 20, Code = 1020, Group = 1 }); // moved out of group 6

		AssertSame(_cache.Query().UseIndex(_byGroup, 6).Execute(), prepared.Execute(6));
		AssertSame(_cache.Query().UseIndex(_byGroup, 1).Execute(), prepared.Execute(1));
	}

	[Test]
	public void ThrowingSelector_Propagates_LeavesNoRentedArrays_AndCommandStaysUsable() {
		var prepared = _cache.Prepare<int, PqItem, int>()
			.UseIndex(_byGroup, 2)
			.UseIndex(_byCode, static code => code < 0 ? throw new InvalidOperationException("boom") : code)
			.Build();

		LeakAssert.Balanced(() => {
			Assert.Throws<InvalidOperationException>(() => prepared.ExecutePooled(-1).Dispose());
		});

		AssertSame(_cache.Query().UseIndex(_byGroup, 2).UseIndex(_byCode, 1009).Execute(), prepared.Execute(1009));
	}

	[Test]
	public void ConcurrentExecutions_OfOneCommand_AgainstAWriter_AreEachConsistent() {
		var prepared = _cache.Prepare<int, PqItem, int>().UseIndex(_byGroup, static g => g).Where(static v => v.Flag).Build();
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => {
			var i = 0;
			while (!stop.IsCancellationRequested) {
				var id = N + (i++ % 50);
				_cache.AddOrUpdate(id, new PqItem { Id = id, Code = 9000 + id, Group = id % 7, Flag = true });
				if (i % 3 == 0) _cache.Remove(N + ((i * 7) % 50));
			}
		});

		var readers = new Task[8];
		for (var t = 0; t < readers.Length; t++) {
			var seed = t;
			readers[t] = Task.Run(() => {
				for (var i = 0; i < 2_000; i++) {
					var group = (seed + i) % 7;
					using var rows = prepared.ExecutePooled(group);
					for (var r = 0; r < rows.Count; r++) {
						Assert.That(rows[r].Group, Is.EqualTo(group));
						Assert.That(rows[r].Flag, Is.True);
					}
				}
			});
		}

		Task.WaitAll(readers);
		stop.Cancel();
		writer.Wait();
	}
}
