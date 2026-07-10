namespace Prague.Core.Collections;

	using System.Runtime.CompilerServices;
	using System.Runtime.InteropServices;

/// <summary>
///   Reclamation gate for single-writer / lock-free-reader pooled structures.
///   Readers pin around each scoped scan with two plain stores plus one local full
///   fence on a thread-owned, cache-line-padded slot — no atomic RMW, no shared-line
///   contention. Writers retiring pooled memory call <see cref="Retire" />: items park
///   in an open limbo batch sealed on every drain (one sealed batch outstanding);
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

	private static readonly Lock _limboLock = new();

	// Double-buffered batches: sealing swaps the lists, reclaiming clears them — no
	// List allocation per seal cycle. The snapshot capture arrays are reused and sized
	// to the whole slot registry so a single fill pass can never overflow (a
	// count-then-fill two-pass would race readers pinning between the passes).
	private static List<IRetirable> _open = new(32);
	private static List<IRetirable> _sealed = new(32);
	private static bool _hasSealed;
	private static Slot?[] _sealedSlots = [];
	private static int[] _sealedSeqs = [];
	private static int _sealedCount;

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
		// 1. Reclaim the sealed batch once its grace period has passed. No fence
		//    needed here: a stale "still pinned" read just defers to the next drain.
		if (_hasSealed && GracePassed()) {
			for (var i = 0; i < _sealed.Count; i++)
				_sealed[i].ReclaimToPool();
			_sealed.Clear();
			_hasSealed = false;
			Array.Clear(_sealedSlots, 0, _sealedCount);
			_sealedCount = 0;
		}

		// 2. Seal the open batch (one outstanding sealed batch at a time). Sealing costs
		//    a local fence plus a scan of the registered slots (~ns), so it runs on
		//    every drain — the quiescent common case reclaims immediately, keeping the
		//    pool round-trip tight for high-churn bucket create/dispose.
		if (!_hasSealed && _open.Count > 0) {
			SnapshotPins();
			if (_sealedCount == 0) {
				for (var i = 0; i < _open.Count; i++)
					_open[i].ReclaimToPool();
				_open.Clear();
				return;
			}

			// Swap the double buffers: _open becomes the sealed batch, the just-cleared
			// _sealed becomes the new open target.
			(_open, _sealed) = (_sealed, _open);
			_hasSealed = true;
		}
	}

	private static void SnapshotPins() {
		// Writer half of the store-buffer litmus: the unlink stores (before Retire)
		// must be visible before the pin loads below. Any reader whose pin this
		// snapshot misses has, by the paired reader fence, already observed the
		// unlinked state — it can never reach parked memory and needs no tracking.
		Interlocked.MemoryBarrier();

		var slots = Volatile.Read(ref _slots);
		// Registry-sized capture buffers: one pass, no overflow. A count-then-fill
		// two-pass would race a reader pinning between the passes — overflowing the
		// buffer, or (if it bailed early) dropping a pre-fence pin and silently
		// breaking the grace period.
		if (_sealedSlots.Length < slots.Length) {
			_sealedSlots = new Slot?[slots.Length];
			_sealedSeqs = new int[slots.Length];
		}

		var count = 0;
		for (var i = 0; i < slots.Length; i++) {
			var slot = slots[i];
			if (Volatile.Read(ref slot.Depth) > 0) {
				_sealedSlots[count] = slot;
				_sealedSeqs[count] = Volatile.Read(ref slot.Sequence);
				count++;
			}
		}

		_sealedCount = count;
	}

	private static bool GracePassed() {
		var slots = _sealedSlots;
		var seqs = _sealedSeqs;
		for (var i = 0; i < _sealedCount; i++) {
			var slot = slots[i]!;
			if (Volatile.Read(ref slot.Depth) > 0 && Volatile.Read(ref slot.Sequence) == seqs[i])
				return false;
		}

		return true;
	}
}
