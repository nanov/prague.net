namespace Prague.Kafka.SerDe;

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

/// <summary>
///   Lock-free weak string intern pool keyed by the UTF-8 bytes on the wire.
///
///   Layout: an open-addressing (linear probing) table of 64-bit slots packing <c>hash &lt;&lt; 32 | handleId + 1</c>,
///   plus an append-only chunked store of weak <see cref="GCHandle"/>s indexed by <c>handleId</c>. No key bytes
///   are stored and there is no per-entry object: a hit hashes the bytes, probes, dereferences the handle and
///   compares the live string against the bytes (<see cref="Ascii.Equals(ReadOnlySpan{byte}, ReadOnlySpan{char})"/>
///   fast path) — no decode, no allocation, no write. Every interned string is held weakly: it lives exactly as
///   long as something else (typically a cached entity) roots it.
///
///   Nothing takes a lock:
///   <list type="bullet">
///     <item>A miss decodes once and claims the first empty slot of its probe chain with one CAS — the packed
///       word publishes hash and handle together. A thread that loses the CAS to the same content adopts the
///       winner's instance.</item>
///     <item>Once filled, a slot never changes. Growth is a rebuild run by whichever thread wins one CAS on a
///       flag; nobody waits for it. The rebuild seals every empty slot of the old table with
///       <see cref="Moved"/>, so it sees every entry; an insert that meets the seal returns its string
///       un-interned instead of waiting.</item>
///     <item>Handles are never freed while the pool lives — readers on a stale table may still dereference
///       them. A rebuild collects the handles of collected strings into an immutable free array, handed out
///       through an <see cref="Interlocked"/> cursor (no ABA); a lost insert parks its handle in a single
///       exchange-based spare.</item>
///   </list>
///   Every handle a reader dereferences is re-checked by content, so a race can only cost a dedupe, never
///   return a wrong string.
/// </summary>
internal sealed class Utf8WeakInternPool : IDisposable {
	private const int MinCapacity = 1024;
	private const int ChunkShift = 12;
	private const int ChunkSize = 1 << ChunkShift;
	private const int MaxChunks = 1 << 12; // 16M handles
	private const int StackEncodeLimit = 1024;
	private const long Moved = -1; // seals an empty slot of a table being rebuilt; never a valid packed slot

	private readonly nint[]?[] _handleChunks = new nint[]?[MaxChunks];
	private int _nextHandleId;
	private int _spareHandleId = -1;
	private int _rebuilding;
	private Table _table = new(new long[MinCapacity], 0, []);

	/// <summary>Occupied slots, live or collected — diagnostics and tests.</summary>
	internal int Count => Volatile.Read(ref _table).Used;

	public string GetOrAdd(ReadOnlySpan<byte> utf8) {
		var hash = Hash(utf8);
		var table = Volatile.Read(ref _table);
		var slots = table.Slots;
		var mask = slots.Length - 1;
		for (var i = hash & mask; ; i = (i + 1) & mask) {
			var slot = Volatile.Read(ref slots[i]);
			if (slot is 0 or Moved)
				return Insert(table, utf8, hash);
			if ((int)(slot >> 32) == hash && Target(slot) is { } candidate && Utf8Equals(utf8, candidate))
				return candidate;
		}
	}

	public void Dispose() {
		var allocated = Math.Min(Volatile.Read(ref _nextHandleId), MaxChunks * ChunkSize);
		for (var id = 0; id < allocated; id++) {
			var handle = _handleChunks[id >> ChunkShift]![id & (ChunkSize - 1)];
			if (handle != 0)
				GCHandle.FromIntPtr(handle).Free();
		}
		Array.Clear(_handleChunks);
	}

	/// <summary>
	///   Miss path: walks the probe chain from its head and publishes <paramref name="utf8"/>'s string into the
	///   first collected slot (re-targeting its handle in place — no new handle, no growth) or, failing that,
	///   the first empty slot. A concurrent insert of the same content wins and is adopted.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private string Insert(Table table, ReadOnlySpan<byte> utf8, int hash) {
		var value = Encoding.UTF8.GetString(utf8);
		var slots = table.Slots;
		var mask = slots.Length - 1;
		var id = -1;
		for (int i = hash & mask, probes = 0; probes < slots.Length; probes++) {
			var current = Volatile.Read(ref slots[i]);
			if (current == Moved)
				break; // a rebuild is sealing this table; don't wait for it

			if (current == 0) {
				if (id < 0 && (id = AcquireHandle(table, value)) < 0)
					return value; // handle store exhausted: correct, just not interned
				current = Interlocked.CompareExchange(ref slots[i], ((long)hash << 32) | (uint)(id + 1), 0);
				if (current == 0) {
					if (Interlocked.Increment(ref table.Used) * 2 > slots.Length)
						TryRebuild(table);
					return value;
				}
				continue; // lost the slot: re-examine the winner
			}

			var target = Target(current);
			if (target is null) {
				// Collected: swap in our hash, then point the slot's own handle at our string.
				var reusedId = (int)(uint)current - 1;
				if (Interlocked.CompareExchange(ref slots[i], ((long)hash << 32) | (uint)(reusedId + 1), current) == current) {
					var reused = GCHandle.FromIntPtr(HandleAt(reusedId));
					reused.Target = value;
					if (id >= 0)
						ReleaseHandle(id);
					return value;
				}
				continue; // slot changed under us: re-examine it
			}

			if ((int)(current >> 32) == hash && Utf8Equals(utf8, target)) {
				if (id >= 0)
					ReleaseHandle(id);
				return target;
			}

			i = (i + 1) & mask;
		}

