namespace Prague.Core.Tests.DataStructures;

using Prague.Core.Collections;

// PooledBTree.EstimateCount*: the frozen pipeline's cardinality signal for range steps. Exact when
// both bounds land in one leaf, within 2× of the truth on wider windows — uniform keys, duplicate
// runs, and after deletes have skewed leaf occupancy. Never used to size a buffer, so the pins are
// on the ratio, not on the exact value.
[TestFixture]
public class PooledBTreeEstimateCountTests {
	private const int N = 20_000;

	private static PooledBTree<int, long> Uniform() {
		var tree = new PooledBTree<int, long>();
		for (var i = 0; i < N; i++)
			tree.Add(i, i);
		return tree;
	}

	private static PooledBTree<int, long> Shuffled() {
		var tree = new PooledBTree<int, long>();
		var rng = new Random(7);
		var keys = new int[N];
		for (var i = 0; i < N; i++) keys[i] = i;
		for (var i = N - 1; i > 0; i--) {
			var j = rng.Next(i + 1);
			(keys[i], keys[j]) = (keys[j], keys[i]);
		}

		for (var i = 0; i < N; i++)
			tree.Add(keys[i], keys[i]);
		return tree;
	}

	private static int Exact(PooledBTree<int, long> tree, int from, bool fromInclusive, int to, bool toInclusive) {
		var agg = new Collect();
		tree.RangeCustom(from, to, fromInclusive, toInclusive, ref agg);
		return agg.Count;
	}

	private struct Collect : PooledBTree<int, long>.IResultAggregator {
		public int Count;
		public void Add(int index, long value) => Count++;
		public void Dispose() { }
	}

	private static void AssertWithin2X(int estimate, int exact, string label) {
		Assert.That(estimate, Is.GreaterThanOrEqualTo(exact / 2), label + " estimate too low");
		Assert.That(estimate, Is.LessThanOrEqualTo(Math.Max(exact * 2, 1)), label + " estimate too high");
	}

	[Test]
	public void EmptyTree_IsZero() {
		using var tree = new PooledBTree<int, long>();
		Assert.Multiple(() => {
			Assert.That(tree.EstimateCount(0, true, 100, true), Is.EqualTo(0));
			Assert.That(tree.EstimateCountFrom(0, true), Is.EqualTo(0));
			Assert.That(tree.EstimateCountTo(0, true), Is.EqualTo(0));
		});
	}

	[Test]
	public void SingleKeyWindows_AreExact_UniqueKeys() {
		using var tree = Uniform();
		Assert.Multiple(() => {
			Assert.That(tree.EstimateCount(42, true, 42, true), Is.EqualTo(1), "[42, 42]");
			Assert.That(tree.EstimateCount(42, false, 42, true), Is.EqualTo(0), "(42, 42]");
			Assert.That(tree.EstimateCount(42, true, 42, false), Is.EqualTo(0), "[42, 42)");
			Assert.That(tree.EstimateCount(-5, true, -1, true), Is.EqualTo(0), "below the range");
			Assert.That(tree.EstimateCount(N, true, N + 5, true), Is.EqualTo(0), "above the range");
			Assert.That(tree.EstimateCountFrom(N - 1, true), Is.EqualTo(1), "[max, +inf)");
			Assert.That(tree.EstimateCountFrom(N - 1, false), Is.EqualTo(0), "(max, +inf)");
			Assert.That(tree.EstimateCountTo(0, true), Is.EqualTo(1), "(-inf, min]");
			Assert.That(tree.EstimateCountTo(0, false), Is.EqualTo(0), "(-inf, min)");
			Assert.That(tree.EstimateCountFrom(int.MinValue, true), Is.EqualTo(N), "everything from below");
			Assert.That(tree.EstimateCountTo(int.MaxValue, true), Is.EqualTo(N), "everything to above");
		});
	}

