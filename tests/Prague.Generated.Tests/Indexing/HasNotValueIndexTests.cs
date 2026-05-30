namespace Prague.Generated.Tests.Indexing;

using Prague.Core;
using NUnit.Framework;

#region Test Entities

/// <summary>
///   Test entity with nullable properties marked with [DataCacheHasNotValueIndex].
///   The code generator should create:
///   - HasNotPhoneIndex field (CacheKeySetIndex)
///   - HasNotAvatarIndex field (CacheKeySetIndex)
///   - HasNotRankIndex field (CacheKeySetIndex)
///   - WithoutPhone() query method
///   - WithoutAvatar() query method
///   - WithoutRank() query method
/// </summary>
[DataCache]
public partial class ProfileWithOptionalFields {
	[DataCacheKey] public int Id { get; set; }

	public required string Name { get; set; }

	/// <summary>
	///   Nullable reference type - will be indexed by HasNotPhoneIndex
	/// </summary>
	[DataCacheHasNotValueIndex]
	public string? Phone { get; set; }

	/// <summary>
	///   Another nullable reference type - will be indexed by HasNotAvatarIndex
	/// </summary>
	[DataCacheHasNotValueIndex]
	public string? Avatar { get; set; }

	/// <summary>
	///   Nullable value type - will be indexed by HasNotRankIndex
	/// </summary>
	[DataCacheHasNotValueIndex]
	public int? Rank { get; set; }

	/// <summary>
	///   Nullable value type with custom index name - will be indexed by UncompletedIndex
	/// </summary>
	[DataCacheHasNotValueIndex(IndexName = "UncompletedIndex")]
	public DateTime? CompletedAt { get; set; }

	/// <summary>
	///   Regular index for filtering
	/// </summary>
	[DataCacheIndex(DataCacheIndexType.Many)]
	public required string Category { get; set; }
}

#endregion

/// <summary>
///   Tests for [DataCacheHasNotValueIndex] attribute and generated code.
/// </summary>
[TestFixture]
public class HasNotValueIndexTests {
	[SetUp]
	public void SetUp() {
		_cache = new ProfileWithOptionalFieldsCache();
	}

	private ProfileWithOptionalFieldsCache _cache;

	#region Index Field Generation Tests

	[Test]
	public void GeneratedCache_ShouldHaveHasNotPhoneIndex() {
		// Assert - The index field should exist and be of correct type
		Assert.That(_cache.HasNotPhoneIndex, Is.Not.Null);
		Assert.That(_cache.HasNotPhoneIndex, Is.TypeOf<CacheKeySetIndex<int, ProfileWithOptionalFields>>());
	}

	[Test]
	public void GeneratedCache_ShouldHaveHasNotAvatarIndex() {
		Assert.That(_cache.HasNotAvatarIndex, Is.Not.Null);
		Assert.That(_cache.HasNotAvatarIndex, Is.TypeOf<CacheKeySetIndex<int, ProfileWithOptionalFields>>());
	}

	[Test]
	public void GeneratedCache_ShouldHaveHasNotRankIndex() {
		Assert.That(_cache.HasNotRankIndex, Is.Not.Null);
		Assert.That(_cache.HasNotRankIndex, Is.TypeOf<CacheKeySetIndex<int, ProfileWithOptionalFields>>());
	}

	[Test]
	public void GeneratedCache_ShouldHaveCustomNamedUncompletedIndex() {
		Assert.That(_cache.UncompletedIndex, Is.Not.Null);
		Assert.That(_cache.UncompletedIndex, Is.TypeOf<CacheKeySetIndex<int, ProfileWithOptionalFields>>());
	}

	#endregion

	#region Index Population Tests

