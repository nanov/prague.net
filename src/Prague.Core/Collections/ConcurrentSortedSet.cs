namespace Prague.Core.Collections;

using System.Runtime.CompilerServices;
using Utils;

// C# imlementation of https://people.csail.mit.edu/shanir/publications/LazySkipList.pdf
// Optimized for maximum performance and minimum allocations
internal sealed class ConcurrentSortedList<TIndex, TValue>
	where TIndex : IComparable<TIndex>
	where TValue : IEquatable<TValue> {
	private const int MaxLevel = 32;
	private const double Probability = 0.25;

	// ReSharper disable once StaticMemberInGenericType
	// Use ThreadStatic for better performance than ThreadLocal (no allocation)
	[ThreadStatic] private static Random? _random;

	// Thread-local arrays to avoid repeated allocations
	[ThreadStatic] private static Node?[]? _preds;

	[ThreadStatic] private static Node?[]? _succs;

	[ThreadStatic] private static Node?[]? _locked;

	private readonly IEqualityComparer<TValue> _comparer;

	private readonly Node _head;
	private int _length;

	public ConcurrentSortedList() {
		_head = new Node(MaxLevel);
		_head.FullyLinked = true;
		_comparer = HashCollectionsTools.GetEqualityComparer<TValue>(null);
	}

	private static Random LocalRandom => _random ??= new Random(Guid.NewGuid().GetHashCode());

	public int Length => Volatile.Read(ref _length);

	/// <summary>
	///   Gets the minimum index in the list, or default if empty.
	/// </summary>
	public bool TryGetMin(out TIndex index, out TValue value) {
		var first = _head.Next[0];
		while (first != null && (first.Marked || !first.FullyLinked)) first = first.Next[0];
		if (first != null) {
			index = first.Index;
			value = first.Value;
			return true;
		}

		index = default!;
		value = default!;
		return false;
	}

	/// <summary>
	///   Gets the maximum index in the list, or default if empty.
	/// </summary>
	public bool TryGetMax(out TIndex index, out TValue value) {
		Node? last = null;
		var current = _head.Next[0];
		while (current != null) {
			if (!current.Marked && current.FullyLinked) last = current;
			current = current.Next[0];
		}

		if (last != null) {
			index = last.Index;
			value = last.Value;
			return true;
		}

		index = default!;
		value = default!;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Node?[] GetPredsArray() {
		var array = _preds;
		if (array == null) {
			array = new Node?[MaxLevel + 1];
			_preds = array;
		}
		else {
			Array.Clear(array);
		}

		return array;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Node?[] GetSuccsArray() {
		var array = _succs;
		if (array == null) {
			array = new Node?[MaxLevel + 1];
			_succs = array;
		}
		else {
			Array.Clear(array);
		}

		return array;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Node?[] GetLockedArray(int size) {
		var array = _locked;
		if (array == null) {
			array = new Node?[MaxLevel + 1];
			_locked = array;
		}
		else {
			Array.Clear(array, 0, size);
		}

		return array;
	}

	public bool Add(TIndex index, TValue value) {
		var topLevel = RandomLevel();
		var preds = GetPredsArray();
		var succs = GetSuccsArray();

		while (true) {
			var lFound = FindNode(index, value, preds, succs);

			if (lFound != -1) {
				var nodeFound = succs[lFound];
				if (nodeFound == null) continue;

				if (!nodeFound.Marked) {
					// Wait for node to be fully linked
					while (!nodeFound.FullyLinked) Thread.SpinWait(1);

					return false;
				}

				continue;
			}

			// Acquire locks from bottom to top
			var locked = GetLockedArray(topLevel + 1);
			var valid = true;

			try {
				for (var level = 0; valid && level <= topLevel; level++) {
					var pred = preds[level];
					if (pred == null) continue;

					var succ = succs[level];

					Monitor.Enter(pred.Lock);
					locked[level] = pred;

					valid = !pred.Marked && (succ == null || !succ.Marked) && pred.Next[level] == succ;
				}

				if (!valid) continue;

				// Create and link new node
				var newNode = new Node(index, value, topLevel);

				for (var level = 0; level <= topLevel; level++) {
					newNode.Next[level] = succs[level];
					var pred = preds[level];
					if (pred != null) pred.Next[level] = newNode;
				}

				newNode.FullyLinked = true;
				Interlocked.Increment(ref _length);
				return true;
			}
			finally {
				// Unlock in reverse order
				for (var i = topLevel; i >= 0; i--) {
					var node = locked[i];
					if (node != null) Monitor.Exit(node.Lock);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(TIndex index, TValue value) {
		var preds = GetPredsArray();
		var succs = GetSuccsArray();

		var lFound = FindNode(index, value, preds, succs);

		if (lFound == -1) return false;

		var found = succs[lFound];
		return found != null && found.FullyLinked && !found.Marked;
	}

	public bool Remove(TIndex index, TValue value) {
		Node? victim = null;
		var isMarked = false;
		var topLevel = -1;

		var preds = GetPredsArray();
		var succs = GetSuccsArray();

		while (true) {
			var lFound = FindNode(index, value, preds, succs);

			if (lFound != -1) victim = succs[lFound];

			if (isMarked || (lFound != -1 && victim != null && victim.FullyLinked && victim.TopLevel == lFound &&
			                 !victim.Marked)) {
				if (!isMarked) {
					topLevel = victim!.TopLevel;

					Monitor.Enter(victim.Lock);

					if (victim.Marked) {
						Monitor.Exit(victim.Lock);
						return false;
					}

					victim.Marked = true;
					isMarked = true;
				}

				// Acquire locks
				var locked = GetLockedArray(topLevel + 1);
				var valid = true;

				for (var level = 0; valid && level <= topLevel; level++) {
					var pred = preds[level];
					if (pred == null) continue;

					if (pred != victim) {
						Monitor.Enter(pred.Lock);
						locked[level] = pred;
					}

					valid = !pred.Marked && pred.Next[level] == victim;
				}

				if (!valid) {
					// Release all acquired locks before continuing
					for (var i = topLevel; i >= 0; i--) {
						var node = locked[i];
						if (node != null) {
							Monitor.Exit(node.Lock);
							locked[i] = null;
						}
					}

					continue;
				}

				// Unlink the node
				for (var level = topLevel; level >= 0; level--) {
					var pred = preds[level];
					if (pred != null && victim != null) pred.Next[level] = victim.Next[level];
				}

				// Release locks
				for (var i = topLevel; i >= 0; i--) {
					var node = locked[i];
					if (node != null) Monitor.Exit(node.Lock);
				}

				if (victim != null) Monitor.Exit(victim.Lock);

				Interlocked.Decrement(ref _length);
				return true;
			}

			return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void Range<TResultsAggregator>(TIndex from, TIndex to, ref TResultsAggregator resAgg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var preds = GetPredsArray();
		var succs = GetSuccsArray();

		FindNode(from, preds, succs);
		var curr = succs[0];

		// Optimized hot path - minimize branches and volatile reads
		while (curr != null) {
			// Use local copies to avoid multiple volatile reads
			var isFullyLinked = curr.FullyLinked;
			var isMarked = curr.Marked;

			if (isFullyLinked & !isMarked) {
				var cmp = curr.Compare(to);
				if (cmp > 0) break;

				if (curr.Compare(from) >= 0) resAgg.Add(curr.Index, curr.Value);
			}

			curr = curr.Next[0];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void RangeFrom<TResultsAggregator>(TIndex start, ref TResultsAggregator resAgg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var preds = GetPredsArray();
		var succs = GetSuccsArray();

		FindNode(start, preds, succs);
		var curr = succs[0];

		while (curr != null) {
			var isFullyLinked = curr.FullyLinked;
			var isMarked = curr.Marked;

			if (isFullyLinked & !isMarked && curr.Compare(start) >= 0) resAgg.Add(curr.Index, curr.Value);

			curr = curr.Next[0];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void RangeTo<TResultsAggregator>(TIndex to, ref TResultsAggregator resAgg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var curr = _head.Next[0];

		while (curr != null) {
			var isFullyLinked = curr.FullyLinked;
			var isMarked = curr.Marked;

			if (isFullyLinked & !isMarked) {
				if (curr.Compare(to) > 0) break;
				resAgg.Add(curr.Index, curr.Value);
			}

			curr = curr.Next[0];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void RangeToExclusive<TResultsAggregator>(TIndex to, ref TResultsAggregator resAgg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var curr = _head.Next[0];

		while (curr != null) {
			var isFullyLinked = curr.FullyLinked;
			var isMarked = curr.Marked;

			if (isFullyLinked & !isMarked) {
				if (curr.Compare(to) >= 0) break;
				resAgg.Add(curr.Index, curr.Value);
			}

			curr = curr.Next[0];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void RangeFromExclusive<TResultsAggregator>(TIndex start, ref TResultsAggregator resAgg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var preds = GetPredsArray();
		var succs = GetSuccsArray();

		FindNode(start, preds, succs);
		var curr = succs[0];

		while (curr != null) {
			var isFullyLinked = curr.FullyLinked;
			var isMarked = curr.Marked;

			if (isFullyLinked & !isMarked && curr.Compare(start) > 0) resAgg.Add(curr.Index, curr.Value);

			curr = curr.Next[0];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void RangeCustom<TResultsAggregator>(TIndex from, TIndex to, bool includeFrom, bool includeTo,
		ref TResultsAggregator resAgg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var preds = GetPredsArray();
		var succs = GetSuccsArray();

		FindNode(from, preds, succs);
		var curr = succs[0];

		while (curr != null) {
			var isFullyLinked = curr.FullyLinked;
			var isMarked = curr.Marked;

			if (isFullyLinked & !isMarked) {
				var fromCmp = curr.Compare(from);
				var toCmp = curr.Compare(to);

				// Check lower bound
				var passesLower = includeFrom ? fromCmp >= 0 : fromCmp > 0;

				// Check upper bound
				if (includeTo ? toCmp > 0 : toCmp >= 0)
					break;

				if (passesLower) resAgg.Add(curr.Index, curr.Value);
			}

			curr = curr.Next[0];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private int FindNode(TIndex index, Node?[] preds, Node?[] succs) {
		var lFound = -1;
		var pred = _head;

		for (var level = MaxLevel; level >= 0; level--) {
			var curr = pred.Next[level];

			while (curr != null) {
				var cmp = index.CompareTo(curr.Index);

				if (cmp > 0) {
					pred = curr;
					curr = pred.Next[level];
				}
				else {
					if (cmp == 0) lFound = level;

					break;
				}
			}

			preds[level] = pred;
			succs[level] = curr;
		}

		return lFound;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private int FindNode(TIndex index, TValue value, Node?[] preds, Node?[] succs) {
		var lFound = -1;
		var pred = _head;

		// Cache hash code computation - only compute once
		var valueHash = value.GetHashCode();

		for (var level = MaxLevel; level >= 0; level--) {
			var curr = pred.Next[level];

			while (curr != null) {
				var indexCmp = index.CompareTo(curr.Index);

				if (indexCmp > 0) {
					pred = curr;
					curr = pred.Next[level];
				}
				else if (indexCmp == 0) {
					// Same index - use value equality and hash code for ordering
					if (value.Equals(curr.Value)) {
						// Found exact match
						if (lFound == -1) lFound = level;
						break;
					}

					// Different values - use hash code for consistent ordering
					var currHash = curr.Value.GetHashCode();
					var hashCmp = valueHash.CompareTo(currHash);

					if (hashCmp >= 0) {
						// Our value's hash is greater, continue searching
						pred = curr;
						curr = pred.Next[level];
					}
					else {
						// Our value's hash is less - won't find it
						break;
					}
				}
				else {
					// indexCmp < 0
					break;
				}
			}

			preds[level] = pred;
			succs[level] = curr;
		}

		return lFound;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int RandomLevel() {
		int level;
		var rnd = LocalRandom;

		// Unroll first iteration for common case (level 0)
		if (rnd.NextDouble() >= Probability)
			return 0;

		level = 1;

		while (level < MaxLevel && rnd.NextDouble() < Probability) level++;

		return level;
	}

	private sealed class Node {
		public readonly object Lock = new();
		public readonly Node?[] Next;
		public readonly int TopLevel;
		public volatile bool FullyLinked;
		public TIndex Index;
		public volatile bool Marked; // For logical deletion
		public TValue Value;

		public Node(int level) {
			Next = new Node[level + 1];
			TopLevel = level;
			Index = default!;
			Value = default!;
		}

		public Node(TIndex index, TValue value, int level) : this(level) {
			Index = index;
			Value = value;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Compare(TIndex otherIndex) {
			return Index.CompareTo(otherIndex);
		}
	}

	internal interface IResultAggregator : IDisposable {
		public void Add(TIndex index, TValue value);
	}
}