		if (id >= 0)
			ReleaseHandle(id);
		return value;
	}

	private int AcquireHandle(Table table, string value) {
		var id = Interlocked.Exchange(ref _spareHandleId, -1);
		if (id < 0 && Volatile.Read(ref table.FreeCursor) < table.FreeHandleIds.Length) {
			var k = Interlocked.Increment(ref table.FreeCursor) - 1;
			if (k < table.FreeHandleIds.Length)
				id = table.FreeHandleIds[k];
		}

		if (id >= 0) {
			var recycled = GCHandle.FromIntPtr(HandleAt(id));
			recycled.Target = value;
			return id;
		}

		id = Interlocked.Increment(ref _nextHandleId) - 1;
		if (id >= MaxChunks * ChunkSize)
			return -1;

		var chunk = Volatile.Read(ref _handleChunks[id >> ChunkShift]);
		if (chunk is null) {
			var created = new nint[ChunkSize];
			chunk = Interlocked.CompareExchange(ref _handleChunks[id >> ChunkShift], created, null) ?? created;
		}

		// Published to readers by the slot CAS that follows (full fence).
		chunk[id & (ChunkSize - 1)] = GCHandle.ToIntPtr(GCHandle.Alloc(value, GCHandleType.Weak));
		return id;
	}

	/// <summary>Parks an unpublished handle for the next insert; a displaced earlier spare is dropped (rare double race).</summary>
	private void ReleaseHandle(int id) {
		var handle = GCHandle.FromIntPtr(HandleAt(id));
		handle.Target = null;
		Interlocked.Exchange(ref _spareHandleId, id);
	}

	/// <summary>
	///   Moves live slots into a table sized for them. Run by one thread at a time (CAS on a flag, losers return);
	///   seals each empty old slot with <see cref="Moved"/> so no entry can land behind the copy.
	/// </summary>
	private void TryRebuild(Table old) {
		if (Interlocked.CompareExchange(ref _rebuilding, 1, 0) != 0)
			return;
		try {
			if (!ReferenceEquals(Volatile.Read(ref _table), old))
				return;

			var oldSlots = old.Slots;
			var liveIds = new List<long>(old.Used);
			var freeIds = new List<int>();
			for (var i = 0; i < oldSlots.Length; i++) {
				// Seal empty and collected slots so no insert can land (or re-target a handle) behind the copy.
				while (true) {
					var slot = Volatile.Read(ref oldSlots[i]);
					if (slot == Moved)
						break;
					if (slot != 0 && Target(slot) is not null) {
						liveIds.Add(slot);
						break;
					}
					if (Interlocked.CompareExchange(ref oldSlots[i], Moved, slot) == slot) {
						if (slot != 0)
							freeIds.Add((int)(uint)slot - 1);
						break;
					}
				}
			}

			// Unclaimed free handles of the old table carry over; closing its cursor first hands each out once.
			var taken = Interlocked.Exchange(ref old.FreeCursor, int.MaxValue / 2);
			for (var k = taken; k < old.FreeHandleIds.Length; k++)
				freeIds.Add(old.FreeHandleIds[k]);

			var slots = new long[Math.Max(MinCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)(liveIds.Count + 1) * 4))];
			var mask = slots.Length - 1;
			foreach (var slot in liveIds) {
				var i = (int)(slot >> 32) & mask;
				while (slots[i] != 0)
					i = (i + 1) & mask;
				slots[i] = slot;
			}

			Volatile.Write(ref _table, new Table(slots, liveIds.Count, freeIds.ToArray()));
		} finally {
			Volatile.Write(ref _rebuilding, 0);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private nint HandleAt(int id) => _handleChunks[id >> ChunkShift]![id & (ChunkSize - 1)];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private string? Target(long slot) => GCHandle.FromIntPtr(HandleAt((int)(uint)slot - 1)).Target as string;

	// HashCode is seeded per process, so wire-supplied keys cannot be crafted to collide. Seeded XxHash3/XxHash32
	// measured the same (StringInternBenchmarks) and would add a dependency.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int Hash(ReadOnlySpan<byte> utf8) {
		var hash = new HashCode();
		hash.AddBytes(utf8);
		return hash.ToHashCode();
	}

	/// <summary>UTF-8 bytes vs a string, without decoding the bytes.</summary>
	[SkipLocalsInit]
	private static bool Utf8Equals(ReadOnlySpan<byte> utf8, string value) {
		if (Ascii.Equals(utf8, value))
			return true;
		// Ascii.Equals rejects any non-ASCII input; only then is an exact UTF-8 compare needed.
		if (value.Length > utf8.Length || Ascii.IsValid(utf8))
			return false;

		byte[]? rented = null;
		var buffer = utf8.Length <= StackEncodeLimit
			? stackalloc byte[StackEncodeLimit]
			: rented = ArrayPool<byte>.Shared.Rent(utf8.Length);
		try {
			// Destination sized to the wire bytes: a longer encoding reports DestinationTooSmall, i.e. not equal.
			var status = Utf8.FromUtf16(value, buffer[..utf8.Length], out _, out var written);
			return status == OperationStatus.Done && written == utf8.Length && buffer[..written].SequenceEqual(utf8);
		} finally {
			if (rented is not null)
				ArrayPool<byte>.Shared.Return(rented);
		}
	}

	private sealed class Table(long[] slots, int used, int[] freeHandleIds) {
		public readonly long[] Slots = slots;
		public readonly int[] FreeHandleIds = freeHandleIds;
		public int Used = used;
		public int FreeCursor;
	}
}
