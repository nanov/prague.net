namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// Repro for "a join returns the right entity only for the first left when the join key repeats":
// three games share sport type 1, and joining games with sport types gave
//   1=Football; 2=NULL; 3=NULL
// Each JoinOne family is driven with several lefts pointing at ONE right:
//   RjkGame.SportTypeId     — ManyToOne FK (the correct declaration)  → JoinOneLeftSymResolver
//   JoinOne(selector)       — PK-to-PK with a non-injective key selector → JoinOneResolver
// A OneToOne FK over this data is a declaration error, not a case to support: OneToOne promises one
// left per right (the left index's forward map keeps one game per sport type). Many lefts on one right
// is ManyToOne.
// Game 4 (Hockey) is the control: a right key used by exactly one left.

[DataCache]
public partial class RjkSportType {
	[DataCacheKey] public int Id { get; set; }
	[DataCacheIndex(DataCacheIndexType.Many)] public string Name { get; set; } = string.Empty;
}

[DataCache]
public partial class RjkGame {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<RjkSportType>(DataCacheJoinType.ManyToOne)]
	public int SportTypeId { get; set; }
}

[TestFixture]
public class JoinOneRepeatedJoinKeyTests {
	private const string AllJoined = "1=Football; 2=Football; 3=Football; 4=Hockey";

	private DataCacheRegistry _registry = null!;
	private RjkSportTypeCache _sportTypes = null!;
	private RjkGameCache _games = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<RjkSportTypeCache>()
			.Register<RjkGameCache>()
			.Build();
		_sportTypes = _registry.GetCache<RjkSportTypeCache>();
		_games = _registry.GetCache<RjkGameCache>();

		_sportTypes.AddOrUpdate(new RjkSportType { Id = 1, Name = "Football" });
		_sportTypes.AddOrUpdate(new RjkSportType { Id = 2, Name = "Hockey" });

