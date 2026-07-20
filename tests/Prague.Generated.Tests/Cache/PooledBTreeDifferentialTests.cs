namespace Prague.Generated.Tests.Cache;

using NUnit.Framework;
using Prague.Core.Collections;
using System.Text;

// ───────────────────── Tree adapter ─────────────────────
//
// The harness drives the production tree through a thin adapter: PooledBTree nests
// its own IResultAggregator, so RangeFrom needs a concrete collector struct per
// closed type. Virtual dispatch is irrelevant here — this is a correctness oracle,
// not a benchmark.

public interface ITreeUnderTest<TIndex, TValue> : IDisposable {
	string Name { get; }
	bool Add(TIndex index, TValue value);
	bool Remove(TIndex index, TValue value);
	bool Contains(TIndex index, TValue value);
	int Length { get; }
	void RangeFromInto(TIndex start, List<(TIndex, TValue)> sink);
}

/// <summary>Adapter over the real production tree, <see cref="PooledBTree{TIndex,TValue}"/>.</summary>
public sealed class RealTree<TIndex, TValue> : ITreeUnderTest<TIndex, TValue>
	where TIndex : IComparable<TIndex>
	where TValue : IEquatable<TValue> {
	private readonly PooledBTree<TIndex, TValue> _tree = new();

	public string Name => "PooledBTree";
	public bool Add(TIndex index, TValue value) => _tree.Add(index, value);
	public bool Remove(TIndex index, TValue value) => _tree.Remove(index, value);
	public bool Contains(TIndex index, TValue value) => _tree.Contains(index, value);
	public int Length => _tree.Length;
	public void Dispose() => _tree.Dispose();

	private struct Collector : PooledBTree<TIndex, TValue>.IResultAggregator {
		public List<(TIndex, TValue)> Sink;
		public void Add(TIndex index, TValue value) => Sink.Add((index, value));
		public void Dispose() { }
	}

	public void RangeFromInto(TIndex start, List<(TIndex, TValue)> sink) {
		var c = new Collector { Sink = sink };
		_tree.RangeFrom(start, ref c);
	}
}

// ───────────────────── Value types under test ─────────────────────

/// <summary>Forced total hash collision: every instance hashes to 0, Equals is exact.</summary>
public readonly record struct ZeroHash(int Id) {
	public bool Equals(ZeroHash other) => Id == other.Id;
	public override int GetHashCode() => 0;
	public override string ToString() => $"Z({Id})";
}

/// <summary>Partial collisions: four hash buckets.</summary>
public readonly record struct Mod4Hash(int Id) {
	public bool Equals(Mod4Hash other) => Id == other.Id;
	public override int GetHashCode() => Id & 3;
	public override string ToString() => $"M({Id})";
}

/// <summary>
///   CompareTo is INCONSISTENT with Equals — it orders on A only while Equals
///   compares (A, B). Any tree that uses CompareTo == 0 as an identity test will
///   confuse distinct values.
/// </summary>
public readonly struct Inconsistent : IEquatable<Inconsistent>, IComparable<Inconsistent> {
	public readonly int A;
	public readonly int B;
	public Inconsistent(int a, int b) { A = a; B = b; }
	public bool Equals(Inconsistent other) => A == other.A && B == other.B;
	public override bool Equals(object? obj) => obj is Inconsistent o && Equals(o);
	public override int GetHashCode() => HashCode.Combine(A, B);
	public int CompareTo(Inconsistent other) => A.CompareTo(other.A); // ignores B on purpose
	public override string ToString() => $"I({A},{B})";
}

/// <summary>
///   A realistic composite key: two ints folded 64→32 bits for the hash. The fold is XOR,
///   the classic weak one, so (a,b), (b,a) and every (a^k, b^k) land in the same bucket —
///   i.e. the composite ordering degenerates to long collision runs while Equals stays
///   exact. Ordering is (A, then B), consistent with Equals.
/// </summary>
public readonly struct IntPairKey : IEquatable<IntPairKey>, IComparable<IntPairKey> {
	public readonly int A;
	public readonly int B;
	public IntPairKey(int a, int b) { A = a; B = b; }
	public bool Equals(IntPairKey other) => A == other.A && B == other.B;
	public override bool Equals(object? obj) => obj is IntPairKey o && Equals(o);
	public override int GetHashCode() => A ^ B; // deliberate 64→32 fold with dense collisions
	public int CompareTo(IntPairKey other) {
		var c = A.CompareTo(other.A);
		return c != 0 ? c : B.CompareTo(other.B);
	}
	public override string ToString() => $"P({A},{B})";
}

