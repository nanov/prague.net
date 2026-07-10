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
/// </summary>
internal sealed class PooledBTree<TIndex, TValue> : IDisposable
	where TIndex : IComparable<TIndex>
	where TValue : IEquatable<TValue> {
	private const int LeafCapacity = 64;
	private const int InternalCapacity = 64; // max children per internal node
	private const int MaxDepth = 8; // 64^8 > 10^14, more than enough

	[ThreadStatic] private static InternalNode?[]? _ancestorsBuf;
	[ThreadStatic] private static int[]? _childIdxBuf;
	[ThreadStatic] private static TIndex[]? _splitKeysBuf;
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
		public Node[] Children;
		public int KeyCount; // number of separator keys; child count = KeyCount + 1

		public InternalNode() {
			Keys = ArrayPool<TIndex>.Shared.Rent(InternalCapacity - 1);
			Children = ArrayPool<Node>.Shared.Rent(InternalCapacity);
		}

		public override void ReclaimToPool() => ReturnToPool();

		public void ReturnToPool() {
			var keys = Keys;
			var children = Children;
			Keys = null!;
			Children = null!;
			try {
				ArrayPool<TIndex>.Shared.Return(keys, RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>());
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
	///   Finds the leaf node that should contain the given index.
	///   Also populates the ancestors array (path from root to leaf's parent)
	///   and childIndices array (which child index was taken at each level).
	///   Returns the depth (number of internal nodes traversed).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private LeafNode FindLeaf(TIndex index) {
		var node = _root;
		while (node is InternalNode intern) {
			var childIdx = FindChildIndex(intern, index);
			var child = intern.Children[childIdx];
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

	/// <summary>
	///   Finds the leaf and records the path for potential splits.
	///   ancestors[i] = internal node at level i, childIndices[i] = child index taken.
	///   Returns depth (0 if root is a leaf).
	/// </summary>
	private LeafNode FindLeafWithPath(TIndex index, InternalNode?[] ancestors, int[] childIndices,
		out int depth) {
		var node = _root;
		depth = 0;
		while (node is InternalNode intern) {
			var childIdx = FindChildIndex(intern, index);
			ancestors[depth] = intern;
			childIndices[depth] = childIdx;
			depth++;
			node = intern.Children[childIdx];
		}

		return Unsafe.As<LeafNode>(node);
	}

	/// <summary>
	///   Binary search within an internal node to find the child index.
	///   Keys equal to a separator route RIGHT (separator = first key of right child).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FindChildIndex(InternalNode node, TIndex index) {
		// Bounds checks elided: mid <= KeyCount - 1 <= InternalCapacity - 2 and the
		// rented separator array is always >= InternalCapacity - 1 long.
		ref var keys = ref MemoryMarshal.GetArrayDataReference(node.Keys);
		var lo = 0;
		var hi = node.KeyCount - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = index.CompareTo(Unsafe.Add(ref keys, mid));
			if (cmp >= 0) // equal goes right
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}

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
				// See FindLeaf: transient race with an in-place structural shift.
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

	/// <summary>
	///   Searches for an exact (index, value) pair in a leaf starting from pos.
	///   Returns the position if found, -1 otherwise.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FindExact(LeafNode leaf, TIndex index, TValue value, int startPos) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
		ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);
		var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
		for (var i = startPos; i < count; i++) {
			var cmp = Unsafe.Add(ref keys, i).CompareTo(index);
			if (cmp > 0) break;
			if (cmp == 0 && value.Equals(Unsafe.Add(ref values, i)))
				return i;
		}

		return -1;
	}

	/// <summary>
	///   Cold path for runs of equal keys spanning leaves: the fast-path leaf's run
	///   portion missed, and pos == 0 means the run may extend into earlier leaves.
	///   Walks backwards while the previous leaf's last key still equals the index.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ContainsInPrevLeaves(LeafNode leaf, TIndex index, TValue value) {
		var prev = leaf.Prev;
		while (prev != null) {
			var count = Volatile.Read(ref prev.Count); // acquire: pairs with InsertIntoLeaf's release
			if (count == 0 || prev.Keys[count - 1].CompareTo(index) != 0)
				break;

			for (var i = count - 1; i >= 0; i--) {
				if (prev.Keys[i].CompareTo(index) != 0)
					break;
				if (value.Equals(prev.Values[i]))
					return true;
			}

			prev = prev.Prev;
		}

		return false;
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
		// entries usually sort past the current maximum. A strictly-greater key cannot
		// be a duplicate and appends into the (non-full) last leaf with no descent, no
		// duplicate scan and no path bookkeeping — O(1) instead of O(log n).
		var last = _lastLeaf;
		var lastCount = last.Count; // writer-owned under the lock: plain read is exact
		if (lastCount > 0 && lastCount < LeafCapacity
			&& index.CompareTo(last.Keys[lastCount - 1]) > 0) {
			last.Keys[lastCount] = index;
			last.Values[lastCount] = value;
			// Release-publish the grown count AFTER the slot stores (see InsertIntoLeaf).
			Volatile.Write(ref last.Count, lastCount + 1);
			_length++;
			return true;
		}

		var ancestors = GetAncestorsBuf();
		var childIndices = GetChildIdxBuf();

		var leaf = FindLeafWithPath(index, ancestors, childIndices, out var depth);

		// Check for duplicate — including earlier leaves when the equal-key run may
		// span a leaf boundary (pos == 0; see ContainsInPrevLeaves)
		var pos = LeafLowerBound(leaf, index);
		if (FindExact(leaf, index, value, pos) >= 0)
			return false;
		if (pos == 0 && ContainsInPrevLeaves(leaf, index, value))
			return false;

		// Find insertion point considering value ordering for stable placement
		// For entries with the same index, we insert at the upper bound to append at end of group
		var insertPos = pos;
		// Advance past same-index entries to insert at end of group
		while (insertPos < leaf.Count && leaf.Keys[insertPos].CompareTo(index) == 0)
			insertPos++;

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

		// Promote separator key to parent
		var promotedKey = newLeaf.Keys[0];
		InsertIntoParent(leaf, promotedKey, newLeaf, ancestors, childIndices, depth);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InsertIntoParent(Node leftChild, TIndex key, Node rightChild,
		InternalNode?[] ancestors, int[] childIndices, int depth) {
		if (depth == 0) {
			// Root was a leaf, create new root. Release-publish: the new root's
			// freshly-rented arrays must be visible before the root pointer is.
			var newRoot = new InternalNode();
			newRoot.Keys[0] = key;
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
				Array.Copy(parent.Children, insertAt + 1, parent.Children, insertAt + 2,
					parent.KeyCount - insertAt);
			}

			parent.Keys[insertAt] = key;
			// Release-publish the new node: its rented arrays were filled with plain
			// stores and must be visible before any pointer to it.
			Volatile.Write(ref parent.Children[insertAt + 1], rightChild);
			parent.KeyCount++;
			return;
		}

		// Parent is full — split internal node
		SplitInternalAndInsert(parent, key, rightChild, childIndices[level], ancestors, childIndices, level);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SplitInternalAndInsert(InternalNode node, TIndex key, Node rightChild, int insertAt,
		InternalNode?[] ancestors, int[] childIndices, int level) {
		// We have InternalCapacity-1 keys + 1 new key = InternalCapacity keys total
		// Split into left (mid keys) and right (rest), promote middle key to parent
		var totalKeys = node.KeyCount + 1;
		var midIdx = totalKeys / 2;

		// Build temporary arrays with the new key/child inserted (thread-static, zero alloc)
		var tempKeys = GetSplitKeysBuf();
		var tempChildren = GetSplitChildrenBuf();

		// Copy existing keys/children with insertion
		for (int i = 0, j = 0; j < totalKeys; i++, j++) {
			if (j == insertAt) {
				tempKeys[j] = key;
				tempChildren[j] = node.Children[i];
				tempChildren[j + 1] = rightChild;
				i--; // don't advance source
			}
			else {
				tempKeys[j] = node.Keys[i];
				// j == insertAt + 1 is already set to rightChild — don't overwrite it
				if (j != insertAt + 1)
					tempChildren[j] = node.Children[i];
				if (j == totalKeys - 1)
					tempChildren[j + 1] = node.Children[i + 1];
			}
		}

		// The promoted key is tempKeys[midIdx]
		var promotedKey = tempKeys[midIdx];

		// Left node keeps keys 0..midIdx-1, children 0..midIdx
		node.KeyCount = midIdx;
		Array.Copy(tempKeys, 0, node.Keys, 0, midIdx);
		for (var i = 0; i <= midIdx; i++)
			node.Children[i] = tempChildren[i];
		// Clear excess slots
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>())
			Array.Clear(node.Keys, midIdx, (InternalCapacity - 1) - midIdx);
		for (var i = midIdx + 1; i < InternalCapacity; i++)
			node.Children[i] = null!;

		// Right node gets keys midIdx+1..totalKeys-1, children midIdx+1..totalKeys
		var newNode = new InternalNode();
		var rightKeyCount = totalKeys - midIdx - 1;
		newNode.KeyCount = rightKeyCount;
		Array.Copy(tempKeys, midIdx + 1, newNode.Keys, 0, rightKeyCount);
		for (var i = 0; i <= rightKeyCount; i++)
			newNode.Children[i] = tempChildren[midIdx + 1 + i];

		// Promote to parent
		InsertIntoParent(node, promotedKey, newNode, ancestors, childIndices, level);
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

		var leaf = FindLeafWithPath(index, ancestors, childIndices, out var depth);

		var pos = LeafLowerBound(leaf, index);
		var exactPos = FindExact(leaf, index, value, pos);
		if (exactPos >= 0) {
			RemoveFromLeaf(leaf, exactPos, ancestors, childIndices, depth);
			return true;
		}

		// Fast-path miss. If the run of equal keys begins inside this leaf (pos > 0 —
		// a smaller key precedes it), no earlier leaf can hold the pair: the miss is
		// definitive. Only when the run may have started in earlier leaves (pos == 0
		// and the previous leaf ends with an equal key) can the pair hide elsewhere.
		if (pos > 0 || leaf.Prev == null
			|| leaf.Prev.Keys[leaf.Prev.Count - 1].CompareTo(index) != 0)
			return false;

		return RemoveFromSubtree(_root, index, value, ancestors, childIndices, 0);
	}

	/// <summary>
	///   Cold path for runs of equal keys spanning leaves: descends into every child
	///   whose key range can contain (index, value) and removes the first exact match.
	///   The recursion keeps ancestors/childIndices valid for the leaf where the pair
	///   is actually found so structural cleanup works. Depth is bounded by MaxDepth.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool RemoveFromSubtree(Node node, TIndex index, TValue value,
		InternalNode?[] ancestors, int[] childIndices, int depth) {
		if (node is LeafNode leaf) {
			var pos = LeafLowerBound(leaf, index);
			var exactPos = FindExact(leaf, index, value, pos);
			if (exactPos < 0)
				return false;

			RemoveFromLeaf(leaf, exactPos, ancestors, childIndices, depth);
			return true;
		}

		var intern = Unsafe.As<InternalNode>(node);
		var first = FindChildIndexLeft(intern, index);
		var last = FindChildIndex(intern, index);
		for (var childIdx = first; childIdx <= last; childIdx++) {
			ancestors[depth] = intern;
			childIndices[depth] = childIdx;
			if (RemoveFromSubtree(intern.Children[childIdx], index, value, ancestors, childIndices, depth + 1))
				return true;
		}

		return false;
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
		if (keyIdx < parent.KeyCount - 1)
			Array.Copy(parent.Keys, keyIdx + 1, parent.Keys, keyIdx, parent.KeyCount - keyIdx - 1);

		// Shift children left
		if (childIdx < parent.KeyCount)
			Array.Copy(parent.Children, childIdx + 1, parent.Children, childIdx,
				parent.KeyCount - childIdx);

		parent.KeyCount--;

		// Clear last slots
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>())
			parent.Keys[parent.KeyCount] = default!;
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
		var leaf = FindLeaf(index);
		var pos = LeafLowerBound(leaf, index);
		if (FindExact(leaf, index, value, pos) >= 0)
			return true;

		// pos > 0 ⇒ a smaller key precedes the run in this leaf ⇒ miss is definitive.
		if (pos > 0)
			return false;

		return ContainsInPrevLeaves(leaf, index, value);
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
