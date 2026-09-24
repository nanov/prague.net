namespace Prague.Core.Tests.DataStructures;

using NUnit.Framework;
using Prague.Core.Collections;

/// <summary>
///   <c>CopyKeysTo</c> walks one generation's slots in order under a single gate pin. A key the writer
///   removes and re-adds during that walk is re-inserted into whatever slot the free list hands out, and
///   the free list is LIFO — so when another key was freed after it, or the free list is empty, the
///   re-add lands in a slot the walk has not reached yet and the same key is delivered twice. That is
///   the staleness model's one duplicate shape, and the pipeline's flat seed buffer must not inherit it.
///   The sink here plays the writer from inside the walk, which makes the interleaving exact.
/// </summary>
[TestFixture]
public class PooledSetCopyKeysChurnTests {
	private const int N = 40;

	private static PooledSet<int, DefaultKeyComparer<int>> Fill() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();
		for (var i = 0; i < N; i++)
			set.Add(i);
		return set;
	}

	/// <summary>Removes key 0 and key 30 when it sees key 0, then re-adds key 0: it takes slot 30, ahead of the walk.</summary>
	private readonly struct ReAddAheadOfTheWalk(PooledSet<int, DefaultKeyComparer<int>> set, List<int> seen) : IKeySink<int> {
		public void Add(int key) {
			seen.Add(key);
			if (key != 0)
				return;
			set.Remove(0);
			set.Remove(30);
			set.Add(0);
		}
	}

	[Test]
	public void CopyKeysTo_AReAddDuringTheWalk_DeliversTheKeyTwice_AndReportsTheWalkDirty() {
		var set = Fill();
		var seen = new List<int>();
		var sink = new ReAddAheadOfTheWalk(set, seen);

		var clean = set.CopyKeysTo(ref sink);

		Assert.Multiple(() => {
			Assert.That(seen.Count(k => k == 0), Is.EqualTo(2), "the model: a re-added key is walked at both its old and its new slot");
			Assert.That(clean, Is.False, "a walk the writer mutated under must say so");
			Assert.That(set.Count, Is.EqualTo(N - 1));
		});
	}

	private readonly struct Collect(List<int> seen) : IKeySink<int> {
		public void Add(int key) => seen.Add(key);
	}

	[Test]
	public void CopyKeysTo_WithNoWriter_ReportsTheWalkClean() {
		var set = Fill();
		var seen = new List<int>();
		var sink = new Collect(seen);

		var clean = set.CopyKeysTo(ref sink);

		Assert.Multiple(() => {
			Assert.That(clean, Is.True);
			Assert.That(seen, Is.EquivalentTo(Enumerable.Range(0, N)));
		});
	}

	/// <summary>A real writer thread moving keys out and back in; the copied keys may repeat, but never when the walk was reported clean.</summary>
	[Test]
	public void CopyKeysTo_UnderAChurningWriter_NeverReportsADuplicateWalkClean() {
		var set = Fill();
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => {
			var i = 0;
			while (!stop.IsCancellationRequested) {
				var a = i % N;
				var b = (i * 7 + 3) % N;
				set.Remove(a);
				set.Remove(b);
				set.Add(a);
				set.Add(b);
				i++;
			}
		});

		var seen = new List<int>(N * 2);
		var dirtyWalks = 0;
		var deadline = Environment.TickCount64 + 300;
		while (Environment.TickCount64 < deadline) {
			seen.Clear();
			var sink = new Collect(seen);
			var clean = set.CopyKeysTo(ref sink);
			var distinct = seen.Distinct().Count();
			if (!clean)
				dirtyWalks++;
			if (distinct != seen.Count)
				Assert.That(clean, Is.False, "a walk that delivered a key twice must not be reported clean");
		}

		stop.Cancel();
		writer.Wait();
		TestContext.Out.WriteLine($"dirty walks: {dirtyWalks}");
	}
}