	[Test]
	public void HasNotPhoneIndex_WhenPhoneNull_ShouldContainKey() {
		// Arrange & Act
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1,
			Name = "profile1",
			Phone = null,
			Category = "personal"
		});

		// Assert
		Assert.That(_cache.HasNotPhoneIndex.Contains(1), Is.True);
	}

	[Test]
	public void HasNotPhoneIndex_WhenPhoneNotNull_ShouldNotContainKey() {
		// Arrange & Act
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1,
			Name = "profile1",
			Phone = "555-1234",
			Category = "personal"
		});

		// Assert
		Assert.That(_cache.HasNotPhoneIndex.Contains(1), Is.False);
	}

	[Test]
	public void HasNotRankIndex_WhenRankNull_ShouldContainKey() {
		// Arrange & Act
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1,
			Name = "profile1",
			Rank = null,
			Category = "personal"
		});

		// Assert
		Assert.That(_cache.HasNotRankIndex.Contains(1), Is.True);
	}

	[Test]
	public void HasNotRankIndex_WhenRankNotNull_ShouldNotContainKey() {
		// Arrange & Act
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1,
			Name = "profile1",
			Rank = 5,
			Category = "personal"
		});

		// Assert
		Assert.That(_cache.HasNotRankIndex.Contains(1), Is.False);
	}

	[Test]
	public void HasNotValueIndex_MixedValues_ShouldIndexCorrectly() {
		// Arrange & Act
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = null, Rank = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 2, Name = "profile2", Phone = "555-0001", Rank = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 3, Name = "profile3", Phone = null, Rank = 10, Category = "business"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 4, Name = "profile4", Phone = "555-0002", Rank = 20, Category = "personal"
		});

		// Assert - HasNotPhoneIndex
		Assert.That(_cache.HasNotPhoneIndex.Contains(1), Is.True);
		Assert.That(_cache.HasNotPhoneIndex.Contains(2), Is.False);
		Assert.That(_cache.HasNotPhoneIndex.Contains(3), Is.True);
		Assert.That(_cache.HasNotPhoneIndex.Contains(4), Is.False);
		Assert.That(_cache.HasNotPhoneIndex.ApproximateCount, Is.EqualTo(2));

		// Assert - HasNotRankIndex
		Assert.That(_cache.HasNotRankIndex.Contains(1), Is.True);
		Assert.That(_cache.HasNotRankIndex.Contains(2), Is.True);
		Assert.That(_cache.HasNotRankIndex.Contains(3), Is.False);
		Assert.That(_cache.HasNotRankIndex.Contains(4), Is.False);
		Assert.That(_cache.HasNotRankIndex.ApproximateCount, Is.EqualTo(2));
	}

	#endregion

	#region Index Update Tests

	[Test]
	public void Update_FromValueToNull_ShouldAddToIndex() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = "555-1234", Category = "personal"
		});
		Assert.That(_cache.HasNotPhoneIndex.Contains(1), Is.False);

		// Act
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = null, Category = "personal"
		});

		// Assert
		Assert.That(_cache.HasNotPhoneIndex.Contains(1), Is.True);
	}

	[Test]
	public void Update_FromNullToValue_ShouldRemoveFromIndex() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = null, Category = "personal"
		});
		Assert.That(_cache.HasNotPhoneIndex.Contains(1), Is.True);

		// Act
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = "555-5678", Category = "personal"
		});

		// Assert
		Assert.That(_cache.HasNotPhoneIndex.Contains(1), Is.False);
	}

	[Test]
	public void Update_NullToNull_ShouldRemainInIndex() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = null, Category = "personal"
		});

		// Act
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1_updated", Phone = null, Category = "business"
		});

		// Assert
		Assert.That(_cache.HasNotPhoneIndex.Contains(1), Is.True);
		Assert.That(_cache.HasNotPhoneIndex.ApproximateCount, Is.EqualTo(1));
	}

	#endregion

	#region Index Remove Tests

	[Test]
	public void Remove_ExistingKeyInIndex_ShouldRemoveFromIndex() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = null, Category = "personal"
		});

		// Act
		_cache.Remove(1);

		// Assert
		Assert.That(_cache.HasNotPhoneIndex.Contains(1), Is.False);
		Assert.That(_cache.HasNotPhoneIndex.ApproximateCount, Is.EqualTo(0));
	}

	#endregion

	#region Query Method Tests

	[Test]
	public void WithoutPhone_ShouldFilterToOnlyProfilesWithoutPhone() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 2, Name = "profile2", Phone = "555-0001", Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 3, Name = "profile3", Phone = null, Category = "business"
		});

		// Act
		var results = _cache.Query()
			.WithoutPhone()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.Id), Is.EquivalentTo(new[] { 1, 3 }));
	}

	[Test]
	public void WithoutAvatar_ShouldFilterToOnlyProfilesWithoutAvatar() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Avatar = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 2, Name = "profile2", Avatar = "avatar.png", Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 3, Name = "profile3", Avatar = null, Category = "personal"
		});

		// Act
		var results = _cache.Query()
			.WithoutAvatar()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.Id), Is.EquivalentTo(new[] { 1, 3 }));
	}

	[Test]
	public void WithoutRank_ShouldFilterToOnlyProfilesWithoutRank() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Rank = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 2, Name = "profile2", Rank = 0, Category = "personal" // 0 is still a value!
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 3, Name = "profile3", Rank = null, Category = "personal"
		});

		// Act
		var results = _cache.Query()
			.WithoutRank()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(2));
		Assert.That(results.Select(p => p.Id), Is.EquivalentTo(new[] { 1, 3 }));
	}

	[Test]
	public void WithoutCompletedAt_CustomNamedIndex_ShouldWork() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", CompletedAt = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 2, Name = "profile2", CompletedAt = DateTime.Now, Category = "personal"
		});

		// Act
		var results = _cache.Query()
			.WithoutCompletedAt()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].Id, Is.EqualTo(1));
	}

	#endregion

	#region Combined Query Tests

	[Test]
	public void WithoutPhone_CombinedWithOtherIndex_ShouldIntersect() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 2, Name = "profile2", Phone = null, Category = "business"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 3, Name = "profile3", Phone = "555-0001", Category = "personal"
		});

		// Act
		var results = _cache.Query()
			.WithoutPhone()
			.WithCategory("personal")
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].Id, Is.EqualTo(1));
	}

	[Test]
	public void MultipleWithoutMethods_ShouldIntersect() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = null, Rank = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 2, Name = "profile2", Phone = null, Rank = 10, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 3, Name = "profile3", Phone = "555-0001", Rank = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 4, Name = "profile4", Phone = "555-0002", Rank = 20, Category = "personal"
		});

		// Act - Profiles without BOTH phone AND rank
		var results = _cache.Query()
			.WithoutPhone()
			.WithoutRank()
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].Id, Is.EqualTo(1));
	}

	[Test]
	public void WithoutPhone_CombinedWithWhereFilter_ShouldWork() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "alpha", Phone = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 2, Name = "beta", Phone = null, Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 3, Name = "gamma", Phone = "555-0001", Category = "personal"
		});

		// Act
		var results = _cache.Query()
			.WithoutPhone()
			.Where(p => p.Name.StartsWith("a"))
			.Execute();

		// Assert
		Assert.That(results, Has.Count.EqualTo(1));
		Assert.That(results[0].Name, Is.EqualTo("alpha"));
	}

	#endregion

	#region Empty Result Tests

	[Test]
	public void WithoutPhone_AllProfilesHavePhone_ShouldReturnEmpty() {
		// Arrange
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 1, Name = "profile1", Phone = "555-0001", Category = "personal"
		});
		_cache.AddOrUpdate(new ProfileWithOptionalFields {
			Id = 2, Name = "profile2", Phone = "555-0002", Category = "personal"
		});

		// Act
		var results = _cache.Query()
			.WithoutPhone()
			.Execute();

		// Assert
		Assert.That(results, Is.Empty);
	}

	[Test]
	public void WithoutPhone_EmptyCache_ShouldReturnEmpty() {
		// Act
		var results = _cache.Query()
			.WithoutPhone()
			.Execute();

		// Assert
		Assert.That(results, Is.Empty);
	}

	#endregion
}
