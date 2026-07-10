namespace Prague.Core.Collections;

	using System.Runtime.CompilerServices;
	using System.Runtime.InteropServices;

/// <summary>
///   Reclamation gate for single-writer / lock-free-reader pooled structures.
///   Readers pin around each scoped scan on a thread-owned, cache-line-padded slot;
///   writers retiring pooled memory call <see cref="Retire" />: items park in an open
///   limbo batch sealed on every drain (one sealed batch outstanding); sealing issues
///   the writer half of the store-buffer litmus (a full fence, then a snapshot of the
///   pinned slots' pin words) — either the snapshot sees a reader's pin, or that
///   reader has already observed the unlink and can never reach parked memory. A
///   sealed batch is reclaimed once every stamped slot has fully unpinned at least
///   once (depth reached 0, or the outermost-section sequence advanced) — the RCU
///   grace-period argument. Slots recycle through a finalizer-backed free list,
///   bounding gate memory by peak concurrent reader threads.
///
///   Performance notes:
///   - Reader pin word: depth (low 32) and sequence (high 32) packed into one long,
///     written only by the owning thread. Enter is one TLS load, one predicted
///     branch, one L1 load and one full-fenced Interlocked.Exchange (the fence is the
///     litmus requirement; the exchange doubles as the store). Exit is one release
///     store. No atomic RMW ever touches a line shared between threads.
///   - Allocations: zero steady-state everywhere. Per-thread: one Slot (padded, ~192 B,
///     recycled via free list) + one SlotOwner (finalizer anchor). Writer side: the two
///     batch lists swap and Clear (never reallocate), and the snapshot buffers grow
///     once to the registry size. Registry growth is +1 per new peak thread —
///     deliberate, it runs once per thread lifetime.
///   - Dispatch: static class, sealed Slot, no virtual calls on the reader path. The
///     IRetirable.ReclaimToPool interface call in the drain loop is left to dynamic
///     PGO's guarded devirtualization (receivers are generic node/table types, so a
///     static type test chain is impossible here).
/// </summary>
internal static class ReaderGate {
	/// <summary>Retired pooled memory awaiting a reader grace period.</summary>
	internal interface IRetirable {
		/// <summary>Returns the backing memory to its pool. Called exactly once.</summary>
		void ReclaimToPool();
	}

	private const long DepthOne = 1L;
	private const long SeqOne = 1L << 32;

	[StructLayout(LayoutKind.Sequential)]
	internal sealed class Slot {
		// Padding isolates the pin word on its own cache line: the object header +
		// _pad0 fill the line before PinState, _pad1 fills the line after, so
		// writer-side snapshot reads never false-share with another thread's pins.
#pragma warning disable CS0169 // Field is never used
		private readonly Padding _pad0;
		// depth (low 32) | sequence (high 32). Owner-thread writes only; the writer
		// side reads it with a single volatile load, so depth and sequence are always
		// observed as a consistent pair.
		public long PinState;
		private readonly Padding _pad1;
#pragma warning restore CS0169 // Field is never used
		public Slot? NextFree;

		[StructLayout(LayoutKind.Explicit, Size = 56)]
		private readonly struct Padding { }
	}

	// The thread-death detector: .NET has no exit callback for arbitrary threads, so a
	// finalizable anchor held ONLY by a thread-static root becomes collectible exactly
	// when its thread dies, and its finalizer recycles the slot. The finalizer cannot
	// live on Slot itself — slots are permanently rooted by the _slots registry (the
	// writer sweep needs them), so a Slot is never collectible.
	private sealed class SlotOwner {
		public readonly Slot Slot;

		public SlotOwner(Slot slot) {
			Slot = slot;
		}

		~SlotOwner() => ReturnSlot(Slot);
	}

	// _slot is the fast-path cache (one TLS load on Enter). _owner is WRITE-ONLY and
	// LOAD-BEARING: it is the thread-static root that keeps the SlotOwner alive for
	// the thread's lifetime. Delete it and the owner is collected on the next GC
	// while the thread still runs — the finalizer recycles the slot early, another
	// thread rents it, and two threads share one pin word (silent use-after-reclaim).
	[ThreadStatic] private static Slot? _slot;
	[ThreadStatic] private static SlotOwner? _owner;

	private static readonly Lock _registryLock = new();
	private static Slot[] _slots = [];
	private static Slot? _freeSlots;

	private static readonly Lock _limboLock = new();

	// Double-buffered batches: sealing swaps the lists, reclaiming clears them — no
	// List allocation per seal cycle. The snapshot capture buffers are reused and
	// sized to the whole slot registry so a single fill pass can never overflow (a
	// count-then-fill two-pass would race readers pinning between the passes).
	private static List<IRetirable> _open = new(32);
	private static List<IRetirable> _sealed = new(32);
	private static Slot?[] _sealedSlots = [];
	private static long[] _sealedStates = [];
	private static int _sealedCount;

