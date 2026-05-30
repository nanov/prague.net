namespace Prague.Generated.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

#region Test Entities

/// <summary>
///   Test entity with nullable properties marked with [DataCacheHasValueIndex].
///   The code generator should create:
///   - HasEmailIndex field (CacheKeySetIndex)
///   - HasNicknameIndex field (CacheKeySetIndex)
///   - HasScoreIndex field (CacheKeySetIndex)
///   - WithEmail() query method
///   - WithNickname() query method
///   - WithScore() query method
/// </summary>
[DataCache]
public partial class UserWithOptionalFields {
	[DataCacheKey] public int Id { get; set; }

	public required string Username { get; set; }

	/// <summary>
	///   Nullable reference type - will be indexed by HasEmailIndex
	/// </summary>
	[DataCacheHasValueIndex]
	public string? Email { get; set; }

	/// <summary>
	///   Another nullable reference type - will be indexed by HasNicknameIndex
	/// </summary>
	[DataCacheHasValueIndex]
	public string? Nickname { get; set; }

	/// <summary>
	///   Nullable value type - will be indexed by HasScoreIndex
	/// </summary>
	[DataCacheHasValueIndex]
	public int? Score { get; set; }

	/// <summary>
	///   Nullable value type with custom index name - will be indexed by VerifiedIndex
	/// </summary>
	[DataCacheHasValueIndex(IndexName = "VerifiedIndex")]
	public DateTime? VerifiedAt { get; set; }

	/// <summary>
	///   Regular index for filtering
	/// </summary>
	[DataCacheIndex(DataCacheIndexType.Many)]
	public required string Status { get; set; }
}

#endregion

/// <summary>
///   Tests for [DataCacheHasValueIndex] attribute and generated code.
/// </summary>
[TestFixture]
public class HasValueIndexTests {
	[SetUp]
	public void SetUp() {
		_cache = new UserWithOptionalFieldsCache();
	}

	private UserWithOptionalFieldsCache _cache;

	#region Index Field Generation Tests

	[Test]
	public void GeneratedCache_ShouldHaveHasEmailIndex() {
		// Assert - The index field should exist and be of correct type
		Assert.That(_cache.HasEmailIndex, Is.Not.Null);
		Assert.That(_cache.HasEmailIndex, Is.TypeOf<CacheKeySetIndex<int, UserWithOptionalFields>>());
	}

	[Test]
	public void GeneratedCache_ShouldHaveHasNicknameIndex() {
		Assert.That(_cache.HasNicknameIndex, Is.Not.Null);
		Assert.That(_cache.HasNicknameIndex, Is.TypeOf<CacheKeySetIndex<int, UserWithOptionalFields>>());
	}

	[Test]
	public void GeneratedCache_ShouldHaveHasScoreIndex() {
		Assert.That(_cache.HasScoreIndex, Is.Not.Null);
		Assert.That(_cache.HasScoreIndex, Is.TypeOf<CacheKeySetIndex<int, UserWithOptionalFields>>());
	}

	[Test]
	public void GeneratedCache_ShouldHaveCustomNamedVerifiedIndex() {
		Assert.That(_cache.VerifiedIndex, Is.Not.Null);
		Assert.That(_cache.VerifiedIndex, Is.TypeOf<CacheKeySetIndex<int, UserWithOptionalFields>>());
	}

	#endregion

	#region Index Population Tests

