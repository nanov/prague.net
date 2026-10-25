namespace Prague.Kafka.SerDe;

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

/// <summary>
///   Weak string intern pool keyed by the UTF-8 bytes on the wire.
///
///   Layout: an open-addressing (linear probing) table of <c>{hash, weak GCHandle}</c> slots — no stored
///   key bytes, no per-entry objects. A hit is lock-free: hash the bytes, probe, dereference the handle and
///   compare the live string against the bytes (<see cref="Ascii.Equals(ReadOnlySpan{byte}, ReadOnlySpan{char})"/>
///   fast path), so a repeat costs no decode and no allocation. Every interned string is held weakly: it lives
///   exactly as long as something else (typically a cached entity) roots it.
///
///   Misses take one lock. Handles are never freed while the pool lives — a slot whose string was collected
///   is recycled by re-targeting its handle — so a lock-free reader can never dereference a freed handle; a
///   reader that races a re-target simply sees a string that fails the content compare and takes the miss
///   path. Dead slots are scavenged when the table is rebuilt for growth; their handles go to a free list.
/// </summary>
internal sealed class Utf8WeakInternPool : IDisposable {
	private const int MinCapacity = 1024;
	private const int StackEncodeLimit = 1024;

	private readonly Lock _writeLock = new();
	private readonly Stack<nint> _freeHandles = new();
	private Table _table = new(MinCapacity);

	/// <summary>Occupied slots, live or collected — diagnostics and tests.</summary>
	internal int Count => Volatile.Read(ref _table).Used;

	public string GetOrAdd(ReadOnlySpan<byte> utf8) {
		var hash = Hash(utf8);
		return TryFind(Volatile.Read(ref _table), utf8, hash) ?? Add(utf8, hash);
	}

	public void Dispose() {
		lock (_writeLock) {
			foreach (ref var slot in _table.Slots.AsSpan())
				if (slot.Handle != 0)
					GCHandle.FromIntPtr(slot.Handle).Free();
			while (_freeHandles.TryPop(out var handle))
				GCHandle.FromIntPtr(handle).Free();
			_table = new Table(MinCapacity);
		}
	}

	private static string? TryFind(Table table, ReadOnlySpan<byte> utf8, int hash) {
		var slots = table.Slots;
		var mask = slots.Length - 1;
		for (var i = hash & mask; ; i = (i + 1) & mask) {
			ref var slot = ref slots[i];
			var handle = Volatile.Read(ref slot.Handle);
			if (handle == 0)
				return null;
			if (slot.Hash == hash && GCHandle.FromIntPtr(handle).Target is string candidate && Utf8Equals(utf8, candidate))
				return candidate;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string Add(ReadOnlySpan<byte> utf8, int hash) {
		lock (_writeLock) {
			var table = _table;
			var slots = table.Slots;
			var mask = slots.Length - 1;
			var reuse = -1;
			int i;
			for (i = hash & mask; ; i = (i + 1) & mask) {
				var handle = slots[i].Handle;
				if (handle == 0)
					break;
				var target = GCHandle.FromIntPtr(handle).Target;
				if (target is null) {
					if (reuse < 0)
						reuse = i;
				} else if (slots[i].Hash == hash && Utf8Equals(utf8, (string)target)) {
					return (string)target; // another thread interned it while we waited
				}
			}

			var value = Encoding.UTF8.GetString(utf8);
			if (reuse >= 0) {
				// Re-target the collected slot in place: readers holding the old hash see a mismatching string.
				ref var slot = ref slots[reuse];
				var recycled = GCHandle.FromIntPtr(slot.Handle);
				recycled.Target = value;
				slot.Hash = hash;
				return value;
			}

			if ((table.Used + 1) * 2 > slots.Length) {
				Rebuild(table);
				return AddFresh(_table, value, hash);
			}

			return AddFresh(table, value, hash);
		}
	}

	/// <summary>Writes into the first empty slot of <paramref name="hash"/>'s probe chain. Caller holds the lock and has checked the load factor.</summary>
	private string AddFresh(Table table, string value, int hash) {
		var slots = table.Slots;
		var mask = slots.Length - 1;
		var i = hash & mask;
		while (slots[i].Handle != 0)
			i = (i + 1) & mask;

		if (_freeHandles.TryPop(out var handle)) {
			var recycled = GCHandle.FromIntPtr(handle);
			recycled.Target = value;
		} else
			handle = GCHandle.ToIntPtr(GCHandle.Alloc(value, GCHandleType.Weak));

		slots[i].Hash = hash;
		Volatile.Write(ref slots[i].Handle, handle); // publishes Hash with the handle
		table.Used++;
		return value;
	}

	/// <summary>
	///   Moves live slots into a table sized for them and parks collected slots' handles on the free list
	///   (never freed: readers may still be probing the old table). Caller holds the lock.
	/// </summary>
	private void Rebuild(Table old) {
		var live = 0;
		foreach (ref var slot in old.Slots.AsSpan())
			if (slot.Handle != 0 && GCHandle.FromIntPtr(slot.Handle).Target is not null)
				live++;

		var next = new Table(Math.Max(MinCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)(live + 1) * 4)));
		var mask = next.Slots.Length - 1;
		foreach (ref var slot in old.Slots.AsSpan()) {
			if (slot.Handle == 0)
				continue;
			if (GCHandle.FromIntPtr(slot.Handle).Target is null) {
				_freeHandles.Push(slot.Handle);
				continue;
			}

			var i = slot.Hash & mask;
			while (next.Slots[i].Handle != 0)
				i = (i + 1) & mask;
			next.Slots[i] = slot;
			next.Used++;
		}

		Volatile.Write(ref _table, next);
	}

	// HashCode is seeded per process, so wire-supplied keys cannot be crafted to collide.
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

	private struct Slot {
		public int Hash;
		public nint Handle; // GCHandle (weak) as IntPtr; 0 = empty
	}

	private sealed class Table(int capacity) {
		public readonly Slot[] Slots = new Slot[capacity];
		public int Used; // written under the lock only
	}
}