	internal static int RegisteredSlotCount => Volatile.Read(ref _slots).Length;

	// ───────────────────── Reader side ─────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Slot Enter() {
		var slot = _slot ?? CreateSlot();
		var state = slot.PinState; // owner-only writes: a plain read is exact
		// Only a NEW OUTERMOST critical section advances the sequence. A nested
		// re-enter must not: a sequence advance tells the writer that the pin it
		// snapshotted has fully exited (see GracePassed), and a nested cycle inside a
		// still-held outer pin has not.
		var next = state + ((int)state == 0 ? SeqOne + DepthOne : DepthOne);
		// Full-fenced publish: the pin must be globally visible before any guarded
		// data load executes — the reader half of the store-buffer litmus, paired
		// with the writer fence in SnapshotPins. (A plain process-wide-barrier
		// asymmetry was rejected: Interlocked.MemoryBarrierProcessWide is not a
		// reliable remote store-buffer drain on every platform — observed corruption
		// on macOS/arm64.) The exchange doubles as the store, on a line no other
		// thread ever writes.
		Interlocked.Exchange(ref slot.PinState, next);
		return slot;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Exit(Slot slot) {
		// Release store: the guarded data loads complete before the unpin is visible.
		Volatile.Write(ref slot.PinState, slot.PinState - DepthOne);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Slot CreateSlot() {
		var owner = new SlotOwner(RentSlot());
		_owner = owner;
		_slot = owner.Slot;
		return owner.Slot;
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
		if ((int)Volatile.Read(ref slot.PinState) != 0)
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
		if (_sealedCount > 0 && GracePassed()) {
			for (var i = 0; i < _sealed.Count; i++)
				_sealed[i].ReclaimToPool();
			_sealed.Clear();
			Array.Clear(_sealedSlots, 0, _sealedCount);
			_sealedCount = 0;
		}

		// 2. Seal the open batch (one outstanding sealed batch at a time). Sealing
		//    costs a local fence plus a sweep of the registered slots (~ns), so it
		//    runs on every drain — the quiescent common case reclaims immediately,
		//    keeping the pool round-trip tight for high-churn bucket create/dispose.
		if (_sealedCount == 0 && _open.Count > 0) {
			SnapshotPins();
			if (_sealedCount == 0) {
				for (var i = 0; i < _open.Count; i++)
					_open[i].ReclaimToPool();
				_open.Clear();
				return;
			}

			// Swap the double buffers: _open becomes the sealed batch, the
			// just-cleared _sealed becomes the new open target.
			(_open, _sealed) = (_sealed, _open);
		}
	}

	private static void SnapshotPins() {
		// Writer half of the store-buffer litmus: the unlink stores (before Retire)
		// must be visible before the pin loads below. Any reader whose pin this
		// snapshot misses has, by the paired reader fence, already observed the
		// unlinked state — it can never reach parked memory and needs no tracking.
		Interlocked.MemoryBarrier();

		var slots = Volatile.Read(ref _slots);
		if (_sealedSlots.Length < slots.Length) {
			_sealedSlots = new Slot?[slots.Length];
			_sealedStates = new long[slots.Length];
		}

		var count = 0;
		ref var slotsRef = ref MemoryMarshal.GetArrayDataReference(slots);
		ref var capSlots = ref MemoryMarshal.GetArrayDataReference(_sealedSlots);
		ref var capStates = ref MemoryMarshal.GetArrayDataReference(_sealedStates);
		for (var i = 0; i < slots.Length; i++) {
			var slot = Unsafe.Add(ref slotsRef, i);
			var state = Volatile.Read(ref slot.PinState); // depth+sequence, one atomic pair
			if ((int)state == 0)
				continue;

			Unsafe.Add(ref capSlots, count) = slot;
			Unsafe.Add(ref capStates, count) = state;
			count++;
		}

		_sealedCount = count;
	}

	private static bool GracePassed() {
		ref var capSlots = ref MemoryMarshal.GetArrayDataReference(_sealedSlots);
		ref var capStates = ref MemoryMarshal.GetArrayDataReference(_sealedStates);
		for (var i = 0; i < _sealedCount; i++) {
			var current = Volatile.Read(ref Unsafe.Add(ref capSlots, i)!.PinState);
			// Blocked only while the SAME outermost critical section that was pinned
			// at seal time is still open: still pinned and sequence unchanged. A
			// sequence advance means that section fully exited; the current pin
			// started after the seal fence and cannot reach parked memory.
			if ((int)current != 0 && current >> 32 == Unsafe.Add(ref capStates, i) >> 32)
				return false;
		}

		return true;
	}
}