// ───────────────────── Harness ─────────────────────

public sealed record OpRecord<TIndex, TValue>(string Op, TIndex Index, TValue Value);

public sealed class DivergenceException : Exception {
	public DivergenceException(string message) : base(message) { }
}

public static class BTreeDifferential {
	public const int LeafCapacity = 64;

	/// <summary>
	///   Runs a randomized op sequence against <paramref name="factory"/>'s tree and a
	///   set oracle. Throws <see cref="DivergenceException"/> with the full replayable op
	///   log (minimized) on the first disagreement.
	/// </summary>
	public static void Run<TIndex, TValue>(
		Func<ITreeUnderTest<TIndex, TValue>> factory,
		(TIndex Index, TValue Value)[] pool,
		TIndex rangeMin,
		int seed,
		int opCount,
		int rangeCheckEvery = 97)
		where TIndex : IComparable<TIndex>
		where TValue : IEquatable<TValue> =>
		RunOps(factory, GenerateOps(pool, seed, opCount), rangeMin, seed, rangeCheckEvery);

	/// <summary>
	///   Same contract as <see cref="Run"/> but takes a pre-built op sequence, so a caller
	///   can shape the sequence itself (e.g. switch pools partway through a run).
	/// </summary>
	public static void RunOps<TIndex, TValue>(
		Func<ITreeUnderTest<TIndex, TValue>> factory,
		List<OpRecord<TIndex, TValue>> ops,
		TIndex rangeMin,
		int seed,
		int rangeCheckEvery = 97)
		where TIndex : IComparable<TIndex>
		where TValue : IEquatable<TValue> {
		var opCount = ops.Count;
		var failure = Replay(factory, ops, rangeMin, rangeCheckEvery);
		if (failure is null) return;

		// Minimize: delta-debug the prefix, then greedily drop individual ops.
		var minimal = Minimize(factory, ops, rangeMin, rangeCheckEvery, failure.Value.Index);
		var stillFails = Replay(factory, minimal, rangeMin, rangeCheckEvery);
		var sb = new StringBuilder();
		using (var probe = factory()) sb.Append(probe.Name);
		sb.Append(" diverged. seed=").Append(seed).Append(" opCount=").Append(opCount).AppendLine();
		sb.AppendLine("first failure: " + failure.Value.Message);
		sb.Append("minimal repro (").Append(minimal.Count).Append(" ops, reproduces: ")
			.Append(stillFails is not null).AppendLine("):");
		foreach (var op in minimal)
			sb.Append("  ").Append(op.Op).Append('(').Append(op.Index).Append(", ").Append(op.Value).AppendLine(")");
		if (stillFails is not null) sb.AppendLine("minimal failure: " + stillFails.Value.Message);
		throw new DivergenceException(sb.ToString());
	}

	public static List<OpRecord<TIndex, TValue>> GenerateOps<TIndex, TValue>(
		(TIndex Index, TValue Value)[] pool, int seed, int opCount)
		where TIndex : IComparable<TIndex>
		where TValue : IEquatable<TValue> =>
		GeneratePhasedOps(pool, pool, opCount, seed, opCount);

