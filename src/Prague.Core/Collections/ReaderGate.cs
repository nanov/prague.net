namespace Prague.Core.Collections;

	using System.Runtime.CompilerServices;
	using System.Runtime.InteropServices;

/// <summary>
///   Reclamation gate for single-writer / lock-free-reader pooled structures.
///   Readers pin around each scoped scan with two plain stores plus one local full
///   fence on a thread-owned, cache-line-padded slot — no atomic RMW, no shared-line
///   contention. Writers retiring pooled memory call <see cref="Retire" />: items park
///   in an open limbo batch that seals past a size/age threshold (or a forced drain);
///   sealing issues the writer half of the store-buffer litmus (a full fence, then a
///   snapshot of pinned slots' sequence numbers) — either the snapshot sees a reader's
///   pin, or that reader has already observed the unlink and can never reach parked
///   memory. A sealed batch is reclaimed once every stamped slot has unpinned at least
///   once (Depth == 0 or Sequence advanced) — the RCU grace-period argument. Slots are
///   recycled through a finalizer-backed free list, bounding gate memory by peak
///   concurrent reader threads.
/// </summary>
internal static class ReaderGate {
	/// <summary>Retired pooled memory awaiting a reader grace period.</summary>
	internal interface IRetirable {
		/// <summary>Returns the backing memory to its pool. Called exactly once.</summary>
		void ReclaimToPool();
	}

	[StructLayout(LayoutKind.Sequential)]
	internal sealed class Slot {
		// Padding isolates the hot fields on their own cache line: the object header +
		// _pad0 fill the line before Depth/Sequence, _pad1 fills the line after, so
		// writer-side snapshot reads never false-share with another thread's pins.
#pragma warning disable CS0169 // Field is never used
		private readonly Padding _pad0;
		public int Depth;
		public int Sequence;
		private readonly Padding _pad1;
#pragma warning restore CS0169 // Field is never used
		public Slot? NextFree;

		[StructLayout(LayoutKind.Explicit, Size = 56)]
		private readonly struct Padding { }
	}

	private sealed class SlotOwner {
		public readonly Slot Slot;

		public SlotOwner(Slot slot) {
			Slot = slot;
		}

		~SlotOwner() => ReturnSlot(Slot);
	}

	[ThreadStatic] private static SlotOwner? _owner;

	private static readonly Lock _registryLock = new();
	private static Slot[] _slots = [];
	private static Slot? _freeSlots;

	// Seal (and pay the process-wide barrier) only when the open batch is big or old
	// enough: per-message bucket churn retires constantly, and an unamortized barrier
	// (~µs) per retire would dwarf the pool round-trip it protects.
	private const int SealThreshold = 256;
	private const long SealAgeMs = 4;

	private static readonly Lock _limboLock = new();
	private static List<IRetirable> _open = new();
	private static long _openSince;
	private static List<IRetirable>? _sealed;
	private static Slot[]? _sealedSlots;
	private static int[]? _sealedSeqs;

	internal static int RegisteredSlotCount => Volatile.Read(ref _slots).Length;

