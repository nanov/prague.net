namespace Prague.Benchmarks;

	using BenchmarkDotNet.Attributes;
	using Prague.Core;

public readonly record struct CompositeId(int A, int B);

public sealed class CompositeEntity : ICacheEquatable<CompositeEntity>, ICacheClonable<CompositeEntity> {
	public CompositeId Id { get; init; }
	public int CategoryId { get; init; }
	public int RegionId { get; init; }
	public long Timestamp { get; init; }
	public int Age { get; init; }

	public CompositeEntity Clone() => this; // immutable: identity clone keeps allocs out of the measured path

	public bool CacheEquals(CompositeEntity? other) =>
		other is not null && Id == other.Id && CategoryId == other.CategoryId
		&& RegionId == other.RegionId && Timestamp == other.Timestamp && Age == other.Age;

	public int CacheGetHashCode() => Id.GetHashCode();
}

public sealed class StringKeyEntity : ICacheEquatable<StringKeyEntity>, ICacheClonable<StringKeyEntity> {
	public string Id { get; init; } = null!;
	public int CategoryId { get; init; }
	public int RegionId { get; init; }
	public long Timestamp { get; init; }
	public int Age { get; init; }

	public StringKeyEntity Clone() => this;

	public bool CacheEquals(StringKeyEntity? other) =>
		other is not null && Id == other.Id && CategoryId == other.CategoryId
		&& RegionId == other.RegionId && Timestamp == other.Timestamp && Age == other.Age;

	public int CacheGetHashCode() => Id.GetHashCode();
}

/// <summary>
///   Measures main-key hashing overhead in index maintenance (see the 2026-07-20 single-hash
///   spec). Model per spec: 2 set indexes (CategoryId, RegionId; ~100 entities per group) +
///   2 btree indexes (Timestamp: unique ascending; Age: 64 distinct values → heavy duplicate
///   runs, forcing the btree's composite (key, hash) mode — the shape that re-hashes hardest).
///   Two PK shapes: composite (int,int) = cheap-hash floor; ~24-char string = expensive-hash
///   ceiling. AddAll = N fresh inserts. UpdateAll = alternate between two prebuilt generations
///   whose CategoryId/RegionId/Timestamp/Age all differ, so every AddOrUpdate is a real Update
///   that moves every index group (the double-hash path in set indexes). Entities are prebuilt
///   in GlobalSetup; explicit timestamps keep DateTimeOffset.Now out of the measurement.
/// </summary>
[MemoryDiagnoser]
public class CacheIndexMaintenanceBenchmarks {
	[Params(100_000)] public int N { get; set; }

	private CompositeEntity[] _compositeGen0 = null!;
	private CompositeEntity[] _compositeGen1 = null!;
	private StringKeyEntity[] _stringGen0 = null!;
	private StringKeyEntity[] _stringGen1 = null!;

	private InMemoryDataCache<CompositeId, CompositeEntity> _compositeUpdateCache = null!;
	private InMemoryDataCache<string, StringKeyEntity> _stringUpdateCache = null!;
	private int _round;

	private static CompositeEntity MakeComposite(int i, int gen, int groups) => new() {
		Id = new CompositeId(i, ~i),
		CategoryId = (i + gen) % groups,
		RegionId = (i * 31 + gen) % groups,
		Timestamp = 1_700_000_000_000L + i * 2 + gen,
		Age = (i + gen) % 64,
	};

	private static StringKeyEntity MakeString(string key, int i, int gen, int groups) => new() {
		Id = key,
		CategoryId = (i + gen) % groups,
		RegionId = (i * 31 + gen) % groups,
		Timestamp = 1_700_000_000_000L + i * 2 + gen,
		Age = (i + gen) % 64,
	};

	private static InMemoryDataCache<CompositeId, CompositeEntity> NewCompositeCache() {
		var cache = new InMemoryDataCache<CompositeId, CompositeEntity>();
		cache.CacheKeyValueListIndex(static (_, v) => v.CategoryId);
		cache.CacheKeyValueListIndex(static (_, v) => v.RegionId);
		cache.CacheRangeIndex(static (_, v) => v.Timestamp);
		cache.CacheRangeIndex(static (_, v) => v.Age);
		return cache;
	}

	private static InMemoryDataCache<string, StringKeyEntity> NewStringCache() {
		var cache = new InMemoryDataCache<string, StringKeyEntity>();
		cache.CacheKeyValueListIndex(static (_, v) => v.CategoryId);
		cache.CacheKeyValueListIndex(static (_, v) => v.RegionId);
		cache.CacheRangeIndex(static (_, v) => v.Timestamp);
		cache.CacheRangeIndex(static (_, v) => v.Age);
		return cache;
	}

	[GlobalSetup]
	public void Setup() {
		var groups = Math.Max(1, N / 100);
		_compositeGen0 = new CompositeEntity[N];
		_compositeGen1 = new CompositeEntity[N];
		_stringGen0 = new StringKeyEntity[N];
		_stringGen1 = new StringKeyEntity[N];
		for (var i = 0; i < N; i++) {
			_compositeGen0[i] = MakeComposite(i, 0, groups);
			_compositeGen1[i] = MakeComposite(i, 1, groups);
			var key = $"cust-{i:D19}"; // ~24 chars, shared prefix — id-like
			_stringGen0[i] = MakeString(key, i, 0, groups);
			_stringGen1[i] = MakeString(key, i, 1, groups);
		}

		_compositeUpdateCache = NewCompositeCache();
		_stringUpdateCache = NewStringCache();
		for (var i = 0; i < N; i++) {
			_compositeUpdateCache.AddOrUpdate(_compositeGen0[i].Id, _compositeGen0[i], i);
			_stringUpdateCache.AddOrUpdate(_stringGen0[i].Id, _stringGen0[i], i);
		}

		_round = 0;
	}

	[Benchmark]
	public void AddAll_Composite() {
		var cache = NewCompositeCache();
		var entities = _compositeGen0;
		for (var i = 0; i < entities.Length; i++)
			cache.AddOrUpdate(entities[i].Id, entities[i], i);
	}

	[Benchmark]
	public void AddAll_String() {
		var cache = NewStringCache();
		var entities = _stringGen0;
		for (var i = 0; i < entities.Length; i++)
			cache.AddOrUpdate(entities[i].Id, entities[i], i);
	}

	[Benchmark]
	public void UpdateAll_Composite() {
		var gen = ++_round & 1;
		var entities = gen == 0 ? _compositeGen0 : _compositeGen1;
		var ts = (long)_round * N;
		for (var i = 0; i < entities.Length; i++)
			_compositeUpdateCache.AddOrUpdate(entities[i].Id, entities[i], ts + i);
	}

	[Benchmark]
	public void UpdateAll_String() {
		var gen = ++_round & 1; // each benchmark runs in its own process — every Update method must flip its own generations
		var entities = gen == 0 ? _stringGen0 : _stringGen1;
		var ts = (long)_round * N;
		for (var i = 0; i < entities.Length; i++)
			_stringUpdateCache.AddOrUpdate(entities[i].Id, entities[i], ts + i);
	}
}
