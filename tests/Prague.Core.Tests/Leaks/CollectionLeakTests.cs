namespace Prague.Core.Tests.Leaks;

using System.Runtime.CompilerServices;
using Prague.Core.Collections;
using Prague.Core.Tests.Infrastructure;
using NUnit.Framework;

// Every scenario runs under the process-wide tracking pools (installed by the global
// LeakTrackingSetup fixture) and must come back to zero new outstanding arrays and
// zero double-/foreign returns. LeakAssert.Balanced runs each scenario twice: the
// first pass warms intentionally-never-returned statics (PooledSet<T>.Empty, the
// disposed-sentinel generation), the second is the measured one.
[TestFixture]
[NonParallelizable]
public class CollectionLeakTests {
	[Test]
	public void TrackingPools_AreInstalled() =>
		Assert.That(PragueArrayPool<int>.Pool, Is.SameAs(TrackingArrayPool<int>.Instance),
			"tracking provider must be latched before any pooled type is first touched");

	// ── PooledBTree ──────────────────────────────────────────────────────────

	[Test]
	public void PooledBTree_ChurnSplitsMergesUpdate_Dispose_Balanced() =>
		LeakAssert.Balanced(static () => {
			var tree = new PooledBTree<long, long>();
			// Duplicate-key runs spanning leaves (composite mode) + unique tail.
			for (var i = 0L; i < 20_000; i++)
				tree.Add(i >> 6, i);
			for (var i = 0L; i < 20_000; i += 2)
				tree.Remove(i >> 6, i);
			for (var i = 1L; i < 4_000; i += 2)
				tree.Update(i >> 6, (i >> 6) + 1_000_000, i);
			tree.Dispose();
		});

	[Test]
	public void PooledBTree_RemoveEverything_EmptyLeafCollapse_Balanced() =>
		LeakAssert.Balanced(static () => {
			var tree = new PooledBTree<long, long>();
			for (var i = 0L; i < 5_000; i++)
				tree.Add(i, i);
			for (var i = 0L; i < 5_000; i++)
				tree.Remove(i, i);
			tree.Dispose();
		});

	[Test]
	public void PooledBTree_DoubleDispose_NoDoubleReturn() =>
		LeakAssert.Balanced(static () => {
			var tree = new PooledBTree<long, long>();
			for (var i = 0L; i < 2_000; i++)
				tree.Add(i >> 4, i);
			tree.Dispose();
			tree.Dispose();
		});

	// ── PooledSet ────────────────────────────────────────────────────────────

	[Test]
	public void PooledSet_GrowChurn_Dispose_Balanced() =>
		LeakAssert.Balanced(static () => {
			var set = new PooledSet<int, DefaultKeyComparer<int>>();
			for (var i = 0; i < 10_000; i++)
				set.Add(i);
			for (var i = 0; i < 10_000; i += 2)
				set.Remove(i);
			foreach (var _ in set) {
			}

			set.Dispose();
			set.Dispose();
		});

	[Test]
	public void PooledSet_AbandonedBoxedEnumerator_FinalizerBackstop_Balanced() =>
		LeakAssert.Balanced(static () => {
			var set = new PooledSet<int, DefaultKeyComparer<int>>();
			for (var i = 0; i < 5_000; i++)
				set.Add(i);
			AbandonBoxedEnumerator(set);
			set.Dispose();
		});

