namespace Prague.Core.Tests.DataStructures;

	using Prague.Core.Collections;
	using NUnit.Framework;

[TestFixture]
[NonParallelizable] // the gate is process-global; these tests reason about pin state
public class ReaderGateTests {
	private sealed class TrackedRetirable : ReaderGate.IRetirable {
		private int _reclaims;

		public int Reclaims => Volatile.Read(ref _reclaims);

		public void ReclaimToPool() => Interlocked.Increment(ref _reclaims);
	}

	[SetUp]
	public void FlushLimbo() {
		// Drain anything a previous test (or fixture) left parked so counts start clean.
		ReaderGate.TryDrain();
		ReaderGate.TryDrain();
	}

	[Test]
	public void Retire_NoPinnedReaders_ReclaimsOnDrain() {
		var item = new TrackedRetirable();
		ReaderGate.Retire(item);
		ReaderGate.TryDrain();
		Assert.That(item.Reclaims, Is.EqualTo(1));
	}

	[Test]
	public void Retire_WhilePinned_DefersUntilUnpin() {
		var item = new TrackedRetirable();
		var slot = ReaderGate.Enter();
		try {
			ReaderGate.Retire(item);
			Assert.That(item.Reclaims, Is.EqualTo(0), "must not reclaim under an active pin");
			ReaderGate.TryDrain();
			Assert.That(item.Reclaims, Is.EqualTo(0), "grace has not passed while still pinned");
		}
		finally {
			ReaderGate.Exit(slot);
		}

		ReaderGate.TryDrain();
		Assert.That(item.Reclaims, Is.EqualTo(1));
	}

	[Test]
	public void Retire_ReaderRepinned_SequenceAdvanceCountsAsGrace() {
		var item = new TrackedRetirable();
		var slot = ReaderGate.Enter();
		ReaderGate.Retire(item);
		ReaderGate.TryDrain(); // seal the batch against the currently-pinned slot
		Assert.That(item.Reclaims, Is.EqualTo(0));
		ReaderGate.Exit(slot);

		// Re-pin before the drain: the CURRENT pin postdates the seal barrier, so it
		// cannot reach the parked item — sequence advance proves the grace period.
		var slot2 = ReaderGate.Enter();
		try {
			ReaderGate.TryDrain();
			Assert.That(item.Reclaims, Is.EqualTo(1));
		}
		finally {
			ReaderGate.Exit(slot2);
		}
	}

	[Test]
	public void Retire_ManyItems_EachReclaimedExactlyOnce() {
		var items = new TrackedRetirable[64];
		for (var i = 0; i < items.Length; i++)
			items[i] = new TrackedRetirable();

		var slot = ReaderGate.Enter();
		for (var i = 0; i < 32; i++)
			ReaderGate.Retire(items[i]);
		ReaderGate.Exit(slot);

		for (var i = 32; i < 64; i++)
			ReaderGate.Retire(items[i]);
		ReaderGate.TryDrain();
		ReaderGate.TryDrain(); // two drains: reclaim sealed, then seal+reclaim the rest

		for (var i = 0; i < items.Length; i++)
			Assert.That(items[i].Reclaims, Is.EqualTo(1), $"item {i}");
	}

	[Test]
	public void NestedEnter_SingleThread_OuterExitReleases() {
		var item = new TrackedRetirable();
		var outer = ReaderGate.Enter();
		var inner = ReaderGate.Enter();
		ReaderGate.Retire(item);
		ReaderGate.Exit(inner);
		ReaderGate.TryDrain();
		Assert.That(item.Reclaims, Is.EqualTo(0), "outer pin still active");
		ReaderGate.Exit(outer);
		ReaderGate.TryDrain();
		Assert.That(item.Reclaims, Is.EqualTo(1));
	}

	[Test]
	public void NestedEnter_AfterSeal_DoesNotCountAsGrace() {
		// A batch sealed against the OUTER pin must not be reclaimed just because the
		// same thread opened and closed a NESTED critical section: the outer pin still
		// holds pre-seal references. Only a full unpin (depth reaching 0) is grace.
		var item = new TrackedRetirable();
		var outer = ReaderGate.Enter();
		ReaderGate.Retire(item);
		ReaderGate.TryDrain(); // seal against the outer pin
		Assert.That(item.Reclaims, Is.EqualTo(0));

		var inner = ReaderGate.Enter(); // nested cycle while outer is held
		ReaderGate.Exit(inner);
		ReaderGate.TryDrain();
		Assert.That(item.Reclaims, Is.EqualTo(0),
			"a nested enter/exit must not advance the grace sequence — the outer pin is still live");

		ReaderGate.Exit(outer);
		ReaderGate.TryDrain();
		Assert.That(item.Reclaims, Is.EqualTo(1));
	}

	[Test]
	public void SlotRecycling_DeadThreads_DoNotGrowRegistryUnboundedly() {
		// warm-up: current thread's slot
		ReaderGate.Exit(ReaderGate.Enter());
		var before = ReaderGate.RegisteredSlotCount;

		for (var i = 0; i < 16; i++) {
			var t = new Thread(static () => ReaderGate.Exit(ReaderGate.Enter()));
			t.Start();
			t.Join();
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		Assert.That(ReaderGate.RegisteredSlotCount, Is.LessThanOrEqualTo(before + 3),
			"dead threads' slots must be recycled through the free list, not accumulated");
	}
}