	// ───────────────────── Reader side ─────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Slot Enter() {
		var owner = _owner ?? CreateOwner();
		var slot = owner.Slot;
		slot.Sequence++;
		Volatile.Write(ref slot.Depth, slot.Depth + 1);
		// Full fence: the pin store must be globally visible before any guarded data
		// load executes. Paired with the writer's fence in SnapshotPins this is the
		// store-buffer litmus — either the writer's snapshot sees the pin, or this
		// reader sees the writer's unlink; both are safe. A local fence (~1-2ns, no
		// shared-line contention) is used instead of relying on
		// Interlocked.MemoryBarrierProcessWide asymmetry: the process-wide flush is
		// membarrier/IPI-based on Linux but NOT reliably store-buffer-draining on
		// every platform (observed corruption on macOS/arm64).
		Interlocked.MemoryBarrier();
		return slot;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Exit(Slot slot) {
		// Release store: the guarded data loads complete before the unpin is visible.
		Volatile.Write(ref slot.Depth, slot.Depth - 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static SlotOwner CreateOwner() {
		var owner = new SlotOwner(RentSlot());
		_owner = owner;
		return owner;
	}

	// ───────────────────── Slot registry (cold) ─────────────────────

	private static Slot RentSlot() {
		lock (_registryLock) {
			var free = _freeSlots;
			if (free != null) {
				_freeSlots = free.NextFree;
				free.NextFree = null;
				return free;
			}

			var slot = new Slot();
			var old = _slots;
			var next = new Slot[old.Length + 1];
			Array.Copy(old, next, old.Length);
			next[old.Length] = slot;
			Volatile.Write(ref _slots, next);
			return slot;
		}
	}

	private static void ReturnSlot(Slot slot) {
		// A thread can only die unpinned (Exit runs on unwind). If an exception ever
		// escaped between Enter and Exit, the slot stays out of circulation — fail-open.
		if (Volatile.Read(ref slot.Depth) != 0)
			return;

		lock (_registryLock) {
			slot.NextFree = _freeSlots;
			_freeSlots = slot;
		}
	}

	// ───────────────────── Writer side (cold, batched) ─────────────────────

	public static void Retire(IRetirable item) {
		lock (_limboLock) {
			if (_open.Count == 0)
				_openSince = Environment.TickCount64;
			_open.Add(item);
			DrainLocked(force: false);
		}
	}

	/// <summary>Forced drain: seals regardless of thresholds. Dispose paths and tests.</summary>
	public static void TryDrain() {
		lock (_limboLock) {
			DrainLocked(force: true);
		}
	}

	private static void DrainLocked(bool force) {
		// 1. Reclaim the sealed batch once its grace period has passed. No barrier
		//    needed here: a stale "still pinned" read just defers to the next drain.
		if (_sealed != null && GracePassed(_sealedSlots!, _sealedSeqs!)) {
			for (var i = 0; i < _sealed.Count; i++)
				_sealed[i].ReclaimToPool();
			_sealed = null;
			_sealedSlots = null;
			_sealedSeqs = null;
		}

		// 2. Seal the open batch. One outstanding sealed batch at a time, and only when
		//    the batch is worth a barrier (size/age threshold, or a forced drain): the
		//    process-wide barrier is amortized over everything parked since the last
		//    seal. Unsealed items are merely reclaimed later — never unsafely.
		if (_sealed == null && _open.Count > 0
			&& (force || _open.Count >= SealThreshold || Environment.TickCount64 - _openSince >= SealAgeMs)) {
			var pinned = SnapshotPins(out var seqs);
			if (pinned.Length == 0) {
				for (var i = 0; i < _open.Count; i++)
					_open[i].ReclaimToPool();
				_open.Clear();
				return;
			}

			_sealed = _open;
			_sealedSlots = pinned;
			_sealedSeqs = seqs;
			_open = new List<IRetirable>();
		}
	}

	private static Slot[] SnapshotPins(out int[] seqs) {
		// Writer half of the store-buffer litmus: the unlink stores (before Retire)
		// must be visible before the pin loads below. Any reader whose pin this
		// snapshot misses has, by the paired reader fence, already observed the
		// unlinked state — it can never reach parked memory and needs no tracking.
		Interlocked.MemoryBarrier();

		var slots = Volatile.Read(ref _slots);
		List<Slot>? pinned = null;
		List<int>? pinnedSeqs = null;
		for (var i = 0; i < slots.Length; i++) {
			var slot = slots[i];
			if (Volatile.Read(ref slot.Depth) > 0) {
				pinned ??= new List<Slot>();
				pinnedSeqs ??= new List<int>();
				pinned.Add(slot);
				pinnedSeqs.Add(Volatile.Read(ref slot.Sequence));
			}
		}

		if (pinned == null) {
			seqs = [];
			return [];
		}

		seqs = pinnedSeqs!.ToArray();
		return pinned.ToArray();
	}

	private static bool GracePassed(Slot[] slots, int[] seqs) {
		for (var i = 0; i < slots.Length; i++) {
			var slot = slots[i];
			if (Volatile.Read(ref slot.Depth) > 0 && Volatile.Read(ref slot.Sequence) == seqs[i])
				return false;
		}

		return true;
	}
}