	/// <summary>
	///   Draws from <paramref name="phase1"/> for the first <paramref name="switchAt"/> ops
	///   and from <paramref name="phase2"/> afterwards, over ONE shared live-set — so
	///   phase-2 Remove/Contains ops still target entries inserted in phase 1. Used to walk a
	///   tree from a strictly-unique-key population into a duplicate-key one mid-life.
	/// </summary>
	public static List<OpRecord<TIndex, TValue>> GeneratePhasedOps<TIndex, TValue>(
		(TIndex Index, TValue Value)[] phase1,
		(TIndex Index, TValue Value)[] phase2,
		int switchAt,
		int seed,
		int opCount)
		where TIndex : IComparable<TIndex>
		where TValue : IEquatable<TValue> {
		var rnd = new Random(seed);
		var ops = new List<OpRecord<TIndex, TValue>>(opCount);
		// Track a shadow live-set so Remove/Contains hit real entries roughly half the time.
		var live = new List<(TIndex, TValue)>();
		var liveSet = new HashSet<(TIndex, TValue)>();
		for (var i = 0; i < opCount; i++) {
			var pool = i < switchAt ? phase1 : phase2;
			var roll = rnd.Next(100);
			if (roll < 48 || live.Count == 0) {
				var p = pool[rnd.Next(pool.Length)];
				ops.Add(new OpRecord<TIndex, TValue>("Add", p.Index, p.Value));
				if (liveSet.Add((p.Index, p.Value))) live.Add((p.Index, p.Value));
			}
			else if (roll < 82) {
				(TIndex, TValue) t;
				if (rnd.Next(100) < 70) {
					var k = rnd.Next(live.Count);
					t = live[k];
					live[k] = live[^1];
					live.RemoveAt(live.Count - 1);
					liveSet.Remove(t);
				}
				else {
					var p = pool[rnd.Next(pool.Length)];
					t = (p.Index, p.Value);
					if (liveSet.Remove(t)) live.Remove(t);
				}
				ops.Add(new OpRecord<TIndex, TValue>("Remove", t.Item1, t.Item2));
			}
			else {
				(TIndex, TValue) t;
				if (rnd.Next(2) == 0 && live.Count > 0) t = live[rnd.Next(live.Count)];
				else { var p = pool[rnd.Next(pool.Length)]; t = (p.Index, p.Value); }
				ops.Add(new OpRecord<TIndex, TValue>("Contains", t.Item1, t.Item2));
			}
		}
		return ops;
	}

	private readonly record struct Failure(int Index, string Message);

	private static Failure? Replay<TIndex, TValue>(
		Func<ITreeUnderTest<TIndex, TValue>> factory,
		IReadOnlyList<OpRecord<TIndex, TValue>> ops,
		TIndex rangeMin,
		int rangeCheckEvery)
		where TIndex : IComparable<TIndex>
		where TValue : IEquatable<TValue> {
		using var tree = factory();
		var oracle = new HashSet<(TIndex, TValue)>();
		var sink = new List<(TIndex, TValue)>();

		for (var i = 0; i < ops.Count; i++) {
			var op = ops[i];
			var key = (op.Index, op.Value);
			switch (op.Op) {
				case "Add": {
					var expected = oracle.Add(key);
					var actual = tree.Add(op.Index, op.Value);
					if (actual != expected)
						return new Failure(i, $"op#{i} Add({op.Index}, {op.Value}) returned {actual}, expected {expected}");
					break;
				}
				case "Remove": {
					var expected = oracle.Remove(key);
					var actual = tree.Remove(op.Index, op.Value);
					if (actual != expected)
						return new Failure(i, $"op#{i} Remove({op.Index}, {op.Value}) returned {actual}, expected {expected}");
					break;
				}
				case "Contains": {
					var expected = oracle.Contains(key);
					var actual = tree.Contains(op.Index, op.Value);
					if (actual != expected)
						return new Failure(i, $"op#{i} Contains({op.Index}, {op.Value}) returned {actual}, expected {expected}");
					break;
				}
			}

			if (tree.Length != oracle.Count)
				return new Failure(i, $"op#{i} {op.Op}({op.Index}, {op.Value}): Length={tree.Length}, expected {oracle.Count}");

			if (rangeCheckEvery > 0 && (i % rangeCheckEvery == rangeCheckEvery - 1 || i == ops.Count - 1)) {
				var f = CheckRange(tree, oracle, rangeMin, sink, i, op);
				if (f is not null) return f;
			}
		}

		// Full-drain check: every live entry must be removable exactly once, and the tree
		// must end empty. This is where leaked / duplicated entries surface.
		var remaining = new List<(TIndex, TValue)>(oracle);
		for (var j = 0; j < remaining.Count; j++) {
			var e = remaining[j];
			if (!tree.Remove(e.Item1, e.Item2))
				return new Failure(ops.Count, $"drain: Remove({e.Item1}, {e.Item2}) returned false for a live entry");
			oracle.Remove(e);
			if (tree.Length != oracle.Count)
				return new Failure(ops.Count, $"drain after Remove({e.Item1}, {e.Item2}): Length={tree.Length}, expected {oracle.Count}");
		}
		if (tree.Length != 0) return new Failure(ops.Count, $"drain: Length={tree.Length}, expected 0");
		sink.Clear();
		tree.RangeFromInto(rangeMin, sink);
		if (sink.Count != 0)
			return new Failure(ops.Count, $"drain: RangeFrom returned {sink.Count} entries on an empty tree (first: {sink[0]})");
		return null;
	}