	[Test]
	public void HasEmailIndex_WhenEmailNotNull_ShouldContainKey() {
		// Arrange & Act
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1,
			Username = "user1",
			Email = "user1@example.com",
			Status = "active"
		});

		// Assert
		Assert.That(_cache.HasEmailIndex.Contains(1), Is.True);
	}

	[Test]
	public void HasEmailIndex_WhenEmailNull_ShouldNotContainKey() {
		// Arrange & Act
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1,
			Username = "user1",
			Email = null,
			Status = "active"
		});

		// Assert
		Assert.That(_cache.HasEmailIndex.Contains(1), Is.False);
	}

	[Test]
	public void HasScoreIndex_WhenScoreNotNull_ShouldContainKey() {
		// Arrange & Act
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1,
			Username = "user1",
			Score = 100,
			Status = "active"
		});

		// Assert
		Assert.That(_cache.HasScoreIndex.Contains(1), Is.True);
	}

	[Test]
	public void HasScoreIndex_WhenScoreNull_ShouldNotContainKey() {
		// Arrange & Act
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1,
			Username = "user1",
			Score = null,
			Status = "active"
		});

		// Assert
		Assert.That(_cache.HasScoreIndex.Contains(1), Is.False);
	}

	[Test]
	public void HasValueIndex_MixedValues_ShouldIndexCorrectly() {
		// Arrange & Act
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = "a@b.com", Score = 100, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 2, Username = "user2", Email = null, Score = 200, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 3, Username = "user3", Email = "c@d.com", Score = null, Status = "inactive"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 4, Username = "user4", Email = null, Score = null, Status = "active"
		});

		// Assert - HasEmailIndex
		Assert.That(_cache.HasEmailIndex.Contains(1), Is.True);
		Assert.That(_cache.HasEmailIndex.Contains(2), Is.False);
		Assert.That(_cache.HasEmailIndex.Contains(3), Is.True);
		Assert.That(_cache.HasEmailIndex.Contains(4), Is.False);
		Assert.That(_cache.HasEmailIndex.ApproximateCount, Is.EqualTo(2));

		// Assert - HasScoreIndex
		Assert.That(_cache.HasScoreIndex.Contains(1), Is.True);
		Assert.That(_cache.HasScoreIndex.Contains(2), Is.True);
		Assert.That(_cache.HasScoreIndex.Contains(3), Is.False);
		Assert.That(_cache.HasScoreIndex.Contains(4), Is.False);
		Assert.That(_cache.HasScoreIndex.ApproximateCount, Is.EqualTo(2));
	}

	#endregion

	#region Index Update Tests

	[Test]
	public void Update_FromNullToValue_ShouldAddToIndex() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = null, Status = "active"
		});
		Assert.That(_cache.HasEmailIndex.Contains(1), Is.False);

		// Act
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = "new@email.com", Status = "active"
		});

		// Assert
		Assert.That(_cache.HasEmailIndex.Contains(1), Is.True);
	}

	[Test]
	public void Update_FromValueToNull_ShouldRemoveFromIndex() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = "user@email.com", Status = "active"
		});
		Assert.That(_cache.HasEmailIndex.Contains(1), Is.True);

		// Act
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = null, Status = "active"
		});

		// Assert
		Assert.That(_cache.HasEmailIndex.Contains(1), Is.False);
	}

	[Test]
	public void Update_ValueToValue_ShouldRemainInIndex() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = "old@email.com", Status = "active"
		});

		// Act
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = "new@email.com", Status = "active"
		});

		// Assert
		Assert.That(_cache.HasEmailIndex.Contains(1), Is.True);
		Assert.That(_cache.HasEmailIndex.ApproximateCount, Is.EqualTo(1));
	}

	#endregion

	#region Index Remove Tests

	[Test]
	public void Remove_ExistingKeyInIndex_ShouldRemoveFromIndex() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = "user@email.com", Status = "active"
		});

		// Act
		_cache.Remove(1);

		// Assert
		Assert.That(_cache.HasEmailIndex.Contains(1), Is.False);
		Assert.That(_cache.HasEmailIndex.ApproximateCount, Is.EqualTo(0));
	}

	#endregion

	#region Query Method Tests

	[Test]
	public void WithEmail_ShouldFilterToOnlyUsersWithEmail() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = "a@b.com", Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 2, Username = "user2", Email = null, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 3, Username = "user3", Email = "c@d.com", Status = "inactive"
		});

		// Act
		var results = _cache.Query()
			.WithEmail()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(u => u.Id), Is.EquivalentTo(new[] { 1, 3 }));
	}

	[Test]
	public void WithNickname_ShouldFilterToOnlyUsersWithNickname() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Nickname = "nick1", Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 2, Username = "user2", Nickname = null, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 3, Username = "user3", Nickname = "nick3", Status = "active"
		});

		// Act
		var results = _cache.Query()
			.WithNickname()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(u => u.Id), Is.EquivalentTo(new[] { 1, 3 }));
	}

	[Test]
	public void WithScore_ShouldFilterToOnlyUsersWithScore() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Score = 100, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 2, Username = "user2", Score = null, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 3, Username = "user3", Score = 0, Status = "active" // 0 is still a value!
		});

		// Act
		var results = _cache.Query()
			.WithScore()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(u => u.Id), Is.EquivalentTo(new[] { 1, 3 }));
	}

	[Test]
	public void WithVerifiedAt_CustomNamedIndex_ShouldWork() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", VerifiedAt = DateTime.Now, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 2, Username = "user2", VerifiedAt = null, Status = "active"
		});

		// Act
		var results = _cache.Query()
			.WithVerifiedAt()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].Id, Is.EqualTo(1));
	}

	#endregion

	#region Combined Query Tests

	[Test]
	public void WithEmail_CombinedWithOtherIndex_ShouldIntersect() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = "a@b.com", Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 2, Username = "user2", Email = "c@d.com", Status = "inactive"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 3, Username = "user3", Email = null, Status = "active"
		});

		// Act
		var results = _cache.Query()
			.WithEmail()
			.WithStatus("active")
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].Id, Is.EqualTo(1));
	}

	[Test]
	public void MultipleWithMethods_ShouldIntersect() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = "a@b.com", Score = 100, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 2, Username = "user2", Email = "c@d.com", Score = null, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 3, Username = "user3", Email = null, Score = 200, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 4, Username = "user4", Email = "e@f.com", Score = 300, Status = "active"
		});

		// Act - Users with BOTH email AND score
		var results = _cache.Query()
			.WithEmail()
			.WithScore()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(u => u.Id), Is.EquivalentTo(new[] { 1, 4 }));
	}

	[Test]
	public void WithEmail_CombinedWithWhereFilter_ShouldWork() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "alpha", Email = "a@b.com", Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 2, Username = "beta", Email = "c@d.com", Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 3, Username = "gamma", Email = null, Status = "active"
		});

		// Act
		var results = _cache.Query()
			.WithEmail()
			.Where(u => u.Username.StartsWith("a"))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].Username, Is.EqualTo("alpha"));
	}

	#endregion

	#region Empty Result Tests

	[Test]
	public void WithEmail_NoUsersWithEmail_ShouldReturnEmpty() {
		// Arrange
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 1, Username = "user1", Email = null, Status = "active"
		});
		_cache.AddOrUpdate(new UserWithOptionalFields {
			Id = 2, Username = "user2", Email = null, Status = "active"
		});

		// Act
		var results = _cache.Query()
			.WithEmail()
			.Execute();

		// Assert
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void WithEmail_EmptyCache_ShouldReturnEmpty() {
		// Act
		var results = _cache.Query()
			.WithEmail()
			.Execute();

		// Assert
		Assert.That(results, Is.Empty);
	}

	#endregion

	#region Pagination and Sorting Tests

	[Test]
	public void WithEmail_WithPagination_ShouldWork() {
		// Arrange
		for (var i = 1; i <= 10; i++)
			_cache.AddOrUpdate(new UserWithOptionalFields {
				Id = i,
				Username = $"user{i}",
				Email = $"user{i}@example.com",
				Status = "active"
			});

		// Act
		var results = _cache.Query()
			.WithEmail()
			.Execute(2, 3);

		// Assert
		Assert.That(results, Has.Count.EqualTo(3));
		Assert.That(results.TotalCount, Is.EqualTo(10));
	}

	#endregion
}