		for (var g = 1; g <= 3; g++) {
			_games.AddOrUpdate(new RjkGame { Id = g, SportTypeId = 1 });
		}
		_games.AddOrUpdate(new RjkGame { Id = 4, SportTypeId = 2 });
	}

	// "1=Football; 2=NULL; 3=NULL" — the report's own format, rows ordered by game id.
	private static string Describe<TLeft>(QueryResults<JoinResult<TLeft, RjkSportType?>> rows, Func<TLeft, int> id) {
		var lines = new (int Id, string Right)[rows.Count];
		for (var i = 0; i < rows.Count; i++)
			lines[i] = (id(rows[i].Left), rows[i].Right?.Name ?? "NULL");
		Array.Sort(lines, static (a, b) => a.Id.CompareTo(b.Id));
		return string.Join("; ", lines.Select(static l => $"{l.Id}={l.Right}"));
	}

	private static string Describe(QueryResults<JoinResult<RjkGame, RjkSportType?>> rows) => Describe(rows, static g => g.Id);

	private readonly struct GameByIdDesc : IComparer<RjkGame> {
		public int Compare(RjkGame? x, RjkGame? y) => (y?.Id ?? 0).CompareTo(x?.Id ?? 0);
	}

	// ── ManyToOne FK (JoinOneLeftSymResolver) ─────────────────────────────────

	[Test]
	public void ManyToOne_Eager_Execute_EveryGameGetsItsSportType() {
		using var rows = _games.Query().JoinWithRjkSportType().Execute();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
	}

	[Test]
	public void ManyToOne_Eager_ExecutePooled_EveryGameGetsItsSportType() {
		using var rows = _games.Query().JoinWithRjkSportType().ExecutePooled();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
	}

	[Test]
	public void ManyToOne_Eager_InnerJoin_KeepsEveryGame() {
		using var rows = _games.Query().InnerJoinWithRjkSportType().Execute();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
	}

	[Test]
	public void ManyToOne_Eager_FilteredJoin_EveryFootballGameGetsFootball() {
		using var rows = _games.Query().JoinWithRjkSportType(static q => q.WithName("Football")).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=Football; 2=Football; 3=Football; 4=NULL"));
	}

	[Test]
	public void ManyToOne_Eager_AfterWithSportTypeId_EveryGameGetsItsSportType() {
		using var rows = _games.Query().WithSportTypeId(1).JoinWithRjkSportType().Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=Football; 2=Football; 3=Football"));
	}

	[Test]
	public void ManyToOne_Eager_SortThenJoin_EveryGameGetsItsSportType() {
		using var rows = _games.Query().Sort(new GameByIdDesc()).JoinWithRjkSportType().Execute();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
	}

	[Test]
	public void ManyToOne_Prepared_Build_EveryGameGetsItsSportType() {
		var prepared = _games.Prepare<int>().WithSportTypeId(static s => s).JoinWithRjkSportType().Build();
		using var rows = prepared.Execute(1);
		Assert.That(Describe(rows), Is.EqualTo("1=Football; 2=Football; 3=Football"));
	}

	[Test]
	public void ManyToOne_Prepared_BuildFrozen_EveryGameGetsItsSportType() {
		var frozen = _games.Prepare<int>().WithSportTypeId(static s => s).JoinWithRjkSportType().BuildFrozen();
		using var rows = frozen.Execute(1);
		Assert.That(Describe(rows), Is.EqualTo("1=Football; 2=Football; 3=Football"));
	}

	[Test]
	public void ManyToOne_SortBoundedPage_EveryGameGetsItsSportType() {
		// The BrandDataReader shape: a bounded page, then the join over the page rows only.
		using var page = _games.Query().SortBounded(new GameByIdDesc()).JoinWithRjkSportType().Execute(1, 2);
		Assert.That(Describe(page), Is.EqualTo("2=Football; 3=Football"));
		Assert.That(page.TotalCount, Is.EqualTo(4));
	}

	[Test]
	public void ManyToOne_AfterUpserts_EveryGameGetsItsSportType() {
		// Kafka-shaped history: a game re-delivered unchanged, and one moved onto the shared sport type.
		_games.AddOrUpdate(new RjkGame { Id = 2, SportTypeId = 1 });
		_games.AddOrUpdate(new RjkGame { Id = 5, SportTypeId = 2 });
		_games.AddOrUpdate(new RjkGame { Id = 5, SportTypeId = 1 });

		using var rows = _games.Query().JoinWithRjkSportType().Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=Football; 2=Football; 3=Football; 4=Hockey; 5=Football"));
	}

	// ── PK-to-PK JoinOne with a non-injective key selector (JoinOneResolver) ───

	[Test]
	public void JoinOne_NonInjectiveSelector_EveryGameGetsItsSportType() {
		// Games 1..3 → sport type 1, game 4 → sport type 2: several left keys select one right key.
		using var rows = _games.Query().JoinOne(static (int gameId) => gameId <= 3 ? 1 : 2, _sportTypes).Execute();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
	}

	[Test]
	public void InnerJoinOne_NonInjectiveSelector_KeepsEveryGame() {
		using var rows = _games.Query().InnerJoinOne(static (int gameId) => gameId <= 3 ? 1 : 2, _sportTypes).Execute();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
	}

	[Test]
	public void SelectorFkOnCompoundPk_SharedComponent_EveryCarGetsItsData() {
		// The generated form of the same resolver: Car's (Id, RegionId) PK joins CarData by Id alone
		// (SelectorForeignKeyTests' own model), so one car in three regions is three lefts on one right.
		var registry = new DataCacheRegistryBuilder().Register<CarCache>().Register<CarDataCache>().Build();
		var cars = registry.GetCache<CarCache>();
		var data = registry.GetCache<CarDataCache>();
		data.AddOrUpdate(new CarData { CarId = 1, Horsepower = 200 });
		foreach (var region in new[] { 100, 200, 300 })
			cars.AddOrUpdate(new Car { Id = 1, RegionId = region, Make = "Toyota" });

		using var rows = cars.Query().JoinWithCarData().Execute();
		var lines = new string[rows.Count];
		for (var i = 0; i < rows.Count; i++)
			lines[i] = $"{rows[i].Left.RegionId}={rows[i].Right?.Horsepower.ToString() ?? "NULL"}";
		Array.Sort(lines, StringComparer.Ordinal);
		Assert.That(string.Join("; ", lines), Is.EqualTo("100=200; 200=200; 300=200"));
	}
}
