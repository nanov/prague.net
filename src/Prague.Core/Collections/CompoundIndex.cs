namespace Prague.Core.Collections;

using System.Runtime.CompilerServices;

/// <summary>
///   A compound index backed by a PooledBTree with compound keys.
///   Stores entries as (Prefix, SortKey, EntityKey) in a single B+ tree.
///   Supports efficient seek-to-prefix + walk-in-sort-order + take(K) queries.
///
///   Thread safety: same as PooledBTree — single writer, concurrent readers.
/// </summary>
internal sealed class CompoundIndex<TPrefix, TSort, TKey> : IDisposable
	where TPrefix : IComparable<TPrefix>
	where TSort : IComparable<TSort>
	where TKey : IComparable<TKey>, IEquatable<TKey> {
	// The BTree index key is the compound key, value is a dummy byte (we only care about key ordering)
	private readonly PooledBTree<CompoundKey<TPrefix, TSort, TKey>, byte> _tree = new();

	public int Count => _tree.Length;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Add(TPrefix prefix, TSort sort, TKey key) {
		return _tree.Add(new CompoundKey<TPrefix, TSort, TKey>(prefix, sort, key), 0);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Remove(TPrefix prefix, TSort sort, TKey key) {
		return _tree.Remove(new CompoundKey<TPrefix, TSort, TKey>(prefix, sort, key), 0);
	}

	/// <summary>
	///   Update: remove old entry, add new one. Only touches the tree if the compound key changed.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Update(TPrefix oldPrefix, TSort oldSort, TPrefix newPrefix, TSort newSort, TKey key) {
		if (oldPrefix.CompareTo(newPrefix) == 0 && oldSort.CompareTo(newSort) == 0)
			return;
		_tree.Remove(new CompoundKey<TPrefix, TSort, TKey>(oldPrefix, oldSort, key), 0);
		_tree.Add(new CompoundKey<TPrefix, TSort, TKey>(newPrefix, newSort, key), 0);
	}

	/// <summary>
	///   Seek to the first entry matching the given prefix, walk forward in sort order,
	///   and collect up to 'take' entity keys. Optionally skip 'skip' entries first.
	///   Returns the number of results written into the buffer.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int SeekAndTake(TPrefix prefix, int skip, int take, TKey[] buffer) {
		var agg = new SeekAggregator(prefix, skip, take, buffer);
		// Seek to the very start of this prefix by using min possible sort/key values
		// We use RangeFrom which finds the first leaf >= the given key
		var seekKey = new CompoundKey<TPrefix, TSort, TKey>(prefix, default!, default!);
		_tree.RangeFrom(seekKey, ref agg);
		return agg.Found;
	}

	/// <summary>
	///   Seek to a prefix and walk in sort order, checking each key against a candidate set.
	///   Stops after collecting 'take' hits. This combines filtering + sorting in one pass.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int SeekFilterAndTake(TPrefix prefix, ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates, int skip, int take,
		TKey[] buffer) {
		var agg = new SeekFilterAggregator(prefix, skip, take, buffer, ref candidates);
		var seekKey = new CompoundKey<TPrefix, TSort, TKey>(prefix, default!, default!);
		_tree.RangeFrom(seekKey, ref agg);
		return agg.Found;
	}

	public void Dispose() {
		_tree.Dispose();
	}

	/// <summary>
	///   Aggregator that collects entity keys in sort order for a given prefix,
	///   with skip/take support. Stops when prefix changes or take is reached.
	/// </summary>
	private ref struct SeekAggregator : PooledBTree<CompoundKey<TPrefix, TSort, TKey>, byte>.IResultAggregator {
		private readonly TPrefix _prefix;
		private readonly TKey[] _buffer;
		private readonly int _take;
		private int _skip;
		private bool _done;
		public int Found;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public SeekAggregator(TPrefix prefix, int skip, int take, TKey[] buffer) {
			_prefix = prefix;
			_skip = skip;
			_take = take;
			_buffer = buffer;
			Found = 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Add(CompoundKey<TPrefix, TSort, TKey> index, byte value) {
			// A dedicated stop flag keeps Found truthful: the old "Found = _take" stop
			// signal over-reported the hit count whenever the prefix run was shorter
			// than take and another prefix followed, leaving garbage buffer tails.
			if (_done)
				return;

			if (index.Prefix.CompareTo(_prefix) != 0 || Found >= _take) {
				_done = true;
				return;
			}

			if (_skip > 0) {
				_skip--;
				return;
			}

			_buffer[Found++] = index.Key;
		}

		public void Dispose() { }
	}

	/// <summary>
	///   Aggregator that walks in sort order within a prefix, but only collects keys
	///   that are present in a candidate ValueSet. Enables compound index + additional filter.
	/// </summary>
	private ref struct SeekFilterAggregator
		: PooledBTree<CompoundKey<TPrefix, TSort, TKey>, byte>.IResultAggregator {
		private readonly TPrefix _prefix;
		private readonly TKey[] _buffer;
		private readonly int _take;
		private ref ValueSet<TKey, DefaultKeyComparer<TKey>> _candidates;
		private int _skip;
		private bool _done;
		public int Found;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public SeekFilterAggregator(TPrefix prefix, int skip, int take, TKey[] buffer,
			ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates) {
			_prefix = prefix;
			_skip = skip;
			_take = take;
			_buffer = buffer;
			_candidates = ref candidates;
			Found = 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Add(CompoundKey<TPrefix, TSort, TKey> index, byte value) {
			// See SeekAggregator: a dedicated stop flag keeps Found truthful.
			if (_done)
				return;

			if (index.Prefix.CompareTo(_prefix) != 0 || Found >= _take) {
				_done = true;
				return;
			}

			// Only collect if this key is in the candidate set
			if (!_candidates.Contains(index.Key)) return;

			if (_skip > 0) {
				_skip--;
				return;
			}

			_buffer[Found++] = index.Key;
		}

		public void Dispose() { }
	}
}
