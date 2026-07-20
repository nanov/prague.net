namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using Core;

/// <summary>
///   Quantifies the cost of joined <c>Count()</c> (which now runs indexed-inner narrowing, see
///   PR fix/joined-count-and-nested-candidates) against the pooled materializing path
///   <c>ExecutePooled()</c>. Gate: <c>Count_InnerJoin</c> may cost more than on main (narrowing
///   is the approved semantics change) but must stay ≤ <c>ExecutePooled_InnerJoin</c>; the
///   pooled path itself must be unaffected by the change.
///
///   Run with: dotnet run -c Release --project benchmarks/Prague.Benchmarks -f net9.0 -- --filter "*JoinCountBenchmarks*" --job short
/// </summary>
[MemoryDiagnoser]
public class JoinCountBenchmarks {
	private InMemoryDataCache<int, BenchLeft> _left = null!;
	private InMemoryDataCache<int, BenchRight> _right = null!;
	private CacheUniqueIndex<int, BenchRight, int> _rightIndex = null!;

	[GlobalSetup]
	public void Setup() {
		_left = new InMemoryDataCache<int, BenchLeft>();
		_right = new InMemoryDataCache<int, BenchRight>();
		_rightIndex = _right.AddKeyValueIndex<int>(static (_, v) => v.LeftId);
		for (var i = 0; i < 10_000; i++) {
			_left.AddOrUpdate(i, new BenchLeft { Id = i });
			if (i % 2 == 0)
				_right.AddOrUpdate(i, new BenchRight { Id = i, LeftId = i });
		}
	}

	[Benchmark(Baseline = true)]
	public int ExecutePooled_InnerJoin() {
		using var r = _left.Query().InnerJoinOne(_right, _rightIndex).ExecutePooled();
		return r.Count;
	}

	[Benchmark]
	public int Count_InnerJoin() => _left.Query().InnerJoinOne(_right, _rightIndex).Count();
}

public sealed class BenchLeft : ICacheEquatable<BenchLeft>, ICacheClonable<BenchLeft> {
	public int Id { get; init; }
	public bool CacheEquals(BenchLeft? other) => other is not null && other.Id == Id;
	public int CacheGetHashCode() => Id;
	public BenchLeft Clone() => new() { Id = Id };
}

public sealed class BenchRight : ICacheEquatable<BenchRight>, ICacheClonable<BenchRight> {
	public int Id { get; init; }
	public int LeftId { get; init; }
	public bool CacheEquals(BenchRight? other) => other is not null && other.Id == Id && other.LeftId == LeftId;
	public int CacheGetHashCode() => HashCode.Combine(Id, LeftId);
	public BenchRight Clone() => new() { Id = Id, LeftId = LeftId };
}
