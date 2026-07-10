namespace Prague.Core.Collections;

	using System.Buffers;
	using System.Collections;
	using System.Runtime.CompilerServices;
	using System.Runtime.InteropServices;
	using Prague.Core.Utils;

/// <summary>
///   Freelist-chained hash set used as an index bucket. Single writer, lock-free readers.
///   Reader safety model:
///   - All reader-visible state lives in one <see cref="Tables" /> generation published
///     with a single volatile store — a reader can never mix old arrays with new bounds.
///   - Arrays stay ArrayPool-rented. A generation carries a pin count (escaping readers:
///     the enumerators handed out by GetValues) plus Retired/Returned bits; the last
///     unpin of a retired generation hands the arrays to <see cref="ReaderGate" />,
///     whose grace period covers the scoped readers (Contains, ValueSet.IntersectWith).
///   - Slot publication is ordered (Next/Value → volatile HashCode → LastIndex/bucket
///     head) and slots removed/reused bump a per-slot version first, so a reader's
///     copy-out is never torn. The version guard is compiled only for multi-word value
///     types; atomically-copyable T (reference types, small structs — the common key
///     shapes) keeps the plain read loop.
///   Readers may observe a STALE view (recently added/removed entries, a chain walk
///   wandering after remove+reuse — bounded by the cycle guard) — the documented
///   staleness model.
/// </summary>
internal sealed class PooledSet<T, TKeyComparer> : IReadOnlyCollection<T>, IEnumerable<T>, IEnumerable, IDisposable
	where TKeyComparer : struct, IKeyComparer<T> {
	// JIT-folded per instantiation: a T copy can never tear when T is a reference type
	// or a small struct without references.
	private static readonly bool AtomicCopy =
		!typeof(T).IsValueType
		|| (Unsafe.SizeOf<T>() <= 8 && !RuntimeHelpers.IsReferenceOrContainsReferences<T>());

	internal sealed class Tables : ReaderGate.IRetirable {
		private const int RetiredBit = 1 << 30;
		private const int ReturnedBit = int.MinValue;

		public readonly int[] Buckets; // rented; valid range [0, Size)
		public readonly HashSlot<T>[] Slots; // rented; valid range [0, Size)

		// Free-list links live OUTSIDE the slots: reusing slot.Next for the free list
		// would send an in-flight chain reader parked on a just-removed slot into the
		// free list (all dead slots) and make it miss the live tail of its chain.
		public readonly int[] FreeNext; // rented

		// Per-slot mutation counter, bumped BEFORE remove/reuse; null when AtomicCopy.
		public readonly int[]? Versions; // rented

		public readonly int Size;
		public readonly ulong FastModMultiplier;

		// Writer-mutated; published volatile AFTER a new slot's HashCode so a reader
		// never scans a not-yet-published slot.
		public int LastIndex;

		private int _state; // pins (bits 0..29) | Retired (bit 30) | Returned (bit 31)

		public Tables(int size, int lastIndex) {
			Size = size;
			FastModMultiplier = HashHelpers.GetFastModMultiplier((uint)size);
			Buckets = ArrayPool<int>.Shared.Rent(size);
			Slots = ArrayPool<HashSlot<T>>.Shared.Rent(size);
			FreeNext = ArrayPool<int>.Shared.Rent(size);
			Array.Clear(Buckets, 0, size);
			if (!AtomicCopy) {
				Versions = ArrayPool<int>.Shared.Rent(size);
				Array.Clear(Versions, 0, size);
			}

			LastIndex = lastIndex;
			_state = 1; // the owning PooledSet's implicit pin
		}

		public bool IsRetired => (Volatile.Read(ref _state) & RetiredBit) != 0;

		/// <summary>
		///   Escaping-reader pin (enumerators). Increment-then-check: when the writer
		///   already retired this generation, the backoff touches only this GC-managed
		///   object, never the (possibly reclaimed) arrays.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryPin() {
			var s = Interlocked.Increment(ref _state);
			if ((s & RetiredBit) == 0)
				return true;

			Unpin();
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Unpin() {
			var s = Interlocked.Decrement(ref _state);
			if (s == RetiredBit)
				HandOffToGate();
		}

		/// <summary>Writer-side, idempotent: marks retired and releases the owner pin.</summary>
		public void Retire() {
			var prev = Interlocked.Or(ref _state, RetiredBit);
			if ((prev & RetiredBit) != 0)
				return;

			Unpin();
		}

		private void HandOffToGate() {
			// Exactly-once: only the Retired|0 → Retired|Returned transition parks.
			if (Interlocked.CompareExchange(ref _state, RetiredBit | ReturnedBit, RetiredBit) != RetiredBit)
				return;

			// Escaping pins are gone; scoped readers (Contains, IntersectWith) may still
			// be inside a gate-protected scan — the gate's grace period covers them.
			ReaderGate.Retire(this);
		}

		public void ReclaimToPool() {
			ArrayPool<int>.Shared.Return(Buckets);
			ArrayPool<HashSlot<T>>.Shared.Return(Slots, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
			ArrayPool<int>.Shared.Return(FreeNext);
			if (Versions != null)
				ArrayPool<int>.Shared.Return(Versions);
		}
	}

	public ref struct Enumerator {
		private Tables? _tables;
		private readonly HashSlot<T>[] _slots;
		private readonly int[]? _versions;
		private readonly int _lastIndex;
		private T _currentValue;
		private int _currentHashCode;
		private int _index;

		public T Current {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _currentValue;
		}

		public int CurrentHashCode {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _currentHashCode;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Enumerator(Tables? tables, int lastIndex) {
			_tables = tables;
			_slots = tables?.Slots ?? [];
			_versions = tables?.Versions;
			_lastIndex = lastIndex;
			_currentValue = default!;
			_currentHashCode = 0;
			_index = -1;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool MoveNext() {
			ref var start = ref MemoryMarshal.GetArrayDataReference(_slots);
			while (++_index < _lastIndex) {
				ref var slot = ref Unsafe.Add(ref start, _index);
				if (AtomicCopy) {
					var hashCode = Volatile.Read(ref slot.HashCode);
					if (hashCode < 0)
						continue;

					// Atomic copy: stale-or-new, never torn. Current/CurrentHashCode
					// must not re-read the live slot later.
					_currentValue = slot.Value;
					_currentHashCode = hashCode;
					return true;
				}

				// Version-guarded copy-out: the writer bumps the slot version before
				// every remove/reuse, so a torn Value copy is rejected even when the
				// slot is re-added with an equal HashCode (ABA).
				var version = Volatile.Read(ref _versions![_index]);
				var guardedHashCode = Volatile.Read(ref slot.HashCode);
				if (guardedHashCode < 0)
					continue;

				var value = slot.Value;
				if (Volatile.Read(ref _versions[_index]) != version)
					continue;

				_currentValue = value;
				_currentHashCode = guardedHashCode;
				return true;
			}

			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Dispose() {
			var tables = _tables;
			if (tables == null)
				return;

			_tables = null;
			tables.Unpin();
		}
	}

	private sealed class BoxedEnumerator : IEnumerator<T>, IEnumerator, IDisposable {
		private Tables? _tables;
		private readonly HashSlot<T>[] _slots;
		private readonly int[]? _versions;
		private readonly int _lastIndex;
		private T _currentValue;
		private int _index;

		public T Current => _currentValue;

		object? IEnumerator.Current => Current;

		internal BoxedEnumerator(Tables? tables, int lastIndex) {
			_tables = tables;
			_slots = tables?.Slots ?? [];
			_versions = tables?.Versions;
			_lastIndex = lastIndex;
			_currentValue = default!;
			_index = -1;
		}

		public bool MoveNext() {
			while (++_index < _lastIndex) {
				if (AtomicCopy) {
					var hashCode = Volatile.Read(ref _slots[_index].HashCode);
					if (hashCode < 0)
						continue;

					_currentValue = _slots[_index].Value;
					return true;
				}

				// Same version-guarded copy-out as the struct enumerator.
				var version = Volatile.Read(ref _versions![_index]);
				var guardedHashCode = Volatile.Read(ref _slots[_index].HashCode);
				if (guardedHashCode < 0)
					continue;

				var value = _slots[_index].Value;
				if (Volatile.Read(ref _versions[_index]) != version)
					continue;

				_currentValue = value;
				return true;
			}

			return false;
		}

		public void Reset() {
			_index = -1;
		}

		public void Dispose() {
			DisposeCore();
			GC.SuppressFinalize(this);
		}

		// Abandoned enumerators (never disposed) release their pin on finalization so
		// the generation's arrays still return to the pool instead of relying on GC.
		~BoxedEnumerator() => DisposeCore();

		private void DisposeCore() {
			var tables = Interlocked.Exchange(ref _tables, null);
			tables?.Unpin();
		}
	}

	private const int DefaultCapacity = 127;

	private readonly bool _clearOnFree = RuntimeHelpers.IsReferenceOrContainsReferences<T>();

	private readonly TKeyComparer _comparer;

	private Tables _tables;

	private int _count;

	private int _freeList;

	public static readonly PooledSet<T, TKeyComparer> Empty = new();

	public int Count => _count;

	public bool IsEmpty => _count == 0;

	public PooledSet() : this(default) { }

	public PooledSet(TKeyComparer comparer) {
		_tables = new Tables(DefaultCapacity, 0);
		_freeList = -1;
		_comparer = comparer;
	}

	/// <summary>
	///   Consistent (slots, versions, lastIndex) triple for gate-protected bulk readers.
	///   Reading through separate properties could straddle a Grow and go out of bounds.
	///   Callers MUST hold a ReaderGate pin for the whole time they touch the arrays.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void GetSnapshot(out HashSlot<T>[] slots, out int[]? versions, out int lastIndex) {
		var tables = Volatile.Read(ref _tables);
		slots = tables.Slots;
		versions = tables.Versions;
		// Acquire read: LastIndex is published AFTER a new slot's HashCode — a plain
		// load could expose a not-yet-published slot as live.
		lastIndex = Volatile.Read(ref tables.LastIndex);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Add(T item) {
		var hashCode = GetHashCode(item);
		var tables = _tables;
		var bucket = GetBucket(tables, hashCode);
		ref var bucketsRef = ref MemoryMarshal.GetArrayDataReference(tables.Buckets);
		ref var slotsRef = ref MemoryMarshal.GetArrayDataReference(tables.Slots);
		var i = Unsafe.Add(ref bucketsRef, bucket) - 1;
		while (i >= 0) {
			ref var slot = ref Unsafe.Add(ref slotsRef, i);
			if (slot.HashCode == hashCode && Equals(slot.Value, item))
				return false;

			i = slot.Next;
		}

		int idx;
		var fromFreeList = _freeList >= 0;
		if (fromFreeList) {
			idx = _freeList;
			_freeList = tables.FreeNext[idx];
		}
		else {
			if (tables.LastIndex == tables.Size) {
				tables = Grow();
				bucket = GetBucket(tables, hashCode);
				bucketsRef = ref MemoryMarshal.GetArrayDataReference(tables.Buckets);
				slotsRef = ref MemoryMarshal.GetArrayDataReference(tables.Slots);
			}

			idx = tables.LastIndex;
		}

		ref var newSlot = ref Unsafe.Add(ref slotsRef, idx);
		// Bump the slot version BEFORE mutating: a reader copying the slot out accepts
		// it only when the version stayed unchanged around the copy.
		if (!AtomicCopy)
			Volatile.Write(ref tables.Versions![idx], tables.Versions[idx] + 1);
		// Publish order matters for lock-free readers: Next/Value first, then HashCode
		// (readers treat HashCode >= 0 as "live"), and only then reachability — via
		// LastIndex for enumerators, via the bucket head for chain lookups.
		newSlot.Next = Unsafe.Add(ref bucketsRef, bucket) - 1;
		newSlot.Value = item;
		Volatile.Write(ref newSlot.HashCode, hashCode);
		if (!fromFreeList)
			Volatile.Write(ref tables.LastIndex, idx + 1);
		Volatile.Write(ref Unsafe.Add(ref bucketsRef, bucket), idx + 1);
		_count++;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Remove(T item) {
		var hashCode = GetHashCode(item);
		var tables = _tables;
		var bucket = GetBucket(tables, hashCode);
		ref var bucketsRef = ref MemoryMarshal.GetArrayDataReference(tables.Buckets);
		ref var slotsRef = ref MemoryMarshal.GetArrayDataReference(tables.Slots);
		var prev = -1;
		var i = Unsafe.Add(ref bucketsRef, bucket) - 1;
		while (i >= 0) {
			ref var slot = ref Unsafe.Add(ref slotsRef, i);
			if (slot.HashCode == hashCode && Equals(slot.Value, item)) {
				if (prev < 0)
					Volatile.Write(ref Unsafe.Add(ref bucketsRef, bucket), slot.Next + 1);
				else
					Unsafe.Add(ref slotsRef, prev).Next = slot.Next;

				// Bump the slot version BEFORE mutating (see Add).
				if (!AtomicCopy)
					Volatile.Write(ref tables.Versions![i], tables.Versions[i] + 1);
				Volatile.Write(ref slot.HashCode, -1);
				// slot.Next stays intact until reuse: a chain reader parked on this
				// slot can still reach the live tail of its chain. The free-list link
				// lives in Tables.FreeNext.
				tables.FreeNext[i] = _freeList;
				if (_clearOnFree)
					slot.Value = default!;
				_count--;
				if (_count == 0) {
					Volatile.Write(ref tables.LastIndex, 0);
					_freeList = -1;
				}
				else {
					_freeList = i;
				}

				return true;
			}

			prev = i;
			i = slot.Next;
		}

		return false;
	}

	public bool Contains(T item) {
		var hashCode = GetHashCode(item);
		var slot = ReaderGate.Enter();
		try {
			return ContainsCore(item, hashCode);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	internal bool ContainsWithHashCode(T item, int hashCode) {
		var slot = ReaderGate.Enter();
		try {
			return ContainsCore(item, hashCode);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	// NoInlining: keeps the chain walk out of the gated wrapper's EH region, which
	// would otherwise pessimize the whole method's codegen.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool ContainsCore(T item, int hashCode) {
		var tables = Volatile.Read(ref _tables);
		var bucket = GetBucket(tables, hashCode);
		ref var bucketsRef = ref MemoryMarshal.GetArrayDataReference(tables.Buckets);
		ref var slotsRef = ref MemoryMarshal.GetArrayDataReference(tables.Slots);
		var i = Unsafe.Add(ref bucketsRef, bucket) - 1;
		// Bounded walk: a chain read concurrently with remove+reuse can transiently
		// wander; the guard turns a would-be infinite loop into a stale miss.
		var remaining = tables.Size;
		while ((uint)i < (uint)tables.Size && remaining-- > 0) {
			ref var slot = ref Unsafe.Add(ref slotsRef, i);
			if (slot.HashCode == hashCode && Equals(slot.Value, item))
				return true;

			i = slot.Next;
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Enumerator GetEnumerator() {
		while (true) {
			var tables = Volatile.Read(ref _tables);
			if (tables.TryPin())
				return new Enumerator(tables, Volatile.Read(ref tables.LastIndex));

			// _tables unchanged after a failed pin ⇒ the set is disposed: enumerate nothing.
			if (ReferenceEquals(Volatile.Read(ref _tables), tables))
				return new Enumerator(null, 0);
		}
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator() => CreateBoxedEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => CreateBoxedEnumerator();

	private BoxedEnumerator CreateBoxedEnumerator() {
		while (true) {
			var tables = Volatile.Read(ref _tables);
			if (tables.TryPin())
				return new BoxedEnumerator(tables, Volatile.Read(ref tables.LastIndex));

			if (ReferenceEquals(Volatile.Read(ref _tables), tables))
				return new BoxedEnumerator(null, 0);
		}
	}

	public void Dispose() {
		if (ReferenceEquals(this, Empty))
			return; // the shared Empty sentinel must never retire its generation

		_tables.Retire();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetHashCode(T item) => _comparer.GetHashCode(item) & 0x7FFFFFFF;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool Equals(T a, T b) => _comparer.Equals(a, b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetBucket(Tables tables, int hashCode) {
		return (int)HashHelpers.FastMod((uint)hashCode, (uint)tables.Size, tables.FastModMultiplier);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private Tables Grow() {
		var oldTables = _tables;
		var newTables = new Tables(HashHelpers.ExpandPrime(_count), oldTables.LastIndex);
		if (oldTables.LastIndex > 0)
			Array.Copy(oldTables.Slots, newTables.Slots, oldTables.LastIndex);

		ref var slotsRef = ref MemoryMarshal.GetArrayDataReference(newTables.Slots);
		ref var bucketsRef = ref MemoryMarshal.GetArrayDataReference(newTables.Buckets);
		for (var i = 0; i < newTables.LastIndex; i++) {
			ref var slot = ref Unsafe.Add(ref slotsRef, i);
			if (slot.HashCode >= 0) {
				var bucket = (int)HashHelpers.FastMod((uint)slot.HashCode, (uint)newTables.Size,
					newTables.FastModMultiplier);
				slot.Next = Unsafe.Add(ref bucketsRef, bucket) - 1;
				Unsafe.Add(ref bucketsRef, bucket) = i + 1;
			}
		}

		// Single atomic publish: readers capture either the fully built new generation
		// or the still-intact old one, never a mix.
		Volatile.Write(ref _tables, newTables);
		// Retire the old generation: its arrays return to the pool once the last
		// escaping pin releases and the scoped-reader grace period passes. (This also
		// fixes the pre-fix leak where grow-rented arrays were never returned at all.)
		oldTables.Retire();
		return newTables;
	}
}
