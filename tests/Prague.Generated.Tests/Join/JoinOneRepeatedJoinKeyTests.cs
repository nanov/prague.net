namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// Repro for "a join returns the right entity only for the first left when the join key repeats":
// three games share sport type 1, and joining games with sport types gave
//   1=Football; 2=NULL; 3=NULL
// Each JoinOne family is driven with several lefts pointing at ONE right:
//   RjkGame.SportTypeId     — ManyToOne FK (the correct declaration)  → JoinOneLeftSymResolver
//   RjkGameO2O.SportTypeId  — OneToOne FK on a non-PK, many-to-one data → JoinOneLeftUniqueIndexResolver
//   JoinOne(selector)       — PK-to-PK with a non-injective key selector → JoinOneResolver
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

[DataCache]
public partial class RjkGameO2O {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<RjkSportType>(DataCacheJoinType.OneToOne)]
	public int SportTypeId { get; set; }

	// A narrowing step for the frozen pipeline: without one the plan replays eager.
	[DataCacheIndex(DataCacheIndexType.Many)]
	public int Group { get; set; }
}

[TestFixture]
public class JoinOneRepeatedJoinKeyTests {
	private const string AllJoined = "1=Football; 2=Football; 3=Football; 4=Hockey";

	private DataCacheRegistry _registry = null!;
	private RjkSportTypeCache _sportTypes = null!;
	private RjkGameCache _games = null!;
	private RjkGameO2OCache _gamesO2O = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<RjkSportTypeCache>()
			.Register<RjkGameCache>()
			.Register<RjkGameO2OCache>()
			.Build();
		_sportTypes = _registry.GetCache<RjkSportTypeCache>();
		_games = _registry.GetCache<RjkGameCache>();
		_gamesO2O = _registry.GetCache<RjkGameO2OCache>();

		_sportTypes.AddOrUpdate(new RjkSportType { Id = 1, Name = "Football" });
		_sportTypes.AddOrUpdate(new RjkSportType { Id = 2, Name = "Hockey" });

		for (var g = 1; g <= 3; g++) {
			_games.AddOrUpdate(new RjkGame { Id = g, SportTypeId = 1 });
			_gamesO2O.AddOrUpdate(new RjkGameO2O { Id = g, SportTypeId = 1 });
		}
		_games.AddOrUpdate(new RjkGame { Id = 4, SportTypeId = 2 });
		_gamesO2O.AddOrUpdate(new RjkGameO2O { Id = 4, SportTypeId = 2 });
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
	private static string Describe(QueryResults<JoinResult<RjkGameO2O, RjkSportType?>> rows) => Describe(rows, static g => g.Id);

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

	// ── OneToOne FK on a non-PK holding many-to-one data (JoinOneLeftUniqueIndexResolver) ─────

	[Test]
	public void OneToOneDeclared_RepeatedFk_Eager_EveryGameGetsItsSportType() {
		using var rows = _gamesO2O.Query().JoinWithRjkSportType().Execute();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
	}

	[Test]
	public void OneToOneDeclared_RepeatedFk_InnerJoin_KeepsEveryGame() {
		using var rows = _gamesO2O.Query().InnerJoinWithRjkSportType().Execute();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
	}

	[Test]
	public void OneToOneDeclared_RepeatedFk_FilteredJoin_EveryFootballGameGetsFootball() {
		using var rows = _gamesO2O.Query().JoinWithRjkSportType(static q => q.WithName("Football")).Execute();
		Assert.That(Describe(rows), Is.EqualTo("1=Football; 2=Football; 3=Football; 4=NULL"));
	}

	[Test]
	public void OneToOneDeclared_RepeatedFk_InnerJoin_EagerRowOrder_IsTheFusedFrozenOrder() {
		// The inner phase creates the rows. The lefts sharing a right must not trail the others: the fused
		// frozen fill keeps the seed's order, and the left-unique family fuses under PreserveEagerOrder too
		// because eager matches it row for row.
		// Sport type 2 first shows up at game 20, after fifteen games that repeat sport type 1: rows created
		// per pair and then per repeat would come out 5, 20, 6, 7, … instead of in candidate order.
		for (var g = 5; g <= 40; g++)
			_gamesO2O.AddOrUpdate(new RjkGameO2O { Id = g, SportTypeId = g >= 20 && g % 2 == 0 ? 2 : 1, Group = 1 });
		for (var g = 1; g <= 4; g++)
			_gamesO2O.AddOrUpdate(new RjkGameO2O { Id = g, SportTypeId = g <= 3 ? 1 : 2, Group = 1 });

		var fused = _gamesO2O.Prepare<int>().WithGroup(static g => g).InnerJoinWithRjkSportType().BuildFrozen();
		Assert.That(fused.Explain(), Does.Contain("executor: Pipeline").And.Contain("joins: 1 (fused: 1, unfused: 0"));

		static string Sequence(QueryResults<JoinResult<RjkGameO2O, RjkSportType?>> rows) {
			var ids = new string[rows.Count];
			for (var i = 0; i < ids.Length; i++)
				ids[i] = $"{rows[i].Left.Id}:{rows[i].Right?.Name ?? "NULL"}";
			return string.Join(",", ids);
		}

		using var eager = _gamesO2O.Query().WithGroup(1).InnerJoinWithRjkSportType().Execute();
		using var fusedRows = fused.Execute(1);
		Assert.That(eager.Count, Is.EqualTo(40));
		Assert.That(Sequence(fusedRows), Is.EqualTo(Sequence(eager)), "fused frozen vs eager, row for row");
	}

	[Test]
	public void OneToOneDeclared_RepeatedFk_PreparedBuild_EveryGameGetsItsSportType() {
		using var rows = _gamesO2O.Prepare().JoinWithRjkSportType().Build().Execute();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
	}

	[Test]
	public void OneToOneDeclared_RepeatedFk_BuildFrozen_EveryGameGetsItsSportType() {
		using var rows = _gamesO2O.Prepare().JoinWithRjkSportType().BuildFrozen().Execute();
		Assert.That(Describe(rows), Is.EqualTo(AllJoined));
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