	private static Failure? CheckRange<TIndex, TValue>(
		ITreeUnderTest<TIndex, TValue> tree,
		HashSet<(TIndex, TValue)> oracle,
		TIndex rangeMin,
		List<(TIndex, TValue)> sink,
		int i,
		OpRecord<TIndex, TValue> op)
		where TIndex : IComparable<TIndex>
		where TValue : IEquatable<TValue> {
		sink.Clear();
		tree.RangeFromInto(rangeMin, sink);
		if (sink.Count != oracle.Count)
			return new Failure(i, $"op#{i} {op.Op}({op.Index}, {op.Value}): RangeFrom yielded {sink.Count} entries, expected {oracle.Count}");
		var seen = new HashSet<(TIndex, TValue)>();
		foreach (var e in sink) {
			if (!seen.Add(e))
				return new Failure(i, $"op#{i} {op.Op}({op.Index}, {op.Value}): RangeFrom yielded duplicate {e}");
			if (!oracle.Contains(e))
				return new Failure(i, $"op#{i} {op.Op}({op.Index}, {op.Value}): RangeFrom yielded unexpected {e}");
		}
		// Key ordering across the scan must be non-decreasing (value order within a run of
		// equal keys is deliberately unspecified and NOT asserted).
		for (var k = 1; k < sink.Count; k++) {
			if (sink[k - 1].Item1.CompareTo(sink[k].Item1) > 0)
				return new Failure(i, $"op#{i}: RangeFrom key order violated at {k}: {sink[k - 1].Item1} > {sink[k].Item1}");
		}
		return null;
	}

	private static List<OpRecord<TIndex, TValue>> Minimize<TIndex, TValue>(
		Func<ITreeUnderTest<TIndex, TValue>> factory,
		List<OpRecord<TIndex, TValue>> ops,
		TIndex rangeMin,
		int rangeCheckEvery,
		int failIndex)
		where TIndex : IComparable<TIndex>
		where TValue : IEquatable<TValue> {
		var current = ops.GetRange(0, Math.Min(failIndex + 1, ops.Count));
		if (Replay(factory, current, rangeMin, rangeCheckEvery) is null) current = new List<OpRecord<TIndex, TValue>>(ops);

		// ddmin-lite: try dropping shrinking chunks, then single ops. Capped so a huge
		// failing sequence still terminates in reasonable time.
		var chunk = Math.Max(1, current.Count / 2);
		var budget = 1500;
		while (chunk >= 1 && budget > 0) {
			var progressed = false;
			for (var start = 0; start + chunk <= current.Count && budget > 0; ) {
				var candidate = new List<OpRecord<TIndex, TValue>>(current.Count - chunk);
				candidate.AddRange(current.GetRange(0, start));
				candidate.AddRange(current.GetRange(start + chunk, current.Count - start - chunk));
				budget--;
				if (candidate.Count > 0 && Replay(factory, candidate, rangeMin, rangeCheckEvery) is not null) {
					current = candidate;
					progressed = true;
				}
				else start += chunk;
			}
			if (!progressed) chunk /= 2;
			if (current.Count > 400 && chunk == 0) break;
		}
		return current;
	}
}

// ───────────────────── Fixtures ─────────────────────

/// <summary>
///   Randomized differential tests: every config drives the production
///   <see cref="PooledBTree{TIndex,TValue}"/> against a HashSet oracle.
/// </summary>
[TestFixture]
public class PooledBTreeDifferentialTests {
	private const int OpCount = 20_000;
	private static readonly int[] Seeds = Enumerable.Range(1, 24).ToArray();

	// ── config 1: TValue = long, unique index keys ──
	private static (long, long)[] UniquePool() {
		var p = new (long, long)[4000];
		for (var i = 0; i < p.Length; i++) p[i] = (i, i * 7L);
		return p;
	}