	// Windows that fit in one leaf (leaves hold up to 64 keys) are exact; a 5-key window is exact
	// whenever it does not straddle a leaf boundary, and off by at most one leaf's share when it does.
	[Test]
	public void SmallWindows_AreExactInsideOneLeaf_AndWithinOneLeafAcross() {
		using var tree = Uniform();
		var exactHits = 0;
		for (var from = 0; from < N - 5; from += 37) {
			var exact = Exact(tree, from, true, from + 4, true);
			var estimate = tree.EstimateCount(from, true, from + 4, true);
			Assert.That(exact, Is.EqualTo(5));
			Assert.That(Math.Abs(estimate - exact), Is.LessThanOrEqualTo(64), $"[{from}, {from + 4}]");
			if (estimate == exact) exactHits++;
		}

		Assert.That(exactHits, Is.GreaterThan(N / 37 / 2), "most 5-key windows land inside one leaf and are exact");
	}

	[TestCase(true, true)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(false, false)]
	public void WideWindows_Within2X_UniformAndShuffled(bool fromInclusive, bool toInclusive) {
		foreach (var tree in new[] { Uniform(), Shuffled() })
			using (tree) {
				foreach (var (from, to) in new[] { (0, 199), (1000, 1999), (5, N - 5), (N / 2, N / 2 + 300), (N - 700, N + 100), (-100, 400) }) {
					var exact = Exact(tree, from, fromInclusive, to, toInclusive);
					AssertWithin2X(tree.EstimateCount(from, fromInclusive, to, toInclusive), exact, $"[{from}, {to}]");
					AssertWithin2X(tree.EstimateCountFrom(from, fromInclusive), Exact(tree, from, fromInclusive, int.MaxValue, true), $"[{from}, +inf)");
					AssertWithin2X(tree.EstimateCountTo(to, toInclusive), Exact(tree, int.MinValue, true, to, toInclusive), $"(-inf, {to}]");
				}

				Assert.That(tree.EstimateCount(100, true, 50, true), Is.EqualTo(0), "inverted window");
			}
	}

	// Ten equal keys per value: the run of an excluded bound spans leaves, and the estimate must
	// still track the exact walk.
	[Test]
	public void DuplicateRuns_Within2X_AndExactForOneRunInsideALeaf() {
		using var tree = new PooledBTree<int, long>();
		for (var i = 0; i < N; i++)
			tree.Add(i / 10, i);
		foreach (var (from, to) in new[] { (0, 19), (100, 129), (500, 1000), (1990, 2100) }) {
			foreach (var (fi, ti) in new[] { (true, true), (false, true), (true, false), (false, false) })
				AssertWithin2X(tree.EstimateCount(from, fi, to, ti), Exact(tree, from, fi, to, ti), $"dup [{from}, {to}] {fi}/{ti}");
		}

		// A single run is 10 keys; every estimate of it is either exact (one leaf) or bounded by a leaf.
		for (var k = 0; k < N / 10; k += 13)
			Assert.That(Math.Abs(tree.EstimateCount(k, true, k, true) - 10), Is.LessThanOrEqualTo(64), "run " + k);
	}

	[Test]
	public void AfterDeletes_Within2X() {
		using var tree = Shuffled();
		for (var i = 0; i < N; i += 2)
			Assert.That(tree.Remove(i, i), Is.True);
		for (var i = 1; i < N / 2; i += 2)
			Assert.That(tree.Remove(i, i), Is.True);
		foreach (var (from, to) in new[] { (0, 999), (1000, 4999), (N / 2, N), (N / 4, 3 * N / 4), (-1, N + 1) }) {
			var exact = Exact(tree, from, true, to, true);
			AssertWithin2X(tree.EstimateCount(from, true, to, true), exact, $"deleted [{from}, {to}]");
		}

		Assert.That(tree.EstimateCount(11, true, 11, true), Is.EqualTo(0), "a deleted key");
		Assert.That(tree.EstimateCount(N - 1, true, N - 1, true), Is.EqualTo(1), "a surviving key");
	}

	[Test]
	public void ReferenceKeys_Work() {
		using var tree = new PooledBTree<string, long>();
		for (var i = 0; i < 2000; i++)
			tree.Add(i.ToString("D6"), i);
		Assert.Multiple(() => {
			Assert.That(tree.EstimateCount("000010", true, "000010", true), Is.EqualTo(1));
			Assert.That(tree.EstimateCount("000100", true, "000199", true), Is.InRange(50, 200));
			Assert.That(tree.EstimateCountTo("000499", true), Is.InRange(250, 1000));
		});
	}
}
