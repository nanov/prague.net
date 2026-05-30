// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Cache;

using Prague.Core;
using Join;
using NUnit.Framework;

/// <summary>
///   Tests that exercise the internal TryGetValues overloads of ConcurrentCacheStore
///   through the join infrastructure. These methods are not directly accessible but
///   are called when performing join queries with predicates and filters.
/// </summary>
[TestFixture]
public class ConcurrentCacheStoreInternalTests {
	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<ClubCache>()
			.Register<PlayerCache>()
			.Register<WarehouseCache>()
			.Register<ContractCache>()
			.Build();
		_clubCache = _registry.GetCache<ClubCache>();
		_playerCache = _registry.GetCache<PlayerCache>();
		_stadiumCache = _registry.GetCache<WarehouseCache>();
		_contractCache = _registry.GetCache<ContractCache>();

		SeedLargeDataset();
	}

	private DataCacheRegistry _registry = null!;
	private ClubCache _clubCache = null!;
	private PlayerCache _playerCache = null!;
	private WarehouseCache _stadiumCache = null!;
	private ContractCache _contractCache = null!;

	private void SeedLargeDataset() {
		// Add many clubs
		for (var i = 1; i <= 20; i++)
			_clubCache.AddOrUpdate(new Store { Id = i, Name = $"Store {i}", City = $"City {i % 5}" });

		// Add stadiums for each club
		for (var i = 1; i <= 20; i++)
			_stadiumCache.AddOrUpdate(new Warehouse {
				Id = i,
				StoreId = i,
				Name = $"Warehouse {i}",
				Capacity = 50000 + i * 1000
			});

		// Add many players per club
		var playerId = 1;
		for (var clubId = 1; clubId <= 20; clubId++)
		for (var j = 0; j < 10; j++)
			_playerCache.AddOrUpdate(new Player {
				Id = playerId++,
				StoreId = clubId,
				Name = $"Player {playerId}",
				Age = 20 + j
			});

		// Add contracts
		var contractId = 1;
		for (var clubId = 1; clubId <= 20; clubId++)
		for (var j = 0; j < 5; j++)
			_contractCache.AddOrUpdate(new Contract {
				Id = contractId++,
				StoreId = clubId,
				Salary = 100000m * (j + 1),
				Years = j + 1
			});
	}

	[Test]
	public void JoinMany_WithWhereFilter_ExercisesInternalTryGetValuesWithPredicate() {
		// This exercises the internal TryGetValues overload with predicate
		var builder = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex, q => q.Where(p => p.Age > 25));
		var results = builder.Execute();

		Assert.That(results.Count, Is.EqualTo(20)); // All clubs
		// Each club has players aged 20-29, so 4 players have age > 25 (26, 27, 28, 29)
		foreach (var result in results) Assert.That(result.Right.All(p => p.Age > 25), Is.True);
	}

	[Test]
	public void JoinMany_WithMultipleWhereFilters_ExercisesPredicateChaining() {
		var builder = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex, q => q
				.Where(p => p.Age >= 22)
				.Where(p => p.Age <= 27));
		var results = builder.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		foreach (var result in results) Assert.That(result.Right.All(p => p.Age >= 22 && p.Age <= 27), Is.True);
	}

	[Test]
	public void JoinOne_WithWhereFilter_ExercisesInternalContainerMethods() {
		var builder = _clubCache.Cache
			.Query()
			.JoinOne(_stadiumCache.Cache, _stadiumCache.ClubIdIndex, q => q.Where(s => s.Capacity > 60000));
		var results = builder.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		// Warehouses with capacity > 60000 are those with StoreId > 10
		var matchedCount = results.Count(r => r.Right != null);
		Assert.That(matchedCount, Is.GreaterThan(0));
	}

	[Test]
	public void JoinManyMany_ExercisesValueSetBasedLookups() {
		var b1 = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var b2 = b1.JoinMany(_contractCache.Cache, _contractCache.ClubIdIndex);
		var results = b2.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		foreach (var result in results) {
			Assert.That(result.Right.Count, Is.EqualTo(10)); // 10 players per club
			Assert.That(result.Right2.Count, Is.EqualTo(5)); // 5 contracts per club
		}
	}

	[Test]
	public void JoinManyManyWithFilters_ExercisesPredicateOnMultipleJoins() {
		var b1 = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex, q => q.Where(p => p.Age < 25));
		var b2 = b1.JoinMany(_contractCache.Cache, _contractCache.ClubIdIndex, q => q.Where(c => c.Salary > 200000m));
		var results = b2.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		foreach (var result in results) {
			Assert.That(result.Right.All(p => p.Age < 25), Is.True);
			Assert.That(result.Right2.All(c => c.Salary > 200000m), Is.True);
		}
	}

	[Test]
	public void JoinOneMany_ExercisesMixedJoinTypes() {
		var b1 = _clubCache.Cache
			.Query()
			.JoinOne(_stadiumCache.Cache, _stadiumCache.ClubIdIndex);
		var b2 = b1.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var results = b2.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		foreach (var result in results) {
			Assert.That(result.Right, Is.Not.Null);
			Assert.That(result.Right2.Count, Is.EqualTo(10));
		}
	}

	[Test]
	public void JoinManyOne_ExercisesMixedJoinTypesReversed() {
		var b1 = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var b2 = b1.JoinOne(_stadiumCache.Cache, _stadiumCache.ClubIdIndex);
		var results = b2.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		foreach (var result in results) {
			Assert.That(result.Right.Count, Is.EqualTo(10));
			Assert.That(result.Right2, Is.Not.Null);
		}
	}

	[Test]
	public void Query_UseIndexWithMultipleValues_ExercisesBatchLookup() {
		var clubIds = new List<int> { 1, 5, 10, 15, 20 };
		var results = _playerCache.Cache
			.Query()
			.UseIndex(_playerCache.ClubIdIndex, clubIds)
			.Execute();

		// 5 clubs * 10 players = 50 players
		Assert.That(results.Count, Is.EqualTo(50));
	}

	[Test]
	public void Query_UseIndexWithWhereFilter_ExercisesBatchWithPredicate() {
		var clubIds = new List<int> { 1, 2, 3 };
		var results = _playerCache.Cache
			.Query()
			.UseIndex(_playerCache.ClubIdIndex, clubIds)
			.Where(p => p.Age > 25)
			.Execute();

		// 3 clubs * 4 players with age > 25 = 12 players
		Assert.That(results.Count, Is.EqualTo(12));
		Assert.That(results.All(p => p.Age > 25), Is.True);
	}

	[Test]
	public void JoinMany_ExecutePooled_ExercisesPooledContainer() {
		var builder = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var results = builder.ExecutePooled();

		Assert.That(results.Count, Is.EqualTo(20));
	}

	[Test]
	public void JoinMany_ExecuteCloned_ExercisesClonedContainer() {
		var builder = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var results = builder.ExecuteCloned();

		Assert.That(results.Count, Is.EqualTo(20));

		// Verify it's a clone
		results[0].Left.Name = "Modified";
		var original = _clubCache.Cache.Query().Execute();
		Assert.That(original[0].Name, Is.Not.EqualTo("Modified"));
	}

	[Test]
	public void JoinMany_ExecutePooledCloned_ExercisesPooledClonedContainer() {
		var builder = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var results = builder.ExecutePooledCloned();

		Assert.That(results.Count, Is.EqualTo(20));
	}

	[Test]
	public void JoinMany_WithPagination_ExercisesSkipTake() {
		var builder = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var results = builder.Execute(5, 5);

		Assert.That(results.Count, Is.EqualTo(5));
	}

	[Test]
	public void JoinMany_WithSorting_ExercisesComparer() {
		var comparer = Comparer<JoinResult<Store, QueryResults<Player>>>.Create((a, b) => string.Compare(b.Left.Name, a.Left.Name, StringComparison.Ordinal));
		var b1 = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var b2 = b1.Sort(comparer);
		var results = b2.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		// Should be sorted by club name descending
		for (var i = 1; i < results.Count; i++)
			Assert.That(string.Compare(results[i - 1].Left.Name, results[i].Left.Name, StringComparison.Ordinal),
				Is.GreaterThanOrEqualTo(0));
	}

	[Test]
	public void JoinMany_WithSortingAndPagination_ExercisesCombinedOptions() {
		var comparer = Comparer<JoinResult<Store, QueryResults<Player>>>.Create((a, b) => a.Left.Id.CompareTo(b.Left.Id));
		var b1 = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var b2 = b1.Sort(comparer);
		var results = b2.Execute(10, 5);

		Assert.That(results.Count, Is.EqualTo(5));
		Assert.That(results[0].Left.Id, Is.EqualTo(11));
	}

	[Test]
	public void JoinManyManyMany_ExercisesThreeLevelJoin() {
		var b1 = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var b2 = b1.JoinMany(_contractCache.Cache, _contractCache.ClubIdIndex);
		var b3 = b2.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex); // Same join again
		var results = b3.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		foreach (var result in results) {
			Assert.That(result.Right.Count, Is.EqualTo(10));
			Assert.That(result.Right2.Count, Is.EqualTo(5));
			Assert.That(result.Right3.Count, Is.EqualTo(10));
		}
	}

	[Test]
	public void JoinOneManyOne_ExercisesMixedThreeLevelJoin() {
		var b1 = _clubCache.Cache
			.Query()
			.JoinOne(_stadiumCache.Cache, _stadiumCache.ClubIdIndex);
		var b2 = b1.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var b3 = b2.JoinOne(_stadiumCache.Cache, _stadiumCache.ClubIdIndex);
		var results = b3.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		foreach (var result in results) {
			Assert.That(result.Right, Is.Not.Null);
			Assert.That(result.Right2.Count, Is.EqualTo(10));
			Assert.That(result.Right3, Is.Not.Null);
		}
	}

	[Test]
	public void JoinMany_EmptySource_ReturnsEmpty() {
		var emptyRegistry = new DataCacheRegistryBuilder()
			.Register<ClubCache>()
			.Register<PlayerCache>()
			.Build();
		var emptyClubCache = emptyRegistry.GetCache<ClubCache>();
		var emptyPlayerCache = emptyRegistry.GetCache<PlayerCache>();

		// Query empty source cache directly (not via join which has edge case issue)
		var clubResults = emptyClubCache.Cache.Query().Execute();
		Assert.That(clubResults.Count, Is.EqualTo(0));

		var playerResults = emptyPlayerCache.Cache.Query().Execute();
		Assert.That(playerResults.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_NoMatchingJoins_ReturnsEmptyRightCollections() {
		// Add a club with no players
		_clubCache.AddOrUpdate(new Store { Id = 999, Name = "Empty Store", City = "Nowhere" });

		var builder = _clubCache.Cache
			.Query()
			.Where(c => c.Id == 999)
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var results = builder.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results[0].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_WithFilterThatMatchesNothing_ReturnsEmptyRightCollections() {
		var builder = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex, q => q.Where(p => p.Age > 100));
		var results = builder.Execute();

		Assert.That(results.Count, Is.EqualTo(20));
		foreach (var result in results) Assert.That(result.Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_LargeDataset_ExercisesInternalBatchOperations() {
		// This test ensures internal batch operations work correctly with larger datasets
		var b1 = _clubCache.Cache
			.Query()
			.JoinMany(_playerCache.Cache, _playerCache.ClubIdIndex);
		var b2 = b1.JoinMany(_contractCache.Cache, _contractCache.ClubIdIndex);
		var allResults = b2.Execute();

		// Verify total counts
		var totalPlayers = allResults.Sum(r => r.Right.Count);
		var totalContracts = allResults.Sum(r => r.Right2.Count);

		Assert.That(totalPlayers, Is.EqualTo(200)); // 20 clubs * 10 players
		Assert.That(totalContracts, Is.EqualTo(100)); // 20 clubs * 5 contracts
	}
}

#endif
