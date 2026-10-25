namespace Prague.Kafka.SerDe;

using System.Collections.Concurrent;
using System.Text;

/// <summary>
///   Weak string intern pool keyed by the UTF-8 bytes on the wire. A hit is a lock-free lookup that
///   returns the live instance without decoding or allocating; a miss decodes once and publishes the
///   string through a weak reference, so an interned string lives exactly as long as something else
///   (typically a cached entity) roots it. Entries whose string was collected are swept whenever the
///   table doubles past its last post-sweep size.
/// </summary>
internal sealed class Utf8WeakInternPool {
	private const int MinSweepThreshold = 1024;

	private readonly ConcurrentDictionary<byte[], WeakReference<string>> _entries = new(Utf8KeyComparer.Instance);
	private readonly ConcurrentDictionary<byte[], WeakReference<string>>.AlternateLookup<ReadOnlySpan<byte>> _lookup;
	private int _sweepThreshold = MinSweepThreshold;
	private int _sweeping;

	public Utf8WeakInternPool() => _lookup = _entries.GetAlternateLookup<ReadOnlySpan<byte>>();

	internal int Count => _entries.Count;

	public string GetOrAdd(ReadOnlySpan<byte> utf8) {
		if (_lookup.TryGetValue(utf8, out var existing) && existing.TryGetTarget(out var hit))
			return hit;
		return Add(utf8);
	}

	private string Add(ReadOnlySpan<byte> utf8) {
		var value = Encoding.UTF8.GetString(utf8);
		var reference = new WeakReference<string>(value);
		while (true) {
			if (_lookup.TryAdd(utf8, reference)) {
				if (_entries.Count > Volatile.Read(ref _sweepThreshold))
					Sweep();
				return value;
			}

			// Lost a race or found a dead entry: adopt a live winner, otherwise replace the dead reference.
			if (!_lookup.TryGetValue(utf8, out var key, out var current))
				continue;
			if (current.TryGetTarget(out var winner))
				return winner;
			if (_entries.TryUpdate(key, reference, current))
				return value;
		}
	}

	private void Sweep() {
		if (Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0)
			return;
		try {
			foreach (var entry in _entries)
				if (!entry.Value.TryGetTarget(out _))
					_entries.TryRemove(entry);
			Volatile.Write(ref _sweepThreshold, Math.Max(MinSweepThreshold, _entries.Count * 2));
		} finally {
			Volatile.Write(ref _sweeping, 0);
		}
	}

	private sealed class Utf8KeyComparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]> {
		public static readonly Utf8KeyComparer Instance = new();

		public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

		public int GetHashCode(byte[] obj) => GetHashCode((ReadOnlySpan<byte>)obj);

		public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) => alternate.SequenceEqual(other);

		// HashCode is seeded per process, so wire-supplied keys cannot be crafted to collide.
		public int GetHashCode(ReadOnlySpan<byte> alternate) {
			var hash = new HashCode();
			hash.AddBytes(alternate);
			return hash.ToHashCode();
		}

		public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();
	}
}
