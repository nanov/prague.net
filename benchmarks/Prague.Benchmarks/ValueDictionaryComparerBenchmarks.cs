namespace Prague.Benchmarks;

using Prague.Core.Collections;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

/// <summary>
/// Measures the JIT-devirtualization win from the struct-generic <c>TKeyComparer</c> on
/// <see cref="ValueDictionary{TKey,TValue,TComparer}"/>. Two closed generics are exercised on
/// the same hot path (mixed Add + TryGetValue, both positive and negative lookups — the shape
/// that dominates <c>RetainNonNullSlots</c> and join intersect paths):
/// <list type="bullet">
///   <item><c>DefaultKeyComparer&lt;T&gt;</c> — zero-size struct, <c>x.Equals(y)</c>, JIT folds to direct call.</item>
///   <item><c>CustomKeyComparer&lt;T&gt;</c> — wraps an <see cref="IEqualityComparer{T}"/>, virtual dispatch per probe.</item>
/// </list>
/// The delta approximates what the migration buys vs. the pre-Phase-1 behavior (which carried an
/// <c>IEqualityComparer&lt;TKey&gt;?</c> reference field with a null-check + virtual call per probe).
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[BenchmarkDotNet.Attributes.InProcess]
public class ValueDictionaryComparerBenchmarks {
	private const int N = 4096;
	private int[] _keys = null!;
	private int[] _missingKeys = null!;
	private string[] _strKeys = null!;
	private string[] _strMissingKeys = null!;

	[GlobalSetup]
	public void Setup() {
		_keys = new int[N];
		_missingKeys = new int[N];
		_strKeys = new string[N];
		_strMissingKeys = new string[N];
		for (var i = 0; i < N; i++) {
			_keys[i] = i;
			_missingKeys[i] = i + 1_000_000;
			_strKeys[i] = $"key_{i}";
			_strMissingKeys[i] = $"miss_{i + 1_000_000}";
		}
	}

	[Benchmark]
	public int IntKeys_Default() {
		using var dict = new ValueDictionary<int, int, DefaultKeyComparer<int>>(false, N);
		for (var i = 0; i < N; i++) dict.Add(_keys[i], i);
		var hit = 0;
		for (var i = 0; i < N; i++)
			if (dict.TryGetValue(_keys[i], out var v)) hit += v;
		for (var i = 0; i < N; i++)
			if (dict.TryGetValue(_missingKeys[i], out var v)) hit += v;
		return hit;
	}

	[Benchmark(Baseline = true)]
	public int IntKeys_Custom() {
		using var dict = new ValueDictionary<int, int, CustomKeyComparer<int>>(false, N,
			new CustomKeyComparer<int>(EqualityComparer<int>.Default));
		for (var i = 0; i < N; i++) dict.Add(_keys[i], i);
		var hit = 0;
		for (var i = 0; i < N; i++)
			if (dict.TryGetValue(_keys[i], out var v)) hit += v;
		for (var i = 0; i < N; i++)
			if (dict.TryGetValue(_missingKeys[i], out var v)) hit += v;
		return hit;
	}

	// ── string keys ─────────────────────────────────────────────────────────

	[Benchmark]
	public int StringKeys_Default() {
		using var dict = new ValueDictionary<string, int, DefaultKeyComparer<string>>(false, N);
		for (var i = 0; i < N; i++) dict.Add(_strKeys[i], i);
		var hit = 0;
		for (var i = 0; i < N; i++)
			if (dict.TryGetValue(_strKeys[i], out var v)) hit += v;
		for (var i = 0; i < N; i++)
			if (dict.TryGetValue(_strMissingKeys[i], out var v)) hit += v;
		return hit;
	}

	[Benchmark]
	public int StringKeys_Custom() {
		using var dict = new ValueDictionary<string, int, CustomKeyComparer<string>>(false, N,
			new CustomKeyComparer<string>(StringComparer.Ordinal));
		for (var i = 0; i < N; i++) dict.Add(_strKeys[i], i);
		var hit = 0;
		for (var i = 0; i < N; i++)
			if (dict.TryGetValue(_strKeys[i], out var v)) hit += v;
		for (var i = 0; i < N; i++)
			if (dict.TryGetValue(_strMissingKeys[i], out var v)) hit += v;
		return hit;
	}

	// ── ConcurrentCacheStore (Phase 4 — internal DefaultKeyComparer<TKey>) ────

	[Benchmark]
	public int ConcurrentCacheStore_IntKeys() {
		var cache = new ConcurrentCacheStore<int, int>();
		for (var i = 0; i < N; i++)
			cache.AddOrUpdate(_keys[i], (_, _) => i, (_, old, _) => old, default(object));
		var hit = 0;
		for (var i = 0; i < N; i++)
			if (cache.TryGetValue(_keys[i], out var v)) hit += v;
		for (var i = 0; i < N; i++)
			if (cache.TryGetValue(_missingKeys[i], out var v)) hit += v;
		return hit;
	}

	[Benchmark]
	public int ConcurrentCacheStore_StringKeys() {
		var cache = new ConcurrentCacheStore<string, int>();
		for (var i = 0; i < N; i++)
			cache.AddOrUpdate(_strKeys[i], (_, _) => i, (_, old, _) => old, default(object));
		var hit = 0;
		for (var i = 0; i < N; i++)
			if (cache.TryGetValue(_strKeys[i], out var v)) hit += v;
		for (var i = 0; i < N; i++)
			if (cache.TryGetValue(_strMissingKeys[i], out var v)) hit += v;
		return hit;
	}
}
