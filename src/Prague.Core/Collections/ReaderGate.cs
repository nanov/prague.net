namespace Prague.Core.Collections;

	using System.Runtime.CompilerServices;
	using System.Runtime.InteropServices;

/// <summary>
///   Asymmetric reclamation gate for single-writer / lock-free-reader pooled structures.
///   Readers pin around each scoped scan with two plain stores to a thread-owned,
///   cache-line-padded slot — zero atomic operations, zero fences on the reader side.
///   Writers retiring pooled memory call <see cref="Retire" />: after an
///   <see cref="Interlocked.MemoryBarrierProcessWide" /> (which makes every in-flight
///   reader pin visible and guarantees later readers observe the unlink), memory is
///   reclaimed immediately when no pin is held, or parked in a limbo batch stamped with
///   the pinned slots' sequence numbers. A batch is reclaimed once every stamped slot
///   has unpinned at least once (Depth == 0 or Sequence advanced) — the RCU
///   grace-period argument: a pin taken after the barrier starts from the structure's
///   root and can no longer reach the unlinked memory, so it never blocks reclamation.
///   Slots are recycled through a finalizer-backed free list, bounding gate memory by
///   peak concurrent reader threads.
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
		private readonly Padding _pad0;
		public int Depth;
		public int Sequence;
		private readonly Padding _pad1;
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

	private static readonly Lock _limboLock = new();
	private static List<IRetirable> _open = new();
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
		// The volatile write/read pair is compiler ordering only (free on x64): the JIT
		// must not sink the pin store below, nor hoist the guarded data loads above,
		// this point. Hardware store-load reordering (the pin store parked in a store
		// buffer while data loads execute) is closed by the writer's process-wide
		// barrier in SnapshotPins.
		Volatile.Write(ref slot.Depth, slot.Depth + 1);
		_ = Volatile.Read(ref slot.Depth);
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
			_open.Add(item);
			DrainLocked();
		}
	}

	public static void TryDrain() {
		lock (_limboLock) {
			DrainLocked();
		}
	}

	private static void DrainLocked() {
		// 1. Reclaim the sealed batch once its grace period has passed. No barrier
		//    needed here: a stale "still pinned" read just defers to the next drain.
		if (_sealed != null && GracePassed(_sealedSlots!, _sealedSeqs!)) {
			for (var i = 0; i < _sealed.Count; i++)
				_sealed[i].ReclaimToPool();
			_sealed = null;
			_sealedSlots = null;
			_sealedSeqs = null;
		}

		// 2. Seal the open batch. One outstanding sealed batch at a time: each
		//    process-wide barrier is amortized over everything parked during the
		//    previous grace period.
		if (_sealed == null && _open.Count > 0) {
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
		// Serialize every core: pins still sitting in a store buffer become visible,
		// and any pin taken after this point observes the already-unlinked structures,
		// so it can never reach parked memory and needs no tracking.
		Interlocked.MemoryBarrierProcessWide();

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
