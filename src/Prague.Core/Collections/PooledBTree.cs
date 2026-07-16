namespace Prague.Core.Collections;

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
///   A B+ tree backed by ArrayPool-rented arrays. Optimized for:
///   - O(log n) add/remove via binary search within cache-friendly leaf nodes
///   - O(log n + k) range queries with sequential leaf-chain iteration
///   - O(1) TryGetMin / TryGetMax via cached first/last leaf pointers
///   - Zero per-element heap allocation (leaf data stored in pooled arrays)
///   Thread safety: single writer serialized by the internal write lock; readers are
///   lock-free with documented staleness (a concurrent range scan may transiently skip
///   or double-see an entry during in-leaf shifts, and results reflect no single point
///   in time). New nodes are release-published and leaf counts acquire-read, so a
///   reader never observes a node before its contents. Structurally removed nodes are
///   retired through ReaderGate and return to the pool only after the reader grace
///   period, so a parked reader never observes recycled memory.
///
///   Performance notes:
///   - Allocations: steady-state zero per operation. Splits allocate the node object
///     (arrays are pool round-trips); path/split scratch is per-thread and reused
///     uncleared (consumers only read what their own descent wrote). Ref-typed
///     keys/values in leaves vacated by a concurrent shrink may read as default —
///     part of the documented staleness model.
///   - Dispatch: no virtual calls on any path — descents pattern-match the sealed
///     node classes (method-table compare), TIndex.CompareTo / TValue.Equals are
///     struct-constrained generic calls that devirtualize and inline per closed type.
///   - Bounds checks are elided in scans and binary searches via
///     MemoryMarshal.GetArrayDataReference: count &lt;= capacity is a writer invariant
///     (stale counts included) and rented arrays are never shorter than capacity.
///   - Adds of strictly-ascending keys (timestamps — the dominant range-index write
///     pattern) take an O(1) append fast path into the last leaf: no descent, no
///     duplicate scan, no path bookkeeping.
///   - Duplicate-key runs: the tree's true sort key is the composite (key, value)
///     pair — entries with equal keys are sorted by value and internal separators
///     carry the value half too, so Add/Remove/Contains binary-search the exact
///     pair even when a run of equal keys spans many leaves: O(log n) instead of
///     O(run length). This is why TValue must be IComparable, and its CompareTo
///     must be consistent with Equals (CompareTo == 0 iff Equals — the standard
///     IComparable contract; holds for longs, tuples, Guids and ordinal strings):
///     point lookups probe exactly one composite position and treat a mismatch
///     there as a definitive miss.
/// </summary>
internal sealed class PooledBTree<TIndex, TValue> : IDisposable
	where TIndex : IComparable<TIndex>
	where TValue : IEquatable<TValue>, IComparable<TValue> {
	private const int LeafCapacity = 64;
	private const int InternalCapacity = 64; // max children per internal node
	private const int MaxDepth = 8; // 64^8 > 10^14, more than enough

	[ThreadStatic] private static InternalNode?[]? _ancestorsBuf;
	[ThreadStatic] private static int[]? _childIdxBuf;
	[ThreadStatic] private static TIndex[]? _splitKeysBuf;
	[ThreadStatic] private static TValue[]? _splitSepValuesBuf;
	[ThreadStatic] private static Node[]? _splitChildrenBuf;

	private readonly Lock _writeLock = new();
	private Node _root;
	private LeafNode _firstLeaf;
	private LeafNode _lastLeaf;
	private int _length;

	public PooledBTree() {
		var leaf = new LeafNode();
		_root = leaf;
		_firstLeaf = leaf;
		_lastLeaf = leaf;
	}

	public int Length {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Volatile.Read(ref _length);
	}

	// ───────────────────── Node types ─────────────────────

	// No IsLeaf virtual: descents pattern-match on the sealed node classes instead —
	// the JIT emits a single method-table compare per level, where a virtual property
	// costs a vtable load + call (and defeats branch prediction less predictably).
	private abstract class Node : ReaderGate.IRetirable {
		public abstract void ReclaimToPool();
	}

	private sealed class LeafNode : Node {
		public TIndex[] Keys;
		public TValue[] Values;
		public int Count;
		public LeafNode? Next;
		public LeafNode? Prev;

		public LeafNode() {
			Keys = ArrayPool<TIndex>.Shared.Rent(LeafCapacity);
			Values = ArrayPool<TValue>.Shared.Rent(LeafCapacity);
		}

		public override void ReclaimToPool() => ReturnToPool();

		public void ReturnToPool() {
			var keys = Keys;
			var values = Values;
			Keys = null!;
			Values = null!;
			try {
				ArrayPool<TIndex>.Shared.Return(keys, RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>());
			}
			catch (ArgumentException) { }

			try {
				ArrayPool<TValue>.Shared.Return(values, RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
			}
			catch (ArgumentException) { }
		}
	}

	private sealed class InternalNode : Node {
		public TIndex[] Keys; // separator keys
		public TValue[] SepValues; // separator values: SepValues[i] pairs with Keys[i] (first pair of Children[i + 1])
		public Node[] Children;
		public int KeyCount; // number of separator keys; child count = KeyCount + 1

		public InternalNode() {
			Keys = ArrayPool<TIndex>.Shared.Rent(InternalCapacity - 1);
			SepValues = ArrayPool<TValue>.Shared.Rent(InternalCapacity - 1);
			Children = ArrayPool<Node>.Shared.Rent(InternalCapacity);
		}

		public override void ReclaimToPool() => ReturnToPool();

		public void ReturnToPool() {
			var keys = Keys;
			var sepValues = SepValues;
			var children = Children;
			Keys = null!;
			SepValues = null!;
			Children = null!;
			try {
				ArrayPool<TIndex>.Shared.Return(keys, RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>());
			}
			catch (ArgumentException) { }

			try {
				ArrayPool<TValue>.Shared.Return(sepValues, RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
			}
			catch (ArgumentException) { }

			try {
				ArrayPool<Node>.Shared.Return(children, true); // clear refs
			}
			catch (ArgumentException) { }
		}
	}

	// ───────────────────── Thread-local buffers ─────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static InternalNode?[] GetAncestorsBuf() {
		// No clearing: every consumer writes ancestors[0..depth) during its own descent
		// before reading them, so stale entries are never observed. (The stale refs
		// keep at most MaxDepth retired nodes alive per thread until the next descent —
		// bounded and harmless; clearing cost 8 stores on every Add/Remove.)
		return _ancestorsBuf ??= new InternalNode?[MaxDepth];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int[] GetChildIdxBuf() {
		var buf = _childIdxBuf;
		if (buf == null) {
			buf = new int[MaxDepth];
			_childIdxBuf = buf;
		}

		return buf;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static TIndex[] GetSplitKeysBuf() {
		var buf = _splitKeysBuf;
		if (buf == null) {
			buf = new TIndex[InternalCapacity]; // max keys after insert = InternalCapacity - 1 + 1
			_splitKeysBuf = buf;
		}

		return buf;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static TValue[] GetSplitSepValuesBuf() {
		var buf = _splitSepValuesBuf;
		if (buf == null) {
			buf = new TValue[InternalCapacity]; // mirrors GetSplitKeysBuf
			_splitSepValuesBuf = buf;
		}

		return buf;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Node[] GetSplitChildrenBuf() {
		var buf = _splitChildrenBuf;
		if (buf == null) {
			buf = new Node[InternalCapacity + 1]; // max children after insert = InternalCapacity + 1
			_splitChildrenBuf = buf;
		}

		return buf;
	}

	// ───────────────────── Tree traversal ─────────────────────

	/// <summary>
	///   Binary search within an internal node for the LEFTMOST child that can contain
	///   the given key. Keys equal to a separator route LEFT (the run of equal keys may
	///   start in the child before the separator).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FindChildIndexLeft(InternalNode node, TIndex index) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(node.Keys);
		var lo = 0;
		var hi = node.KeyCount - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = index.CompareTo(Unsafe.Add(ref keys, mid));
			if (cmp > 0) // equal goes LEFT (to find leftmost child with this key)
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}

	// ───────────────────── Composite (key, value) search ─────────────────────

	/// <summary>
	///   Compares the value halves of composite entries — a struct-constrained
	///   generic call that devirtualizes and inlines per closed type, exactly like
	///   TIndex.CompareTo on the key half.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int CompareValues(TValue a, TValue b) => a.CompareTo(b);

	/// <summary>
	///   Composite FindChildIndex: separators are (key, value) pairs, entries equal to
	///   a separator route RIGHT (separator = first pair of the right child).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FindChildIndexComposite(InternalNode node, TIndex index, TValue value) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(node.Keys);
		var lo = 0;
		var hi = node.KeyCount - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = index.CompareTo(Unsafe.Add(ref keys, mid));
			if (cmp == 0) {
				// The separator-values array is only touched on a key tie: unique-key
				// descents never pay its load (a second array header is a likely cache
				// miss on every visited node).
				cmp = CompareValues(value,
					Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(node.SepValues), mid));
			}

			if (cmp >= 0) // equal goes right
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}

	/// <summary>
	///   Composite descent for point lookups (writer-exact under the lock; lock-free
	///   readers get the same documented staleness as the key-only descent).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private LeafNode FindLeafComposite(TIndex index, TValue value) {
		var node = _root;
		while (node is InternalNode intern) {
			var child = intern.Children[FindChildIndexComposite(intern, index, value)];
			if (child == null) {
				// Lock-free descent raced an in-place structural shift (stale KeyCount
				// vs cleared child slot) — restart from the root; the writer's change
				// is transient and the retry lands on a consistent view.
				node = _root;
				continue;
			}

			node = child;
		}

		return Unsafe.As<LeafNode>(node);
	}

	/// <summary>Composite twin of FindLeafWithPath.</summary>
	private LeafNode FindLeafWithPathComposite(TIndex index, TValue value,
		InternalNode?[] ancestors, int[] childIndices, out int depth) {
		var node = _root;
		depth = 0;
		while (node is InternalNode intern) {
			var childIdx = FindChildIndexComposite(intern, index, value);
			ancestors[depth] = intern;
			childIndices[depth] = childIdx;
			depth++;
			node = intern.Children[childIdx];
		}

		return Unsafe.As<LeafNode>(node);
	}

	/// <summary>
	///   Composite LeafLowerBound: first position where (Keys[pos], Values[pos]) >=
	///   (index, value) in composite order. The caller-supplied pair is always the
	///   CompareTo receiver (same orientation as FindChildIndexComposite): a lock-free
	///   reader racing a shrink may observe a stale Count and probe a vacated slot,
	///   which for ref-typed slots reads as null — safe as an argument, an NRE as a
	///   receiver.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int LeafLowerBoundComposite(LeafNode leaf, TIndex index, TValue value) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
		var lo = 0;
		var hi = Volatile.Read(ref leaf.Count) - 1; // acquire: pairs with InsertIntoLeaf's release
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = index.CompareTo(Unsafe.Add(ref keys, mid));
			if (cmp == 0) {
				// Values are only touched on a key tie — see FindChildIndexComposite.
				cmp = CompareValues(value,
					Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(leaf.Values), mid));
			}

			if (cmp > 0)
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}

	/// <summary>
	///   Finds the leftmost leaf that could contain keys >= index.
	///   Routes equal keys LEFT so range scans start from the first matching leaf.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private LeafNode FindLeafForRange(TIndex index) {
		var node = _root;
		while (node is InternalNode intern) {
			var child = intern.Children[FindChildIndexLeft(intern, index)];
			if (child == null) {
				// See FindLeafComposite: transient race with an in-place structural shift.
				node = _root;
				continue;
			}

			node = child;
		}

		return Unsafe.As<LeafNode>(node);
	}

	// ───────────────────── Leaf binary search ─────────────────────

	/// <summary>
	///   Binary search within a leaf for the first position where Keys[pos] >= index.
	///   Returns the insertion point (may be equal to count if all keys are less).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int LeafLowerBound(LeafNode leaf, TIndex index) {
		// Bounds checks elided: mid <= Count - 1 <= LeafCapacity - 1 and the rented
		// key array is always >= LeafCapacity long.
		ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
		var lo = 0;
		var hi = Volatile.Read(ref leaf.Count) - 1; // acquire: pairs with InsertIntoLeaf's release
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = Unsafe.Add(ref keys, mid).CompareTo(index);
			if (cmp < 0)
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}

	/// <summary>
	///   Binary search within a leaf for the first position where Keys[pos] > index.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int LeafUpperBound(LeafNode leaf, TIndex index) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
		var lo = 0;
		var hi = Volatile.Read(ref leaf.Count) - 1; // acquire: pairs with InsertIntoLeaf's release
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = Unsafe.Add(ref keys, mid).CompareTo(index);
			if (cmp <= 0)
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}

	// ───────────────────── Add ─────────────────────

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool Add(TIndex index, TValue value) {
		lock (_writeLock) {
			return AddCore(index, value);
		}
	}

	private bool AddCore(TIndex index, TValue value) {
		// Monotonic-append fast path: range-index keys are typically timestamps, so new
		// entries usually sort past the current maximum. An entry strictly greater than
		// the current maximum cannot be a duplicate and appends into the (non-full) last
		// leaf with no descent, no duplicate scan and no path bookkeeping — O(1) instead
		// of O(log n). The comparison is composite, so batch-stamped keys (equal key,
		// ascending value) keep hitting this path.
		var last = _lastLeaf;
		var lastCount = last.Count; // writer-owned under the lock: plain read is exact
		if (lastCount > 0 && lastCount < LeafCapacity) {
			var cmpLast = index.CompareTo(last.Keys[lastCount - 1]);
			if (cmpLast == 0)
				cmpLast = CompareValues(value, last.Values[lastCount - 1]);
			if (cmpLast > 0) {
				last.Keys[lastCount] = index;
				last.Values[lastCount] = value;
				// Release-publish the grown count AFTER the slot stores (see InsertIntoLeaf).
				Volatile.Write(ref last.Count, lastCount + 1);
				_length++;
				return true;
			}
		}

		var ancestors = GetAncestorsBuf();
		var childIndices = GetChildIdxBuf();

		// Composite descent lands on the exact leaf for the (index, value) pair;
		// the composite lower bound is both the duplicate probe and the sorted
		// insertion point — no run scans regardless of duplicate-key run length.
		var leaf = FindLeafWithPathComposite(index, value, ancestors, childIndices, out var depth);
		var insertPos = LeafLowerBoundComposite(leaf, index, value);
		if (insertPos < leaf.Count && leaf.Keys[insertPos].CompareTo(index) == 0
			&& value.Equals(leaf.Values[insertPos]))
			return false;

		// Insert into leaf
		if (leaf.Count < LeafCapacity) {
			InsertIntoLeaf(leaf, insertPos, index, value);
			_length++;
			return true;
		}

		// Leaf is full — need to split
		SplitAndInsert(leaf, insertPos, index, value, ancestors, childIndices, depth);
		_length++;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void InsertIntoLeaf(LeafNode leaf, int pos, TIndex index, TValue value) {
		if (pos < leaf.Count) {
			Array.Copy(leaf.Keys, pos, leaf.Keys, pos + 1, leaf.Count - pos);
			Array.Copy(leaf.Values, pos, leaf.Values, pos + 1, leaf.Count - pos);
		}

		leaf.Keys[pos] = index;
		leaf.Values[pos] = value;
		// Release-publish the grown count AFTER the slot stores: on weak memory models
		// a plain Count++ can become visible first, letting a concurrent reader scan
		// the not-yet-written tail slot and serve dirt left in the pooled array.
		// Readers pair this with an acquire read of Count.
		Volatile.Write(ref leaf.Count, leaf.Count + 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SplitAndInsert(LeafNode leaf, int insertPos, TIndex index, TValue value,
		InternalNode?[] ancestors, int[] childIndices, int depth) {
		// Leaf is full (LeafCapacity items). We need to insert one more and split.
		// Strategy: left keeps (mid) items, right gets the rest.
		// mid = (LeafCapacity + 1) / 2 = 33 for capacity 64.
		var mid = (LeafCapacity + 1) / 2;
		var newLeaf = new LeafNode();

		if (insertPos < mid) {
			// New item goes into left half.
			// Left will have (mid-1) existing items + 1 new = mid items.
			// Right will have LeafCapacity - (mid-1) items.
			var leftKeep = mid - 1;
			var rightCount = LeafCapacity - leftKeep;

			// Copy right portion to new leaf
			Array.Copy(leaf.Keys, leftKeep, newLeaf.Keys, 0, rightCount);
			Array.Copy(leaf.Values, leftKeep, newLeaf.Values, 0, rightCount);
			newLeaf.Count = rightCount;

			// Trim old leaf to leftKeep items
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>())
				Array.Clear(leaf.Keys, leftKeep, rightCount);
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
				Array.Clear(leaf.Values, leftKeep, rightCount);
			leaf.Count = leftKeep;

			// Insert new item into left half (InsertIntoLeaf handles shifting)
			InsertIntoLeaf(leaf, insertPos, index, value);
		}
		else {
			// New item goes into right half.
			// Left keeps mid items. Right gets (LeafCapacity - mid) existing + 1 new.
			var rightCount = LeafCapacity - mid;
			var newInsertPos = insertPos - mid;

			// Copy right portion to new leaf
			Array.Copy(leaf.Keys, mid, newLeaf.Keys, 0, rightCount);
			Array.Copy(leaf.Values, mid, newLeaf.Values, 0, rightCount);
			newLeaf.Count = rightCount;

			// Trim old leaf
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>())
				Array.Clear(leaf.Keys, mid, rightCount);
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
				Array.Clear(leaf.Values, mid, rightCount);
			leaf.Count = mid;

			// Insert new item into right (new) leaf
			InsertIntoLeaf(newLeaf, newInsertPos, index, value);
		}

		// Link new leaf into the chain. Publication order matters for lock-free chain
		// readers on weak memory models: the new leaf's freshly-rented arrays were just
		// filled with plain stores, so every edge that makes it reachable must be a
		// release store — otherwise a reader can observe the link before the contents
		// and serve stale pool garbage.
		newLeaf.Next = leaf.Next;
		newLeaf.Prev = leaf;
		if (leaf.Next != null)
			Volatile.Write(ref leaf.Next.Prev, newLeaf);
		Volatile.Write(ref leaf.Next, newLeaf);

		// Update last leaf if needed
		if (_lastLeaf == leaf && newLeaf.Next == null)
			Volatile.Write(ref _lastLeaf, newLeaf);

		// Promote separator (key, value) to parent — the composite descent needs the
		// value half to discriminate children inside duplicate-key runs.
		var promotedKey = newLeaf.Keys[0];
		var promotedValue = newLeaf.Values[0];
		InsertIntoParent(leaf, promotedKey, promotedValue, newLeaf, ancestors, childIndices, depth);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InsertIntoParent(Node leftChild, TIndex key, TValue sepValue, Node rightChild,
		InternalNode?[] ancestors, int[] childIndices, int depth) {
		if (depth == 0) {
			// Root was a leaf, create new root. Release-publish: the new root's
			// freshly-rented arrays must be visible before the root pointer is.
			var newRoot = new InternalNode();
			newRoot.Keys[0] = key;
			newRoot.SepValues[0] = sepValue;
			newRoot.Children[0] = leftChild;
			newRoot.Children[1] = rightChild;
			newRoot.KeyCount = 1;
			Volatile.Write(ref _root, newRoot);
			return;
		}

		var level = depth - 1;
		var parent = ancestors[level]!;

		if (parent.KeyCount < InternalCapacity - 1) {
			// Parent has room — insert key and child
			var insertAt = childIndices[level];
			// Shift keys right
			if (insertAt < parent.KeyCount) {
				Array.Copy(parent.Keys, insertAt, parent.Keys, insertAt + 1, parent.KeyCount - insertAt);
				Array.Copy(parent.SepValues, insertAt, parent.SepValues, insertAt + 1, parent.KeyCount - insertAt);
				Array.Copy(parent.Children, insertAt + 1, parent.Children, insertAt + 2,
					parent.KeyCount - insertAt);
			}

			parent.Keys[insertAt] = key;
			parent.SepValues[insertAt] = sepValue;
			// Release-publish the new node: its rented arrays were filled with plain
			// stores and must be visible before any pointer to it.
			Volatile.Write(ref parent.Children[insertAt + 1], rightChild);
			parent.KeyCount++;
			return;
		}

		// Parent is full — split internal node
		SplitInternalAndInsert(parent, key, sepValue, rightChild, childIndices[level], ancestors, childIndices, level);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SplitInternalAndInsert(InternalNode node, TIndex key, TValue sepValue, Node rightChild, int insertAt,
		InternalNode?[] ancestors, int[] childIndices, int level) {
		// We have InternalCapacity-1 keys + 1 new key = InternalCapacity keys total
		// Split into left (mid keys) and right (rest), promote middle key to parent
		var totalKeys = node.KeyCount + 1;
		var midIdx = totalKeys / 2;

		// Build temporary arrays with the new key/child inserted (thread-static, zero alloc)
		var tempKeys = GetSplitKeysBuf();
		var tempSepValues = GetSplitSepValuesBuf();
		var tempChildren = GetSplitChildrenBuf();

		// Copy existing keys/children with insertion
		for (int i = 0, j = 0; j < totalKeys; i++, j++) {
			if (j == insertAt) {
				tempKeys[j] = key;
				tempSepValues[j] = sepValue;
				tempChildren[j] = node.Children[i];
				tempChildren[j + 1] = rightChild;
				i--; // don't advance source
			}
			else {
				tempKeys[j] = node.Keys[i];
				tempSepValues[j] = node.SepValues[i];
				// j == insertAt + 1 is already set to rightChild — don't overwrite it
				if (j != insertAt + 1)
					tempChildren[j] = node.Children[i];
				if (j == totalKeys - 1)
					tempChildren[j + 1] = node.Children[i + 1];
			}
		}

		// The promoted pair is (tempKeys[midIdx], tempSepValues[midIdx])
		var promotedKey = tempKeys[midIdx];
		var promotedValue = tempSepValues[midIdx];

		// Left node keeps keys 0..midIdx-1, children 0..midIdx
		node.KeyCount = midIdx;
		Array.Copy(tempKeys, 0, node.Keys, 0, midIdx);
		Array.Copy(tempSepValues, 0, node.SepValues, 0, midIdx);
		for (var i = 0; i <= midIdx; i++)
			node.Children[i] = tempChildren[i];
		// Clear excess slots
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>())
			Array.Clear(node.Keys, midIdx, (InternalCapacity - 1) - midIdx);
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
			Array.Clear(node.SepValues, midIdx, (InternalCapacity - 1) - midIdx);
		for (var i = midIdx + 1; i < InternalCapacity; i++)
			node.Children[i] = null!;

		// Right node gets keys midIdx+1..totalKeys-1, children midIdx+1..totalKeys
		var newNode = new InternalNode();
		var rightKeyCount = totalKeys - midIdx - 1;
		newNode.KeyCount = rightKeyCount;
		Array.Copy(tempKeys, midIdx + 1, newNode.Keys, 0, rightKeyCount);
		Array.Copy(tempSepValues, midIdx + 1, newNode.SepValues, 0, rightKeyCount);
		for (var i = 0; i <= rightKeyCount; i++)
			newNode.Children[i] = tempChildren[midIdx + 1 + i];

		// Promote to parent
		InsertIntoParent(node, promotedKey, promotedValue, newNode, ancestors, childIndices, level);
	}

	// ───────────────────── Remove ─────────────────────

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool Remove(TIndex index, TValue value) {
		lock (_writeLock) {
			return RemoveCore(index, value);
		}
	}

	private bool RemoveCore(TIndex index, TValue value) {
		var ancestors = GetAncestorsBuf();
		var childIndices = GetChildIdxBuf();

		// Composite descent + composite lower bound pinpoint the pair in O(log n) no
		// matter how long the equal-key run is: the pair, if present, sits exactly at
		// the lower-bound position (CompareTo consistent with Equals — see class doc),
		// so a mismatch there is a definitive miss.
		var leaf = FindLeafWithPathComposite(index, value, ancestors, childIndices, out var depth);
		var pos = LeafLowerBoundComposite(leaf, index, value);
		if (pos >= leaf.Count || leaf.Keys[pos].CompareTo(index) != 0
			|| !value.Equals(leaf.Values[pos]))
			return false;

		RemoveFromLeaf(leaf, pos, ancestors, childIndices, depth);
		return true;
	}

	private void RemoveFromLeaf(LeafNode leaf, int exactPos,
		InternalNode?[] ancestors, int[] childIndices, int depth) {
		// Shift elements left
		leaf.Count--;
		if (exactPos < leaf.Count) {
			Array.Copy(leaf.Keys, exactPos + 1, leaf.Keys, exactPos, leaf.Count - exactPos);
			Array.Copy(leaf.Values, exactPos + 1, leaf.Values, exactPos, leaf.Count - exactPos);
		}

		// Clear the last slot
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>())
			leaf.Keys[leaf.Count] = default!;
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
			leaf.Values[leaf.Count] = default!;

		// Handle empty leaf
		if (leaf.Count == 0 && _root != leaf)
			RemoveEmptyLeaf(leaf, ancestors, childIndices, depth);

		_length--;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RemoveEmptyLeaf(LeafNode leaf, InternalNode?[] ancestors, int[] childIndices, int depth) {
		// Unlink from chain
		if (leaf.Prev != null)
			leaf.Prev.Next = leaf.Next;
		if (leaf.Next != null)
			leaf.Next.Prev = leaf.Prev;

		// Update first/last pointers
		if (_firstLeaf == leaf)
			_firstLeaf = leaf.Next ?? leaf; // shouldn't happen if root != leaf
		if (_lastLeaf == leaf)
			_lastLeaf = leaf.Prev ?? leaf;

		// Remove from parent BEFORE retiring: a node may only be retired once no tree
		// pointer leads to it. Retiring first would let the gate reclaim the leaf
		// (quiescent case reclaims immediately) while the parent still routes readers
		// into it — a descent would then dereference the nulled arrays.
		if (depth > 0)
			RemoveFromParent(ancestors, childIndices, depth);

		// Defer the pool return past the reader grace period: lock-free readers
		// (Range*, TryGetMin/Max, Contains) may still hold this node. Its Keys/Values
		// and Next/Prev stay intact until reclamation, so a parked reader continues
		// the chain correctly (documented staleness: it may re-see or miss the
		// removed entries).
		ReaderGate.Retire(leaf);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RemoveFromParent(InternalNode?[] ancestors, int[] childIndices, int depth) {
		var level = depth - 1;
		var parent = ancestors[level]!;
		var childIdx = childIndices[level];

		// Remove the child pointer and associated key
		// If childIdx == 0, remove key[0] and child[0]
		// If childIdx > 0, remove key[childIdx-1] and child[childIdx]
		var keyIdx = childIdx > 0 ? childIdx - 1 : 0;

		// Shift keys left
		if (keyIdx < parent.KeyCount - 1) {
			Array.Copy(parent.Keys, keyIdx + 1, parent.Keys, keyIdx, parent.KeyCount - keyIdx - 1);
			Array.Copy(parent.SepValues, keyIdx + 1, parent.SepValues, keyIdx, parent.KeyCount - keyIdx - 1);
		}

		// Shift children left
		if (childIdx < parent.KeyCount)
			Array.Copy(parent.Children, childIdx + 1, parent.Children, childIdx,
				parent.KeyCount - childIdx);

		parent.KeyCount--;

		// Clear last slots
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>())
			parent.Keys[parent.KeyCount] = default!;
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
			parent.SepValues[parent.KeyCount] = default!;
		parent.Children[parent.KeyCount + 1] = null!;

		// If parent is now empty and is root, make the remaining child the new root
		if (parent.KeyCount == 0 && parent == _root) {
			_root = parent.Children[0];
			ReaderGate.Retire(parent);
		}
		else if (parent.KeyCount == 0 && level > 0) {
			// Parent is empty but not root: replace the grandparent's pointer to parent
			// with parent's sole remaining child. A recursive RemoveFromParent here would
			// drop that child entirely, orphaning its subtree.
			ancestors[level - 1]!.Children[childIndices[level - 1]] = parent.Children[0];
			ReaderGate.Retire(parent);
		}
	}

	// ───────────────────── Update ─────────────────────

	/// <summary>
	///   Moves a value from one index key to another — the range-index update pattern
	///   (every cache update re-stamps the entry's timestamp) — under a single write
	///   lock. Semantics: when the keys differ, this is Add(newIndex, value) followed
	///   by Remove(oldIndex, value) (so if the old pair is absent, the new pair is
	///   still inserted); when the keys compare equal, nothing is mutated — the pair
	///   simply stays, unlike the literal two-call sequence, which would net-delete
	///   it. Insert-before-remove keeps the value visible to lock-free readers
	///   throughout (it may transiently be seen under both keys). Returns true if the
	///   value was present under oldIndex.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool Update(TIndex oldIndex, TIndex newIndex, TValue value) {
		lock (_writeLock) {
			if (oldIndex.CompareTo(newIndex) == 0)
				return ContainsCore(oldIndex, value);

			AddCore(newIndex, value);
			return RemoveCore(oldIndex, value);
		}
	}

	// ───────────────────── Contains ─────────────────────

	public bool Contains(TIndex index, TValue value) {
		var slot = ReaderGate.Enter();
		try {
			return ContainsCore(index, value);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool ContainsCore(TIndex index, TValue value) {
		var leaf = FindLeafComposite(index, value);
		var pos = LeafLowerBoundComposite(leaf, index, value);
		var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
		// Caller-supplied index/value stay the receivers: this is a lock-free reader
		// and the probed slot may be a vacated (nulled) one under a racing shrink.
		return pos < count && index.CompareTo(leaf.Keys[pos]) == 0
			&& value.Equals(leaf.Values[pos]);
	}

	// ───────────────────── TryGetMin / TryGetMax ─────────────────────

	public bool TryGetMin(out TIndex index, out TValue value) {
		var slot = ReaderGate.Enter();
		try {
			return TryGetMinCore(out index, out value);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool TryGetMinCore(out TIndex index, out TValue value) {
		var leaf = _firstLeaf;
		if (Volatile.Read(ref leaf.Count) > 0) {
			index = leaf.Keys[0];
			value = leaf.Values[0];
			return true;
		}

		index = default!;
		value = default!;
		return false;
	}

	public bool TryGetMax(out TIndex index, out TValue value) {
		var slot = ReaderGate.Enter();
		try {
			return TryGetMaxCore(out index, out value);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool TryGetMaxCore(out TIndex index, out TValue value) {
		var leaf = _lastLeaf;
		var count = Volatile.Read(ref leaf.Count);
		if (count > 0) {
			index = leaf.Keys[count - 1];
			value = leaf.Values[count - 1];
			return true;
		}

		index = default!;
		value = default!;
		return false;
	}

	// ───────────────────── Range queries ─────────────────────

	internal interface IResultAggregator : IDisposable {
		void Add(TIndex index, TValue value);
	}

	/// <summary>
	///   Range query [from, to] — inclusive on both bounds.
	/// </summary>
	public void Range<TResultsAggregator>(TIndex from, TIndex to, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var slot = ReaderGate.Enter();
		try {
			RangeCore(from, to, ref agg);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RangeCore<TResultsAggregator>(TIndex from, TIndex to, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var leaf = FindLeafForRange(from);
		var pos = LeafLowerBound(leaf, from);

		while (leaf != null) {
			ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
			ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);
			var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release

			for (var i = pos; i < count; i++) {
				ref var key = ref Unsafe.Add(ref keys, i);
				if (key.CompareTo(to) > 0)
					return;
				agg.Add(key, Unsafe.Add(ref values, i));
			}

			leaf = leaf.Next;
			pos = 0;
		}
	}

	/// <summary>
	///   Range query [start, ∞) — from start inclusive.
	/// </summary>
	public void RangeFrom<TResultsAggregator>(TIndex start, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var slot = ReaderGate.Enter();
		try {
			RangeFromCore(start, ref agg);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RangeFromCore<TResultsAggregator>(TIndex start, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var leaf = FindLeafForRange(start);
		var pos = LeafLowerBound(leaf, start);

		while (leaf != null) {
			// Indexed access measured faster than GetArrayDataReference refs for this
			// simple scan shape (the JIT's addressing + layout win over byref chains).
			var keys = leaf.Keys;
			var values = leaf.Values;
			var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release

			for (var i = pos; i < count; i++)
				agg.Add(keys[i], values[i]);

			leaf = leaf.Next;
			pos = 0;
		}
	}

	/// <summary>
	///   Range query (-∞, to] — up to to inclusive.
	/// </summary>
	public void RangeTo<TResultsAggregator>(TIndex to, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var slot = ReaderGate.Enter();
		try {
			RangeToCore(to, ref agg);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RangeToCore<TResultsAggregator>(TIndex to, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var leaf = _firstLeaf;

		while (leaf != null) {
			ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
			ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);
			var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release

			for (var i = 0; i < count; i++) {
				ref var key = ref Unsafe.Add(ref keys, i);
				if (key.CompareTo(to) > 0)
					return;
				agg.Add(key, Unsafe.Add(ref values, i));
			}

			leaf = leaf.Next;
		}
	}

	/// <summary>
	///   Range query (-∞, to) — up to to exclusive.
	/// </summary>
	public void RangeToExclusive<TResultsAggregator>(TIndex to, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var slot = ReaderGate.Enter();
		try {
			RangeToExclusiveCore(to, ref agg);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RangeToExclusiveCore<TResultsAggregator>(TIndex to, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var leaf = _firstLeaf;

		while (leaf != null) {
			ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
			ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);
			var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release

			for (var i = 0; i < count; i++) {
				ref var key = ref Unsafe.Add(ref keys, i);
				if (key.CompareTo(to) >= 0)
					return;
				agg.Add(key, Unsafe.Add(ref values, i));
			}

			leaf = leaf.Next;
		}
	}

	/// <summary>
	///   Range query (start, ∞) — from start exclusive.
	/// </summary>
	public void RangeFromExclusive<TResultsAggregator>(TIndex start, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var slot = ReaderGate.Enter();
		try {
			RangeFromExclusiveCore(start, ref agg);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RangeFromExclusiveCore<TResultsAggregator>(TIndex start, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var leaf = FindLeafForRange(start);
		var pos = LeafUpperBound(leaf, start);

		// A run of keys equal to the exclusive bound can span leaves: pos == Count means
		// every key in this leaf is <= start, so re-apply the upper bound on the next
		// leaf. Once a leaf has pos < Count, every later key in the chain is > start.
		while (pos == leaf.Count) {
			var next = leaf.Next;
			if (next == null)
				return;
			leaf = next;
			pos = LeafUpperBound(leaf, start);
		}

		while (leaf != null) {
			// Indexed access measured faster than GetArrayDataReference refs for this
			// simple scan shape (the JIT's addressing + layout win over byref chains).
			var keys = leaf.Keys;
			var values = leaf.Values;
			var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release

			for (var i = pos; i < count; i++)
				agg.Add(keys[i], values[i]);

			leaf = leaf.Next;
			pos = 0;
		}
	}

	/// <summary>
	///   Range query with custom inclusive/exclusive bounds.
	/// </summary>
	public void RangeCustom<TResultsAggregator>(TIndex from, TIndex to, bool includeFrom, bool includeTo,
		ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var slot = ReaderGate.Enter();
		try {
			RangeCustomCore(from, to, includeFrom, includeTo, ref agg);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RangeCustomCore<TResultsAggregator>(TIndex from, TIndex to, bool includeFrom, bool includeTo,
		ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		var leaf = FindLeafForRange(from);
		int pos;
		if (includeFrom) {
			pos = LeafLowerBound(leaf, from);
		}
		else {
			// Exclusive lower bound: skip the run of keys equal to `from` across leaves
			// (see RangeFromExclusive).
			pos = LeafUpperBound(leaf, from);
			while (pos == leaf.Count) {
				var next = leaf.Next;
				if (next == null)
					return;
				leaf = next;
				pos = LeafUpperBound(leaf, from);
			}
		}

		while (leaf != null) {
			ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
			ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);
			var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release

			for (var i = pos; i < count; i++) {
				ref var key = ref Unsafe.Add(ref keys, i);
				var cmp = key.CompareTo(to);
				if (includeTo ? cmp > 0 : cmp >= 0)
					return;
				agg.Add(key, Unsafe.Add(ref values, i));
			}

			leaf = leaf.Next;
			pos = 0;
		}
	}

	// ───────────────────── Dispose ─────────────────────

	public void Dispose() {
		// Walk leaf chain and return all arrays
		var leaf = _firstLeaf;
		while (leaf != null) {
			var next = leaf.Next;
			leaf.ReturnToPool();
			leaf = next;
		}

		// Walk internal nodes via BFS/DFS and return key arrays
		DisposeInternalNodes(_root);

		// Nodes retired earlier may still sit in the gate limbo — give it a chance to
		// reclaim now that this tree no longer produces reader traffic.
		ReaderGate.TryDrain();
	}

		private static void DisposeInternalNodes(Node node) {
				if (node is not InternalNode intern)
						return;
				var children = intern.Children;
				if (children == null)
						return; // already disposed — second Dispose() is a no-op
				for (var i = 0; i <= intern.KeyCount; i++)
						DisposeInternalNodes(children[i]);
				intern.ReturnToPool();
		}
}