	[Test]
	public void Config1_LongUniqueKeys([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, long>(() => new RealTree<long, long>(), UniquePool(), long.MinValue, seed, OpCount);

	// ── config 2: TValue = long, keys quantized into runs of ~1000 (batch-stamped shape) ──
	// 1000 equal keys / 64 per leaf ⇒ each run spans ~16 leaves.
	private static (long, long)[] RunPool() {
		var p = new (long, long)[6000];
		for (var i = 0; i < p.Length; i++) p[i] = (i / 1000, i * 31L);
		return p;
	}

	[Test]
	public void Config2_LongBatchStampedRuns([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, long>(() => new RealTree<long, long>(), RunPool(), long.MinValue, seed, OpCount);

	// ── config 3: TValue = string ──
	private static (long, string)[] StringPool() {
		var p = new (long, string)[4000];
		for (var i = 0; i < p.Length; i++) p[i] = (i / 500, "v-" + i.ToString("D5"));
		return p;
	}

	[Test]
	public void Config3_String([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, string>(() => new RealTree<long, string>(), StringPool(), long.MinValue, seed, OpCount);

	// ── config 4: TValue = (int, string) ValueTuple ──
	private static (long, (int, string))[] TuplePool() {
		var p = new (long, (int, string))[4000];
		for (var i = 0; i < p.Length; i++) p[i] = (i / 500, (i % 97, "t" + i));
		return p;
	}

	[Test]
	public void Config4_ValueTuple([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, (int, string)>(
			() => new RealTree<long, (int, string)>(), TuplePool(), long.MinValue, seed, OpCount);

	// ── config 4b: (int, string) where the string half is culture-ignorable ──
	// Comparer<(int,string)>.Default is culture-sensitive (ICU), so "a‍" and "a"
	// compare EQUAL while string.Equals says they differ. This is the real-world shape
	// of the "CompareTo inconsistent with Equals" hazard for a perfectly ordinary
	// ValueTuple<int,string> — no exotic user type required.
	private static readonly string[] Ignorables = { "", "‍", "­", "﻿", "​", "‌" };

	private static (long, (int, string))[] IgnorableTuplePool() {
		var p = new (long, (int, string))[6000];
		for (var i = 0; i < p.Length; i++)
			// Item1 is constant across each group of 6 so the culture-equal string half is
			// what actually decides the tuple comparison.
			p[i] = (i / 1000, ((i / 6) % 13, "t" + (i / 6) + Ignorables[i % 6]));
		return p;
	}

	[Test]
	public void Config4b_CultureIgnorableTuple([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, (int, string)>(
			() => new RealTree<long, (int, string)>(), IgnorableTuplePool(), long.MinValue, seed, OpCount);

	// ── config 5: forced TOTAL hash collision ──
	// 1500 equal keys per run, all values hash to 0 ⇒ every run is one collision run
	// spanning ~24 leaves. This is the collision-probe / cross-leaf RepairPath stressor.
	private static (long, ZeroHash)[] ZeroHashPool() {
		var p = new (long, ZeroHash)[6000];
		for (var i = 0; i < p.Length; i++) p[i] = (i / 1500, new ZeroHash(i));
		return p;
	}

	[Test]
	public void Config5_TotalHashCollision([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, ZeroHash>(
			() => new RealTree<long, ZeroHash>(), ZeroHashPool(), long.MinValue, seed, OpCount);

	/// <summary>Single-key variant: ONE key, 3000 total-collision values — maximal run.</summary>
	[Test]
	public void Config5b_SingleKeyTotalCollision([ValueSource(nameof(Seeds))] int seed) {
		var p = new (long, ZeroHash)[3000];
		for (var i = 0; i < p.Length; i++) p[i] = (42L, new ZeroHash(i));
		BTreeDifferential.Run<long, ZeroHash>(() => new RealTree<long, ZeroHash>(), p, long.MinValue, seed, OpCount);
	}

	// ── config 6: partial collisions (hash = Id % 4) ──
	private static (long, Mod4Hash)[] Mod4Pool() {
		var p = new (long, Mod4Hash)[6000];
		for (var i = 0; i < p.Length; i++) p[i] = (i / 1000, new Mod4Hash(i));
		return p;
	}

	[Test]
	public void Config6_PartialHashCollision([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, Mod4Hash>(
			() => new RealTree<long, Mod4Hash>(), Mod4Pool(), long.MinValue, seed, OpCount);

	// ── config 7: CompareTo inconsistent with Equals ──
	private static (long, Inconsistent)[] InconsistentPool() {
		var p = new (long, Inconsistent)[6000];
		// A repeats every 8 entries, so CompareTo == 0 happens constantly between
		// non-Equal values — exactly what a CompareTo-as-identity tree gets wrong.
		for (var i = 0; i < p.Length; i++) p[i] = (i / 1000, new Inconsistent(i % 8, i));
		return p;
	}

	[Test]
	public void Config7_InconsistentCompareTo([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, Inconsistent>(
			() => new RealTree<long, Inconsistent>(), InconsistentPool(), long.MinValue, seed, OpCount);

	/// <summary>
	///   Hand-minimized regression guard, no randomness: LeafCapacity + 1 entries that all
	///   share the same index key AND the same value hash. The 65th Add splits the leaf and
	///   the split separator is exactly (42, hash 0) — the same composite pair every entry
	///   has. A composite descent that routes composite-equal strictly one way, paired with
	///   a run scan that only walks forward, would strand half the collision run in the
	///   left leaf: present in RangeFrom but invisible to Contains/Remove.
	/// </summary>
	[Test]
	public void MinimalRepro_CollisionRunSplitAcrossLeaves() {
		using var tree = new PooledBTree<long, ZeroHash>();
		const int n = BTreeDifferential.LeafCapacity + 1; // 65
		for (var i = 0; i < n; i++) Assert.That(tree.Add(42L, new ZeroHash(i)), Is.True, $"Add #{i}");
		Assert.That(tree.Length, Is.EqualTo(n));

		var missing = new List<int>();
		for (var i = 0; i < n; i++)
			if (!tree.Contains(42L, new ZeroHash(i))) missing.Add(i);

		var sink = new List<(long, ZeroHash)>();
		var c = default(Collector);
		c.Sink = sink;
		tree.RangeFrom(long.MinValue, ref c);

		TestContext.Out.WriteLine($"Length={tree.Length} RangeFrom yielded {sink.Count}");
		TestContext.Out.WriteLine($"Contains() returned false for {missing.Count} of {n} live entries: " +
			string.Join(", ", missing.Take(80)));

		if (missing.Count > 0) {
			// The same blind spot corrupts Add's duplicate probe: re-adding an already-live
			// pair reports "inserted", so the tree silently holds two copies of it.
			var dup = new ZeroHash(missing[0]);
			var reAdded = tree.Add(42L, dup);
			TestContext.Out.WriteLine($"re-Add of already-live {dup} returned {reAdded} (expected False); Length={tree.Length}");
			var removedOnce = tree.Remove(42L, dup);
			var removedTwice = tree.Remove(42L, dup);
			TestContext.Out.WriteLine($"Remove x2 of {dup}: {removedOnce}, {removedTwice}; Length={tree.Length}");
		}

		Assert.That(missing, Is.Empty, "entries present in RangeFrom but invisible to Contains/Remove");
	}

	private struct Collector : PooledBTree<long, ZeroHash>.IResultAggregator {
		public List<(long, ZeroHash)> Sink;
		public void Add(long index, ZeroHash value) => Sink.Add((index, value));
		public void Dispose() { }
	}

	// ── config 8: TValue = (int, int) ValueTuple, unique index keys ──
	// The guard shape: a composite value with no key duplication at all, so the tree stays
	// on the key-only path and the tiebreak never fires.
	private static (long, (int, int))[] IntPairUniquePool() {
		var p = new (long, (int, int))[4000];
		for (var i = 0; i < p.Length; i++) p[i] = (i, (i, i * 7));
		return p;
	}

	[Test]
	public void Config8_IntPairTuple_UniqueKeys([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, (int, int)>(
			() => new RealTree<long, (int, int)>(), IntPairUniquePool(), long.MinValue, seed, OpCount);

	// ── config 9: TValue = (int, int), keys quantized into runs of ~1000 ──
	// 1000 equal keys / 64 per leaf ⇒ each run spans ~16 leaves, so the tiebreak decides
	// ordering inside a run that is split across many leaves.
	private static (long, (int, int))[] IntPairRunPool() {
		var p = new (long, (int, int))[6000];
		for (var i = 0; i < p.Length; i++) p[i] = (i / 1000, (i % 1000, i * 31));
		return p;
	}

	[Test]
	public void Config9_IntPairTuple_BatchStampedRuns([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, (int, int)>(
			() => new RealTree<long, (int, int)>(), IntPairRunPool(), long.MinValue, seed, OpCount);

	// ── config 10: composite (int, int) whose 64→32 hash fold collides densely ──
	// A ∈ [0,64), B ∈ [0,94) ⇒ 6000 distinct values sharing only 128 distinct hashes, so
	// the collision probe runs constantly on a value type that is otherwise ordinary
	// (Equals exact, CompareTo consistent with Equals).
	private static (long, IntPairKey)[] IntPairCollidingPool() {
		var p = new (long, IntPairKey)[6000];
		for (var i = 0; i < p.Length; i++) p[i] = (i / 1000, new IntPairKey(i % 64, i / 64));
		return p;
	}

	[Test]
	public void Config10_IntPairFoldCollision([ValueSource(nameof(Seeds))] int seed) =>
		BTreeDifferential.Run<long, IntPairKey>(
			() => new RealTree<long, IntPairKey>(), IntPairCollidingPool(), long.MinValue, seed, OpCount);

	// ── config 11: flag transition — strictly unique keys, then duplicates mid-life ──
	//
	// Phase 1 (SwitchAt ops) draws only from a pool where every index key appears with
	// exactly one value, so the tree can never hold two entries under one key and stays on
	// the key-only path. Phase 2 switches to a pool of 500-wide key runs, forcing the first
	// duplicate key and the flip to the composite path — while phase-1 entries are still
	// live and still get removed / probed by the shared live-set. The oracle is checked on
	// EVERY op, so agreement is asserted both before and after the transition; the
	// prefix-only run below pins the "before" half on its own.
	private const int SwitchAt = 8000;

	private static (long, (int, int))[] TransitionPhase1Pool() {
		var p = new (long, (int, int))[4000];
		for (var i = 0; i < p.Length; i++) p[i] = (i, (i, i * 3));
		return p;
	}

	private static (long, (int, int))[] TransitionPhase2Pool() {
		var p = new (long, (int, int))[6000];
		// Keys deliberately overlap phase 1's range so duplicates appear against entries that
		// were inserted while the tree was still unique-keyed.
		for (var i = 0; i < p.Length; i++) p[i] = (i / 500, (i % 500, i * 11 + 1));
		return p;
	}

	[Test]
	public void Config11_UniqueThenDuplicateKeys([ValueSource(nameof(Seeds))] int seed) {
		var ops = BTreeDifferential.GeneratePhasedOps(
			TransitionPhase1Pool(), TransitionPhase2Pool(), SwitchAt, seed, OpCount);
		// "before": the unique-key prefix on its own must already agree with the oracle.
		BTreeDifferential.RunOps<long, (int, int)>(
			() => new RealTree<long, (int, int)>(), ops.GetRange(0, SwitchAt), long.MinValue, seed);
		// "after": the same prefix followed by the duplicate-key tail, one tree, one lifetime.
		BTreeDifferential.RunOps<long, (int, int)>(
			() => new RealTree<long, (int, int)>(), ops, long.MinValue, seed);
	}

	/// <summary>
	///   The prefix must genuinely contain no duplicate index key — otherwise
	///   <see cref="Config11_UniqueThenDuplicateKeys"/> is not testing a transition at
	///   all — and the tail must genuinely introduce them.
	/// </summary>
	[Test]
	public void Config11_TransitionShapeIsWhatItClaims() {
		var ops = BTreeDifferential.GeneratePhasedOps(
			TransitionPhase1Pool(), TransitionPhase2Pool(), SwitchAt, seed: 1, opCount: OpCount);
		var live = new HashSet<(long, (int, int))>();
		var keys = new Dictionary<long, int>();
		var duplicatesBefore = 0;
		var duplicatesAfter = 0;
		for (var i = 0; i < ops.Count; i++) {
			var op = ops[i];
			switch (op.Op) {
				case "Add":
					if (live.Add((op.Index, op.Value))) {
						keys.TryGetValue(op.Index, out var n);
						keys[op.Index] = n + 1;
						if (n + 1 > 1) { if (i < SwitchAt) duplicatesBefore++; else duplicatesAfter++; }
					}
					break;
				case "Remove":
					if (live.Remove((op.Index, op.Value))) keys[op.Index]--;
					break;
			}
		}

		Assert.That(duplicatesBefore, Is.Zero, "phase 1 must hold strictly unique index keys");
		Assert.That(duplicatesAfter, Is.GreaterThan(1000), "phase 2 must actually introduce duplicate keys");
	}
}