	// Keep the undisposed boxed enumerator's lifetime confined to a non-inlined frame
	// so the finalizer can run during LeakAssert's quiesce.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AbandonBoxedEnumerator(PooledSet<int, DefaultKeyComparer<int>> set) {
		var enumerator = ((IEnumerable<int>)set).GetEnumerator();
		enumerator.MoveNext();
	}

	// ── SortedArraySet ───────────────────────────────────────────────────────

	[Test]
	public void SortedArraySet_GrowPastPooledSegment_Dispose_Balanced() =>
		LeakAssert.Balanced(static () => {
			using var set = new SortedArraySet<int>();
			for (var i = 0; i < 300; i++)
				set.Add(i);
			foreach (var _ in set) {
			}
		});

	[Test]
	public void SortedArraySet_DisposeWhileEnumeratorAlive_RefCountDefersReturn_Balanced() =>
		LeakAssert.Balanced(static () => {
			var set = new SortedArraySet<int>();
			for (var i = 0; i < 100; i++)
				set.Add(i);
			var enumerator = set.GetEnumerator();
			enumerator.MoveNext();
			set.Dispose();
			enumerator.Dispose();
		});

	[Test]
	public void SortedArraySet_EnumeratorDoubleDispose_DoesNotDoubleRelease() =>
		LeakAssert.Balanced(static () => {
			var set = new SortedArraySet<int>();
			for (var i = 0; i < 100; i++)
				set.Add(i);
			var enumerator = set.GetEnumerator();
			enumerator.MoveNext();
			enumerator.Dispose();
			enumerator.Dispose(); // must be a no-op, not a second refcount decrement
			Assert.That(set.Contains(50), Is.True, "set must still be usable");

			// The double-dispose above (when unguarded) already drives the refcount to
			// zero and returns the array to the pool while the set is still live. A
			// second acquire/dispose cycle re-increments that corrupted refcount and
			// hands out the very same, already-returned array; disposing it drives the
			// refcount to zero a second time, forcing a real double-return of one array
			// instance — that's what TrackingArrayPool flags as a violation.
			var enumerator2 = set.GetEnumerator();
			enumerator2.MoveNext();
			enumerator2.Dispose();
			set.Dispose();
		});

	// ── ValueSet ─────────────────────────────────────────────────────────────

	[Test]
	public void ValueSet_GrowChurn_Dispose_Balanced() =>
		LeakAssert.Balanced(static () => {
			var set = new ValueSet<int, DefaultKeyComparer<int>>();
			for (var i = 0; i < 5_000; i++)
				set.Add(i);
			set.Dispose();
			set.Dispose();
		});

	[Test]
	public void ValueSet_LargeIntersect_HappyPath_Balanced() =>
		LeakAssert.Balanced(static () => {
			var set = new ValueSet<int, DefaultKeyComparer<int>>();
			for (var i = 0; i < 5_000; i++)
				set.Add(i);
			set.IntersectWith(Enumerable.Range(0, 2_500));
			set.Dispose();
		});

	[Test]
	public void ValueSet_LargeIntersect_ThrowingEnumerable_DoesNotLeakBitmap() =>
		LeakAssert.Balanced(static () => {
			var set = new ValueSet<int, DefaultKeyComparer<int>>();
			// > 3200 live slots so the BitHelper bitmap spills past the stackalloc
			// threshold and is pool-rented.
			for (var i = 0; i < 5_000; i++)
				set.Add(i);
			try {
				set.IntersectWith(new ThrowingEnumerable<int>(Enumerable.Range(0, 5_000), 100));
				Assert.Fail("hostile enumerable must throw");
			} catch (InvalidOperationException) {
			}

			set.Dispose();
		});

	// ── IncrementalIntersecter ───────────────────────────────────────────────

	[Test]
	public void IncrementalIntersecter_DoubleDispose_NoDoubleReturn() =>
		LeakAssert.Balanced(static () => {
			var set = new ValueSet<int, DefaultKeyComparer<int>>();
			for (var i = 0; i < 100; i++)
				set.Add(i);
			// Empty helper buffer forces the rent path regardless of set size.
			var intersecter = new ValueSet<int, DefaultKeyComparer<int>>.IncrementalIntersecter(ref set, []);
			intersecter.IntersectWith(1);
			intersecter.Dispose(false);
			intersecter.Dispose(false);
			set.Dispose();
		});

	// ── ValueDictionary ──────────────────────────────────────────────────────

	[Test]
	public void ValueDictionary_PooledLifecycle_Balanced() =>
		LeakAssert.Balanced(static () => {
			var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(true, 512);
			for (var i = 0; i < 512; i++)
				dict.Add(i, i.ToString());
			dict.Dispose(true);
			dict.Dispose(true);
		});

	[Test]
	public void ValueDictionary_NoArgDisposeThenWithValues_NoDoubleReturn() =>
		LeakAssert.Balanced(static () => {
			var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(true, 64);
			for (var i = 0; i < 64; i++)
				dict.Add(i, i.ToString());
			dict.Dispose(true);
			dict.Dispose();
		});
}
