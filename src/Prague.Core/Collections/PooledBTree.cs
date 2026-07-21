namespace Prague.Core.Collections;

using Prague.Core.Utils;
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
	///   - Duplicate-key runs: the tree starts as a plain key-ordered B+tree and stays
	///     that way — running main's key-only search at main's cost — until an Add first
	///     observes an existing equal index key. From then on the sort key is the
	///     composite (key, hash(value)) pair: equal-key runs are hash-ordered, internal
	///     separators carry the hash half, and Add/Remove/Contains binary-search to the
	///     exact pair — O(log n) instead of O(run length) — even when a run spans many
	///     leaves. No rebuild happens at the transition: with unique keys, composite
	///     order IS key order, so the existing layout is already valid for both searches.
	///     The only precondition on TValue is GetHashCode consistent with Equals, which
	///     IEquatable&lt;TValue&gt; already implies and every Dictionary here relies on.
	///     Hash ties are resolved by an Equals probe over the collision run, so a
	///     collision costs time, never correctness; a degenerate hash degrades toward
	///     the old run scan. Order WITHIN a run of equal index keys is unspecified.
	///   - The key-only path is kept as main's full algorithm (run-scanning FindExact,
	///     backwards ContainsInPrevLeaves) rather than a single-slot probe. That is what
	///     makes a lock-free reader observing a STALE _hasDuplicateKeys == false correct
	///     rather than merely lucky: it returns the right answer on a tree that already
	///     has duplicates, just more slowly. Writers read the flag under the write lock.
	///   - Ref-typed values' tiebreak hashes ride beside them (leaf ValueHashes, internal
	///     SepValueHashes), maintained in lockstep with each Values/SepValues move and
	///     written before the Count/link store that publishes the slot. Composite descents
	///     and probes compare stored ints instead of re-hashing values (Marvin32 on string
	///     values made stored-value re-hashing the dominant duplicate-run cost). Value-typed
	///     values skip the arrays entirely (StoreValueHashes, JIT-folded) and recompute the
	///     identity-class hash on read: Task 10 measured always-on storage as pure carry
	///     cost there (RemoveAll_Composite +2.3–6%, +4 bytes per entry never read).
/// </summary>
internal sealed class PooledBTree<TIndex, TValue> : IDisposable
	where TIndex : IComparable<TIndex>
	where TValue : IEquatable<TValue> {
	private const int LeafCapacity = 64;
	private const int InternalCapacity = 64; // max children per internal node
	private const int MaxDepth = 8; // 64^8 > 10^14, more than enough

	// JIT-folded per closed generic: value-type tiebreak hashes are identity-class raw
	// GetHashCode — recomputing beats maintaining a third parallel array (Task 10 measured
	// RemoveAll_Composite +2.3–6% with always-on storage). Ref-type hashes (Marvin over
	// strings) are O(length): stored once at insert, read forever.
	private static readonly bool StoreValueHashes = !typeof(TValue).IsValueType;

	[ThreadStatic] private static InternalNode?[]? _ancestorsBuf;
	[ThreadStatic] private static int[]? _childIdxBuf;
	[ThreadStatic] private static TIndex[]? _splitKeysBuf;
	[ThreadStatic] private static TValue[]? _splitSepValuesBuf;
	[ThreadStatic] private static int[]? _splitSepValueHashesBuf;
	[ThreadStatic] private static Node[]? _splitChildrenBuf;

	private readonly Lock _writeLock = new();
	private Node _root;
	private LeafNode _firstLeaf;
	private LeafNode _lastLeaf;
	private int _length;

	// False until an Add first observes an existing equal index key. While false the
	// tree is a plain key-ordered B+tree and every operation runs main's key-only path
	// at main's cost; once true the composite (key, hash) path takes over and keeps
	// point operations O(log n) inside duplicate-key runs. Monotonic: never reset.
	// No rebuild is needed at the transition — with unique keys, composite order IS key
	// order, so the existing layout is already valid for both searches.
	private bool _hasDuplicateKeys;

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
		public int[]? ValueHashes; // ValueHashes[i] == HashOf(Values[i]) for every live slot; null unless StoreValueHashes
		public int Count;
		public LeafNode? Next;
		public LeafNode? Prev;

		public LeafNode() {
			Keys = PragueArrayPool<TIndex>.Pool.Rent(LeafCapacity);
			Values = PragueArrayPool<TValue>.Pool.Rent(LeafCapacity);
			ValueHashes = StoreValueHashes ? PragueArrayPool<int>.Pool.Rent(LeafCapacity) : null;
		}

		public override void ReclaimToPool() => ReturnToPool();

		public void ReturnToPool() {
			var keys = Keys;
			var values = Values;
			var valueHashes = ValueHashes;
			Keys = null!;
			Values = null!;
			ValueHashes = null;
			try {
				PragueArrayPool<TIndex>.Pool.Return(keys, RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>());
			}
			catch (ArgumentException) { }

			try {
				PragueArrayPool<TValue>.Pool.Return(values, RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
			}
			catch (ArgumentException) { }

			if (valueHashes != null) {
				try {
					PragueArrayPool<int>.Pool.Return(valueHashes, false); // ints — no clear
				}
				catch (ArgumentException) { }
			}
		}
	}

	private sealed class InternalNode : Node {
		public TIndex[] Keys; // separator keys
		public TValue[] SepValues; // separator values: SepValues[i] pairs with Keys[i] (first pair of Children[i + 1])
		public int[]? SepValueHashes; // SepValueHashes[i] == HashOf(SepValues[i]); null unless StoreValueHashes
		public Node[] Children;
		public int KeyCount; // number of separator keys; child count = KeyCount + 1

		public InternalNode() {
			Keys = PragueArrayPool<TIndex>.Pool.Rent(InternalCapacity - 1);
			SepValues = PragueArrayPool<TValue>.Pool.Rent(InternalCapacity - 1);
			SepValueHashes = StoreValueHashes ? PragueArrayPool<int>.Pool.Rent(InternalCapacity - 1) : null;
			Children = PragueArrayPool<Node>.Pool.Rent(InternalCapacity);
		}

		public override void ReclaimToPool() => ReturnToPool();

		public void ReturnToPool() {
			var keys = Keys;
			var sepValues = SepValues;
			var sepValueHashes = SepValueHashes;
			var children = Children;
			Keys = null!;
			SepValues = null!;
			SepValueHashes = null;
			Children = null!;
			try {
				PragueArrayPool<TIndex>.Pool.Return(keys, RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>());
			}
			catch (ArgumentException) { }

			try {
				PragueArrayPool<TValue>.Pool.Return(sepValues, RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
			}
			catch (ArgumentException) { }

			if (sepValueHashes != null) {
				try {
					PragueArrayPool<int>.Pool.Return(sepValueHashes, false); // ints — no clear
				}
				catch (ArgumentException) { }
			}

			try {
				PragueArrayPool<Node>.Pool.Return(children, true); // clear refs
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

	// Stored mode only: callers rent and touch this scratch under StoreValueHashes.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int[] GetSplitSepValueHashesBuf() {
		var buf = _splitSepValueHashesBuf;
		if (buf == null) {
			buf = new int[InternalCapacity]; // mirrors GetSplitSepValuesBuf
			_splitSepValueHashesBuf = buf;
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
	///   Null-tolerant raw (unmixed) hash of the value half — the tree's tiebreaker inside a
	///   run of equal index keys. Raw, not DefaultKeyComparer's Fibonacci-mixed form: the mix
	///   would destroy the value-order ↔ hash-order correlation that the O(1) monotonic-append
	///   fast path depends on for batch-stamped inserts (equal key, ascending id), and being a
	///   bijection it removes no collisions either. Strings use string.GetHashCode (Marvin32,
	///   ordinal-consistent, process-stable) — the same hash the store threads in precomputed,
	///   so pre-hashed entry points consume it for free (2026-07-20 single-hash spec; DJB2
	///   tiebreak retired). Hash ties are resolved by an Equals probe, so a collision costs
	///   time, never correctness. Called on incoming values (public wrappers, Contains,
	///   pre-hashed debug asserts) and, through HashAt in value-type trees, on stored
	///   slots — identity-class there, so recomputing beats carrying a parallel array.
	///   Ref-typed stored slots carry their hash in ValueHashes / SepValueHashes instead.
	///   Still null-tolerant — an incoming default value hashes safely.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int HashOf(TValue value) {
		if (typeof(TValue).IsValueType)
			return value!.GetHashCode();
		return value is null ? 0 : value.GetHashCode();
	}

	/// <summary>
	///   Mode-folded tiebreak hash of stored slot i (JIT-folded per closed generic): the
	///   stored-hash read when StoreValueHashes, an identity-class recompute from the value
	///   otherwise — value-type instantiations fold back to the compute-on-the-fly codegen,
	///   ref-type ones to the stored-array read.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int HashAt(TValue[] values, int[]? hashes, int i) =>
		StoreValueHashes ? hashes![i] : HashOf(values[i]);

	/// <summary>
	///   Hoisted-ref variant for the binary-search loops: callers keep their
	///   GetArrayDataReference bounds-check elision; the hashes ref is NullRef (and
	///   provably unread) when !StoreValueHashes.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int HashAt(ref TValue values, ref int hashes, int i) =>
		StoreValueHashes ? Unsafe.Add(ref hashes, i) : HashOf(Unsafe.Add(ref values, i));

	/// <summary>
	///   Composite FindChildIndex: separators are (key, value) pairs, entries equal to
	///   a separator route RIGHT (separator = first pair of the right child).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FindChildIndexComposite(InternalNode node, TIndex index, int valueHash) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(node.Keys);
		// Hoisted out of the loop. The original resolved this inside the tie-branch so that
		// unique-key descents never loaded a second array header; with the duplicate-key
		// flag, unique-key trees never reach the composite descent at all, so that
		// protection is dead weight and the repeated resolve is pure cost. Per mode exactly
		// one of the two refs below is live (HashAt folds); the other load is dead code.
		ref var sepValues = ref MemoryMarshal.GetArrayDataReference(node.SepValues);
		ref var sepValueHashes = ref StoreValueHashes
			? ref MemoryMarshal.GetArrayDataReference(node.SepValueHashes!)
			: ref Unsafe.NullRef<int>();
		var lo = 0;
		var hi = node.KeyCount - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = index.CompareTo(Unsafe.Add(ref keys, mid));
			if (cmp == 0)
				cmp = valueHash.CompareTo(HashAt(ref sepValues, ref sepValueHashes, mid));

			// Equal goes RIGHT — the same routing the key-only tree used, which is what
			// keeps the unique-key path free of extra leaf hops. (key, hash) does NOT
			// uniquely identify an entry, so a split inside a collision run leaves a
			// separator equal to every entry in it; the entries left behind are reached
			// by TryFindPair's BACKWARD scan, exactly as main reached equal-key runs.
			if (cmp >= 0)
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
	private LeafNode FindLeafComposite(TIndex index, int valueHash) {
		var node = _root;
		while (node is InternalNode intern) {
			var child = intern.Children[FindChildIndexComposite(intern, index, valueHash)];
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
	///   Composite descent that records the path for structural maintenance:
	///   ancestors[i] = internal node at level i, childIndices[i] = child index taken.
	///   Writer-only (exact under the lock).
	/// </summary>
	private LeafNode FindLeafWithPathComposite(TIndex index, int valueHash,
		InternalNode?[] ancestors, int[] childIndices, out int depth) {
		var node = _root;
		depth = 0;
		while (node is InternalNode intern) {
			var childIdx = FindChildIndexComposite(intern, index, valueHash);
			ancestors[depth] = intern;
			childIndices[depth] = childIdx;
			depth++;
			node = intern.Children[childIdx];
		}

		return Unsafe.As<LeafNode>(node);
	}

	/// <summary>
	///   Composite LeafLowerBound: first position where (Keys[pos], Values[pos]) >=
	///   (index, value) in composite order. The caller-supplied index stays the CompareTo
	///   receiver (same orientation as FindChildIndexComposite): a lock-free reader racing
	///   a shrink may observe a stale Count and probe a vacated slot, which for ref-typed
	///   keys reads as null — safe as an argument, an NRE as a receiver. The value half is
	///   a stored-hash read (ValueHashes) in stored mode; in value-type trees HashAt
	///   recomputes from the slot, whose vacated read is a struct default — hashes safely.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int LeafLowerBoundComposite(LeafNode leaf, TIndex index, int valueHash) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
		ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values); // hoisted — see FindChildIndexComposite
		ref var valueHashes = ref StoreValueHashes
			? ref MemoryMarshal.GetArrayDataReference(leaf.ValueHashes!)
			: ref Unsafe.NullRef<int>();
		var lo = 0;
		var hi = Volatile.Read(ref leaf.Count) - 1; // acquire: pairs with InsertIntoLeaf's release
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = index.CompareTo(Unsafe.Add(ref keys, mid));
			if (cmp == 0)
				cmp = valueHash.CompareTo(HashAt(ref values, ref valueHashes, mid));

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
	///   The caller supplies the acquire-read count and MUST bound its subsequent
	///   emission by the same value: a fresher count could expose slots shifted after
	///   this search ran, emitting entries outside the requested range. The search
	///   index is the CompareTo receiver — a lock-free reader racing a shrink may
	///   probe a vacated slot, which for ref-typed keys reads as null (safe as an
	///   argument, an NRE as a receiver).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int LeafLowerBound(LeafNode leaf, TIndex index, int count) {
		// Bounds checks elided: mid <= count - 1 <= LeafCapacity - 1 and the rented
		// key array is always >= LeafCapacity long.
		ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
		var lo = 0;
		var hi = count - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = index.CompareTo(Unsafe.Add(ref keys, mid));
			if (cmp > 0)
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}

	/// <summary>
	///   Binary search within a leaf for the first position where Keys[pos] > index.
	///   Same count and receiver discipline as <see cref="LeafLowerBound" />.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int LeafUpperBound(LeafNode leaf, TIndex index, int count) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
		var lo = 0;
		var hi = count - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = index.CompareTo(Unsafe.Add(ref keys, mid));
			if (cmp >= 0)
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}

	// ───────────────────── Key-only search — unique-key mode ─────────────────────
	// Verbatim main behaviour, used while the tree has never seen a duplicate index key.
	// Kept EXACT (run-scanning FindExact, backwards ContainsInPrevLeaves) rather than
	// reduced to a single-slot probe: that is what makes a lock-free reader observing a
	// stale _hasDuplicateKeys == false still CORRECT, merely slower, instead of wrong.
	// Receiver discipline is upgraded over main: the caller-supplied key/value is always
	// the CompareTo/Equals receiver, so a vacated slot read as null is an argument.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FindChildIndex(InternalNode node, TIndex index) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(node.Keys);
		var lo = 0;
		var hi = node.KeyCount - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			if (index.CompareTo(Unsafe.Add(ref keys, mid)) >= 0) // equal goes right
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private LeafNode FindLeaf(TIndex index) {
		var node = _root;
		while (node is InternalNode intern) {
			var child = intern.Children[FindChildIndex(intern, index)];
			if (child == null) {
				node = _root; // raced an in-place structural shift — restart
				continue;
			}

			node = child;
		}

		return Unsafe.As<LeafNode>(node);
	}

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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FindExact(LeafNode leaf, TIndex index, TValue value, int startPos) {
		ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
		ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);
		var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
		for (var i = startPos; i < count; i++) {
			var cmp = index.CompareTo(Unsafe.Add(ref keys, i));
			if (cmp < 0) break;
			if (cmp == 0 && value.Equals(Unsafe.Add(ref values, i)))
				return i;
		}

		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ContainsInPrevLeaves(LeafNode leaf, TIndex index, TValue value) {
		var prev = leaf.Prev;
		while (prev != null) {
			var count = Volatile.Read(ref prev.Count); // acquire: pairs with InsertIntoLeaf's release
			if (count == 0 || index.CompareTo(prev.Keys[count - 1]) != 0)
				break;

			for (var i = count - 1; i >= 0; i--) {
				if (index.CompareTo(prev.Keys[i]) != 0)
					break;
				if (value.Equals(prev.Values[i]))
					return true;
			}

			prev = prev.Prev;
		}

		return false;
	}

	// ───────────────────── Exact-pair probe ─────────────────────

	/// <summary>
	///   Finds the Equals-match for (index, value) given the composite lower bound.
	///   The composite descent routes equal RIGHT — exactly as the key-only tree did —
	///   so a run of entries sharing one (key, hash) may extend BOTH ways from the
	///   landing position: forward within/after this leaf, and, when the lower bound is
	///   0, backward into earlier leaves. That is the same shape main used for equal-key
	///   runs (ContainsInPrevLeaves); keeping it is what makes the unique-key path
	///   structurally identical to main rather than merely close to it.
	///   Bounded by the hash-collision run (expected length 1), not by the key run.
	///   Caller-supplied index/value stay the CompareTo/Equals receivers: a lock-free
	///   reader racing a shrink may probe a vacated slot, which reads as null for ref
	///   types — safe as an argument, an NRE as a receiver.
	/// </summary>
	private static bool TryFindPair(LeafNode leaf, int pos, TIndex index, TValue value, int valueHash,
		out LeafNode foundLeaf, out int foundPos) {
		if (ScanForward(leaf, pos, index, value, valueHash, out foundLeaf, out foundPos))
			return true;

		// pos > 0 ⇒ a strictly smaller pair precedes the run in this leaf ⇒ the miss is
		// definitive and no earlier leaf can hold the pair.
		if (pos == 0 && ScanBackward(leaf.Prev, index, value, valueHash, out foundLeaf, out foundPos))
			return true;

		foundLeaf = null!;
		foundPos = -1;
		return false;
	}

	private static bool ScanForward(LeafNode leaf, int pos, TIndex index, TValue value, int valueHash,
		out LeafNode foundLeaf, out int foundPos) {
		while (true) {
			var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
			for (; pos < count; pos++) {
				if (index.CompareTo(leaf.Keys[pos]) != 0 || valueHash != HashAt(leaf.Values, leaf.ValueHashes, pos))
					break;
				if (value.Equals(leaf.Values[pos])) {
					foundLeaf = leaf;
					foundPos = pos;
					return true;
				}
			}

			if (pos < count)
				break; // left the (key, hash) run

			var next = leaf.Next;
			if (next == null)
				break;
			leaf = next;
			pos = 0;
		}

		foundLeaf = null!;
		foundPos = -1;
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ScanBackward(LeafNode? prev, TIndex index, TValue value, int valueHash,
		out LeafNode foundLeaf, out int foundPos) {
		while (prev != null) {
			var count = Volatile.Read(ref prev.Count); // acquire: pairs with InsertIntoLeaf's release
			if (count == 0)
				break;

			var i = count - 1;
			for (; i >= 0; i--) {
				if (index.CompareTo(prev.Keys[i]) != 0 || valueHash != HashAt(prev.Values, prev.ValueHashes, i))
					break;
				if (value.Equals(prev.Values[i])) {
					foundLeaf = prev;
					foundPos = i;
					return true;
				}
			}

			if (i >= 0)
				break; // left the (key, hash) run
			prev = prev.Prev;
		}

		foundLeaf = null!;
		foundPos = -1;
		return false;
	}

	/// <summary>
	///   The probe left the leaf the descent recorded a path for, so RemoveFromLeaf's
	///   structural cleanup would edit the wrong parent. Re-descend on the TARGET leaf's
	///   own first pair and then step the recorded path to it. Writer-only, under the
	///   lock; reachable only on a hash collision that straddles a leaf boundary.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RepairPath(LeafNode target, InternalNode?[] ancestors, int[] childIndices, out int depth) {
		var leaf = FindLeafWithPathComposite(target.Keys[0], HashAt(target.Values, target.ValueHashes, 0),
			ancestors, childIndices, out depth);
		var guard = 0;
		while (!ReferenceEquals(leaf, target)) {
			if (++guard > MaxLeafWalk)
				return; // writer invariant violated — leave the path alone rather than corrupt it
			if (!StepPath(ancestors, childIndices, ref depth, out leaf))
				return;
		}
	}

	// Collision runs are bounded in practice; this only stops a corrupted tree from
	// spinning forever inside the writer lock.
	private const int MaxLeafWalk = 1 << 20;

	/// <summary>Steps the recorded path one leaf to the LEFT (equal routes right, so a
	/// descent on the target's first pair lands at or after it).</summary>
	private static bool StepPath(InternalNode?[] ancestors, int[] childIndices, ref int depth,
		out LeafNode leaf) {
		var level = depth - 1;
		while (level >= 0 && childIndices[level] == 0)
			level--;
		if (level < 0) {
			leaf = null!;
			return false;
		}

		childIndices[level]--;
		var node = ancestors[level]!.Children[childIndices[level]];
		var d = level + 1;
		while (node is InternalNode intern) {
			ancestors[d] = intern;
			childIndices[d] = intern.KeyCount; // rightmost child
			d++;
			node = intern.Children[intern.KeyCount];
		}

		depth = d;
		leaf = Unsafe.As<LeafNode>(node);
		return true;
	}


	// ───────────────────── Add ─────────────────────

	// Recover the raw tiebreak hash from a store-form (DefaultKeyComparer) hash. Value types
	// were Fibonacci-mixed by the store — one inverse multiply undoes it exactly. Ref types
	// (including strings) were never mixed — the store hash IS the raw hash. JIT-folded.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int RawHashFromStoreHash(int storeKeyHash) =>
		typeof(TValue).IsValueType ? HashMixing.Unmix(storeKeyHash) : storeKeyHash;

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool Add(TIndex index, TValue value) {
		var valueHash = HashOf(value);
		lock (_writeLock) {
			return AddCore(index, value, valueHash);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal bool Add(TIndex index, TValue value, int storeKeyHash) {
		var valueHash = RawHashFromStoreHash(storeKeyHash);
		System.Diagnostics.Debug.Assert(valueHash == HashOf(value), "pre-hashed Add: hash mismatch");
		lock (_writeLock) {
			return AddCore(index, value, valueHash);
		}
	}

	private bool AddCore(TIndex index, TValue value, int valueHash) {
		// valueHash is HashOf(value), computed ONCE per operation by the wrapper (outside
		// the lock) or recovered from the store hash by the pre-hashed entry; every
		// comparison in the descent and in the leaf binary search is then a single int
		// compare. The IComparable variant could hoist nothing — it re-ran CompareTo at
		// every level.

		// Monotonic-append fast path: range-index keys are typically timestamps, so new
		// entries usually sort past the current maximum. An entry strictly greater than
		// the current maximum cannot be a duplicate and appends into the (non-full) last
		// leaf with no descent, no duplicate scan and no path bookkeeping — O(1) instead
		// of O(log n). The comparison is composite, so batch-stamped keys (equal key,
		// differing value) keep hitting this path whenever the hash ascends.
		var last = _lastLeaf;
		var lastCount = last.Count; // writer-owned under the lock: plain read is exact
		if (lastCount > 0 && lastCount < LeafCapacity) {
			var cmpLast = index.CompareTo(last.Keys[lastCount - 1]);
			// In unique mode an equal key means this Add creates the first duplicate, so
			// fall through and let the mode flip rather than appending.
			if (cmpLast == 0 && _hasDuplicateKeys)
				cmpLast = valueHash.CompareTo(HashAt(last.Values, last.ValueHashes, lastCount - 1));
			if (cmpLast > 0) {
				last.Keys[lastCount] = index;
				last.Values[lastCount] = value;
				if (StoreValueHashes)
					last.ValueHashes![lastCount] = valueHash;
				// Release-publish the grown count AFTER the slot stores (see InsertIntoLeaf).
				Volatile.Write(ref last.Count, lastCount + 1);
				_length++;
				return true;
			}
		}

		var ancestors = GetAncestorsBuf();
		var childIndices = GetChildIdxBuf();

		// Unique-key mode: main's key-only descent, at main's cost. Writers read the flag
		// under the write lock, so it is exact here — no staleness to reason about.
		if (!_hasDuplicateKeys) {
			var uLeaf = FindLeafWithPath(index, ancestors, childIndices, out var uDepth);
			var uPos = LeafLowerBound(uLeaf, index, uLeaf.Count);
			// Every key is unique, so if this key exists at all it sits exactly at the
			// lower bound of this leaf (equal routes right).
			if (uPos >= uLeaf.Count || index.CompareTo(uLeaf.Keys[uPos]) != 0) {
				if (uLeaf.Count < LeafCapacity) {
					InsertIntoLeaf(uLeaf, uPos, index, value, valueHash);
					_length++;
					return true;
				}

				SplitAndInsert(uLeaf, uPos, index, value, valueHash, ancestors, childIndices, uDepth);
				_length++;
				return true;
			}

			if (value.Equals(uLeaf.Values[uPos]))
				return false; // exact pair already present

			// This Add creates the tree's first duplicate index key. Flip the mode and
			// fall through: from here on the composite path places the pair correctly
			// within the new run. No rebuild — with unique keys the existing layout is
			// already in composite order.
			Volatile.Write(ref _hasDuplicateKeys, true);
		}

		// One composite descent lands on the exact leaf for the (index, hash) pair; the
		// composite lower bound is both the sorted insertion point and the start of the
		// duplicate probe — no key-run scans regardless of duplicate-key run length.
		var leaf = FindLeafWithPathComposite(index, valueHash, ancestors, childIndices, out var depth);
		var insertPos = LeafLowerBoundComposite(leaf, index, valueHash);
		// A key-or-hash mismatch at the composite lower bound proves this pair is absent
		// without walking anything — that is every unique-key insert.
		if (insertPos == 0 || insertPos >= leaf.Count
			|| index.CompareTo(leaf.Keys[insertPos]) == 0 && valueHash == HashAt(leaf.Values, leaf.ValueHashes, insertPos)) {
			if (TryFindPair(leaf, insertPos, index, value, valueHash, out _, out _))
				return false;
		}

		if (leaf.Count < LeafCapacity) {
			InsertIntoLeaf(leaf, insertPos, index, value, valueHash);
			_length++;
			return true;
		}

		// Leaf is full — need to split
		SplitAndInsert(leaf, insertPos, index, value, valueHash, ancestors, childIndices, depth);
		_length++;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void InsertIntoLeaf(LeafNode leaf, int pos, TIndex index, TValue value, int valueHash) {
		if (pos < leaf.Count) {
			Array.Copy(leaf.Keys, pos, leaf.Keys, pos + 1, leaf.Count - pos);
			Array.Copy(leaf.Values, pos, leaf.Values, pos + 1, leaf.Count - pos);
			if (StoreValueHashes)
				Array.Copy(leaf.ValueHashes!, pos, leaf.ValueHashes!, pos + 1, leaf.Count - pos);
		}

		leaf.Keys[pos] = index;
		leaf.Values[pos] = value;
		if (StoreValueHashes)
			leaf.ValueHashes![pos] = valueHash;
		// Release-publish the grown count AFTER the slot stores: on weak memory models
		// a plain Count++ can become visible first, letting a concurrent reader scan
		// the not-yet-written tail slot and serve dirt left in the pooled array.
		// Readers pair this with an acquire read of Count.
		Volatile.Write(ref leaf.Count, leaf.Count + 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SplitAndInsert(LeafNode leaf, int insertPos, TIndex index, TValue value, int valueHash,
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
			if (StoreValueHashes)
				Array.Copy(leaf.ValueHashes!, leftKeep, newLeaf.ValueHashes!, 0, rightCount);
			newLeaf.Count = rightCount;

			// Trim old leaf to leftKeep items
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>())
				Array.Clear(leaf.Keys, leftKeep, rightCount);
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
				Array.Clear(leaf.Values, leftKeep, rightCount);
			leaf.Count = leftKeep;

			// Insert new item into left half (InsertIntoLeaf handles shifting)
			InsertIntoLeaf(leaf, insertPos, index, value, valueHash);
		}
		else {
			// New item goes into right half.
			// Left keeps mid items. Right gets (LeafCapacity - mid) existing + 1 new.
			var rightCount = LeafCapacity - mid;
			var newInsertPos = insertPos - mid;

			// Copy right portion to new leaf
			Array.Copy(leaf.Keys, mid, newLeaf.Keys, 0, rightCount);
			Array.Copy(leaf.Values, mid, newLeaf.Values, 0, rightCount);
			if (StoreValueHashes)
				Array.Copy(leaf.ValueHashes!, mid, newLeaf.ValueHashes!, 0, rightCount);
			newLeaf.Count = rightCount;

			// Trim old leaf
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>())
				Array.Clear(leaf.Keys, mid, rightCount);
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
				Array.Clear(leaf.Values, mid, rightCount);
			leaf.Count = mid;

			// Insert new item into right (new) leaf
			InsertIntoLeaf(newLeaf, newInsertPos, index, value, valueHash);
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
		var promotedHash = HashAt(newLeaf.Values, newLeaf.ValueHashes, 0);
		InsertIntoParent(leaf, promotedKey, promotedValue, promotedHash, newLeaf,
			ancestors, childIndices, depth);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InsertIntoParent(Node leftChild, TIndex key, TValue sepValue, int sepValueHash,
		Node rightChild, InternalNode?[] ancestors, int[] childIndices, int depth) {
		if (depth == 0) {
			// Root was a leaf, create new root. Release-publish: the new root's
			// freshly-rented arrays must be visible before the root pointer is.
			var newRoot = new InternalNode();
			newRoot.Keys[0] = key;
			newRoot.SepValues[0] = sepValue;
			if (StoreValueHashes)
				newRoot.SepValueHashes![0] = sepValueHash;
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
				if (StoreValueHashes)
					Array.Copy(parent.SepValueHashes!, insertAt, parent.SepValueHashes!, insertAt + 1, parent.KeyCount - insertAt);
				Array.Copy(parent.Children, insertAt + 1, parent.Children, insertAt + 2,
					parent.KeyCount - insertAt);
			}

			parent.Keys[insertAt] = key;
			parent.SepValues[insertAt] = sepValue;
			if (StoreValueHashes)
				parent.SepValueHashes![insertAt] = sepValueHash;
			// Release-publish the new node: its rented arrays were filled with plain
			// stores and must be visible before any pointer to it.
			Volatile.Write(ref parent.Children[insertAt + 1], rightChild);
			parent.KeyCount++;
			return;
		}

		// Parent is full — split internal node
		SplitInternalAndInsert(parent, key, sepValue, sepValueHash, rightChild, childIndices[level],
			ancestors, childIndices, level);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SplitInternalAndInsert(InternalNode node, TIndex key, TValue sepValue, int sepValueHash,
		Node rightChild, int insertAt, InternalNode?[] ancestors, int[] childIndices, int level) {
		// We have InternalCapacity-1 keys + 1 new key = InternalCapacity keys total
		// Split into left (mid keys) and right (rest), promote middle key to parent
		var totalKeys = node.KeyCount + 1;
		var midIdx = totalKeys / 2;

		// Build temporary arrays with the new key/child inserted (thread-static, zero alloc)
		var tempKeys = GetSplitKeysBuf();
		var tempSepValues = GetSplitSepValuesBuf();
		var tempSepValueHashes = StoreValueHashes ? GetSplitSepValueHashesBuf() : null;
		var tempChildren = GetSplitChildrenBuf();

		// Copy existing keys/children with insertion
		for (int i = 0, j = 0; j < totalKeys; i++, j++) {
			if (j == insertAt) {
				tempKeys[j] = key;
				tempSepValues[j] = sepValue;
				if (StoreValueHashes)
					tempSepValueHashes![j] = sepValueHash;
				tempChildren[j] = node.Children[i];
				tempChildren[j + 1] = rightChild;
				i--; // don't advance source
			}
			else {
				tempKeys[j] = node.Keys[i];
				tempSepValues[j] = node.SepValues[i];
				if (StoreValueHashes)
					tempSepValueHashes![j] = node.SepValueHashes![i];
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
		var promotedHash = HashAt(tempSepValues, tempSepValueHashes, midIdx);

		// Left node keeps keys 0..midIdx-1, children 0..midIdx
		node.KeyCount = midIdx;
		Array.Copy(tempKeys, 0, node.Keys, 0, midIdx);
		Array.Copy(tempSepValues, 0, node.SepValues, 0, midIdx);
		if (StoreValueHashes)
			Array.Copy(tempSepValueHashes!, 0, node.SepValueHashes!, 0, midIdx);
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
		if (StoreValueHashes)
			Array.Copy(tempSepValueHashes!, midIdx + 1, newNode.SepValueHashes!, 0, rightKeyCount);
		for (var i = 0; i <= rightKeyCount; i++)
			newNode.Children[i] = tempChildren[midIdx + 1 + i];

		// Promote to parent
		InsertIntoParent(node, promotedKey, promotedValue, promotedHash, newNode, ancestors, childIndices, level);
	}

	// ───────────────────── Remove ─────────────────────

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool Remove(TIndex index, TValue value) {
		var valueHash = HashOf(value);
		lock (_writeLock) {
			return RemoveCore(index, value, valueHash);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal bool Remove(TIndex index, TValue value, int storeKeyHash) {
		var valueHash = RawHashFromStoreHash(storeKeyHash);
		System.Diagnostics.Debug.Assert(valueHash == HashOf(value), "pre-hashed Remove: hash mismatch");
		lock (_writeLock) {
			return RemoveCore(index, value, valueHash);
		}
	}

	/// <summary>
	///   Locates the exact (index, value) pair and leaves ancestors/childIndices pointing at
	///   the leaf that holds it, so a caller can mutate it structurally. Writer-only.
	/// </summary>
	private bool TryLocate(TIndex index, TValue value, int valueHash, InternalNode?[] ancestors,
		int[] childIndices, out LeafNode foundLeaf, out int foundPos, out int depth) {
		// Unique-key mode: main's key-only path. Exact under the write lock.
		if (!_hasDuplicateKeys) {
			var uLeaf = FindLeafWithPath(index, ancestors, childIndices, out depth);
			var uPos = LeafLowerBound(uLeaf, index, uLeaf.Count);
			var uExact = FindExact(uLeaf, index, value, uPos);
			if (uExact >= 0) {
				foundLeaf = uLeaf;
				foundPos = uExact;
				return true;
			}

			// With no duplicate keys a miss here is definitive: the run cannot straddle a leaf.
			// The guard is belt-and-braces — if it ever trips, fall through to the composite
			// path rather than silently under-reporting.
			if (uPos > 0 || uLeaf.Prev == null || uLeaf.Prev.Count == 0
				|| index.CompareTo(uLeaf.Prev.Keys[uLeaf.Prev.Count - 1]) != 0) {
				foundLeaf = null!;
				foundPos = -1;
				return false;
			}
		}

		// Composite descent + composite lower bound pinpoint the pair in O(log n) no matter
		// how long the equal-key run is; TryFindPair then walks only the hash-collision run.
		// valueHash arrives precomputed from the caller — the public wrappers hash eagerly
		// outside the lock, the pre-hashed entries recover it from the store hash.
		var leaf = FindLeafWithPathComposite(index, valueHash, ancestors, childIndices, out depth);
		var pos = LeafLowerBoundComposite(leaf, index, valueHash);
		// Definitive miss at the lower bound — no probe, no out-param call.
		if (pos > 0 && pos < leaf.Count && index.CompareTo(leaf.Keys[pos]) != 0) {
			foundLeaf = null!;
			foundPos = -1;
			return false;
		}

		if (!TryFindPair(leaf, pos, index, value, valueHash, out foundLeaf, out foundPos))
			return false;

		if (!ReferenceEquals(foundLeaf, leaf))
			RepairPath(foundLeaf, ancestors, childIndices, out depth);

		return true;
	}

	private bool RemoveCore(TIndex index, TValue value, int valueHash) {
		var ancestors = GetAncestorsBuf();
		var childIndices = GetChildIdxBuf();
		if (!TryLocate(index, value, valueHash, ancestors, childIndices, out var leaf, out var pos, out var depth))
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
			if (StoreValueHashes)
				Array.Copy(leaf.ValueHashes!, exactPos + 1, leaf.ValueHashes!, exactPos, leaf.Count - exactPos);
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
			if (StoreValueHashes)
				Array.Copy(parent.SepValueHashes!, keyIdx + 1, parent.SepValueHashes!, keyIdx, parent.KeyCount - keyIdx - 1);
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
		var valueHash = HashOf(value);
		lock (_writeLock) {
			return UpdateCore(oldIndex, newIndex, value, valueHash);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal bool Update(TIndex oldIndex, TIndex newIndex, TValue value, int storeKeyHash) {
		var valueHash = RawHashFromStoreHash(storeKeyHash);
		System.Diagnostics.Debug.Assert(valueHash == HashOf(value), "pre-hashed Update: hash mismatch");
		lock (_writeLock) {
			return UpdateCore(oldIndex, newIndex, value, valueHash);
		}
	}

	private bool UpdateCore(TIndex oldIndex, TIndex newIndex, TValue value, int valueHash) {
		// Genuinely the same key — nothing to move.
		if (EqualityComparer<TIndex>.Default.Equals(oldIndex, newIndex))
			return ContainsCore(oldIndex, value);

		// Sorts to the same position but is NOT the same key — e.g. two strings that
		// collate equal under the current culture yet differ ordinally. Add+Remove would
		// DESTROY the entry here: AddCore rejects the new pair as a duplicate (same
		// position, same value) and RemoveCore then deletes the original. Overwrite the
		// key slot in place instead — the sort position is unchanged, so a lock-free
		// reader observes either the old or the new key and both satisfy the ordering
		// invariant.
		if (oldIndex.CompareTo(newIndex) == 0) {
			var ancestors = GetAncestorsBuf();
			var childIndices = GetChildIdxBuf();
			if (!TryLocate(oldIndex, value, valueHash, ancestors, childIndices, out var leaf, out var pos, out _))
				return false;

			leaf.Keys[pos] = newIndex;
			return true;
		}

		AddCore(newIndex, value, valueHash);
		return RemoveCore(oldIndex, value, valueHash);
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
		// Unique-key mode: main's algorithm verbatim. A lock-free reader may observe a
		// STALE false here — that is safe, not merely unlikely: FindExact scans the whole
		// equal-key run and ContainsInPrevLeaves walks back across leaves, so this path
		// returns the right answer on a tree that has duplicates, just more slowly.
		if (!Volatile.Read(ref _hasDuplicateKeys)) {
			var uLeaf = FindLeaf(index);
			var uPos = LeafLowerBound(uLeaf, index, Volatile.Read(ref uLeaf.Count));
			if (FindExact(uLeaf, index, value, uPos) >= 0)
				return true;

			// uPos > 0 ⇒ a smaller key precedes the run in this leaf ⇒ definitive miss.
			return uPos <= 0 && ContainsInPrevLeaves(uLeaf, index, value);
		}

		var valueHash = HashOf(value);
		var leaf = FindLeafComposite(index, valueHash);
		var pos = LeafLowerBoundComposite(leaf, index, valueHash);
		// Inline first probe. For unique keys — the dominant shape — the lower bound is
		// either an exact hit or a key/hash mismatch, and a mismatch there is definitive.
		// Only a genuine (key, hash) match that fails Equals needs the collision-run walk,
		// so the out-param call stays off the hot path entirely.
		// pos > 0 guards every early return: a strictly smaller pair precedes the run in
		// this leaf, so no earlier leaf can hold the pair and a mismatch here is final.
		// At pos == 0 the run may extend backwards and only TryFindPair can decide.
		if (pos > 0 && pos < Volatile.Read(ref leaf.Count)) {
			if (index.CompareTo(Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(leaf.Keys), pos)) != 0)
				return false;
			ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);
			ref var valueHashes = ref StoreValueHashes
				? ref MemoryMarshal.GetArrayDataReference(leaf.ValueHashes!)
				: ref Unsafe.NullRef<int>();
			if (valueHash != HashAt(ref values, ref valueHashes, pos))
				return false;
			if (value.Equals(Unsafe.Add(ref values, pos)))
				return true;
		}

		return TryFindPair(leaf, pos, index, value, valueHash, out _, out _);
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
		// One acquire-read of Count serves both the bound search and the first leaf's
		// emission: a fresher count could expose slots shifted after pos was computed
		// and emit entries below the requested lower bound.
		var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
		var pos = LeafLowerBound(leaf, from, count);

		while (leaf != null) {
			ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
			ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);

			// Split on a JIT constant — exactly ONE loop is emitted per closed type. The
			// ref-typed loop pays for the vacated-slot hazard; the value-typed loop must
			// NOT (measured 1.28× on a full scan when it did — the copy + argument-position
			// compare defeat the JIT's induction addressing on the emit).
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>()) {
				for (var i = pos; i < count; i++) {
					var key = Unsafe.Add(ref keys, i); // COPY, not ref: the guard, the bound compare
					// and the emit must all see one value — a racing shrink can null the slot
					// between three separate reads of a ref (reproduced: 1036 null keys emitted).
					// A ref-typed key is never legitimately null, so a null here means this
					// lock-free reader raced a shrink into a vacated slot: stop rather than emit
					// a default entry into the caller's results.
					if (key is null)
						return;
					// `to` is the receiver: the slot may be a vacated (null) one under a
					// racing shrink — safe as an argument, an NRE as a receiver.
					if (to.CompareTo(key) < 0)
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
			}
			else {
				// Value-typed keys can't race to null — pre-PR-20 loop verbatim.
				for (var i = pos; i < count; i++) {
					ref var key = ref Unsafe.Add(ref keys, i);
					if (key.CompareTo(to) > 0)
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
			}

			leaf = leaf.Next;
			pos = 0;
			count = leaf != null ? Volatile.Read(ref leaf.Count) : 0;
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
		// One acquire-read of Count serves both the bound search and the first leaf's
		// emission — see RangeCore.
		var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
		var pos = LeafLowerBound(leaf, start, count);

		while (leaf != null) {
			// Ref-based access elides bounds checks (count <= capacity is a writer
			// invariant) and lets the JIT vectorize consuming aggregators — measured
			// ~3x on windowed scans vs indexed access, whose bounds checks block SIMD.
			ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
			ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);

			// Split rather than a folded-away branch inside one loop: even when the guard
			// condition is a JIT constant, a conditional return in the body blocks the
			// vectorization this scan depends on. IsReferenceOrContainsReferences is a JIT
			// constant, so exactly ONE of these loops is emitted per closed type — value-typed
			// TIndex gets main's tight loop verbatim.
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>()) {
				for (var i = pos; i < count; i++) {
					var key = Unsafe.Add(ref keys, i); // COPY, not ref: the guard, the bound compare
				// and the emit must all see one value — a racing shrink can null the slot
				// between three separate reads of a ref (reproduced: 1036 null keys emitted).
					if (key is null) // raced a vacated slot — see RangeCore
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
			}
			else {
				for (var i = pos; i < count; i++)
					agg.Add(Unsafe.Add(ref keys, i), Unsafe.Add(ref values, i));
			}

			leaf = leaf.Next;
			pos = 0;
			count = leaf != null ? Volatile.Read(ref leaf.Count) : 0;
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

			// Ref/value split on a JIT constant — see RangeCore.
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>()) {
				for (var i = 0; i < count; i++) {
					var key = Unsafe.Add(ref keys, i); // COPY, not ref — see RangeCore.
					if (key is null) // raced a vacated slot — see RangeCore
						return;
					// `to` is the receiver — see RangeCore.
					if (to.CompareTo(key) < 0)
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
			}
			else {
				for (var i = 0; i < count; i++) {
					ref var key = ref Unsafe.Add(ref keys, i);
					if (key.CompareTo(to) > 0)
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
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

			// Ref/value split on a JIT constant — see RangeCore.
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>()) {
				for (var i = 0; i < count; i++) {
					var key = Unsafe.Add(ref keys, i); // COPY, not ref — see RangeCore.
					if (key is null) // raced a vacated slot — see RangeCore
						return;
					// `to` is the receiver — see RangeCore.
					if (to.CompareTo(key) <= 0)
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
			}
			else {
				for (var i = 0; i < count; i++) {
					ref var key = ref Unsafe.Add(ref keys, i);
					if (key.CompareTo(to) >= 0)
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
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
		// One acquire-read of Count per leaf serves the bound search, the hop check
		// AND the emission — see RangeCore.
		var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
		var pos = LeafUpperBound(leaf, start, count);

		// A run of keys equal to the exclusive bound can span leaves: pos == count means
		// every key in this leaf is <= start, so re-apply the upper bound on the next
		// leaf. Once a leaf has pos < count, every later key in the chain is > start.
		while (pos == count) {
			var next = leaf.Next;
			if (next == null)
				return;
			leaf = next;
			count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
			pos = LeafUpperBound(leaf, start, count);
		}

		while (leaf != null) {
			// Ref-based access elides bounds checks (count <= capacity is a writer
			// invariant) and lets the JIT vectorize consuming aggregators — measured
			// ~3x on windowed scans vs indexed access, whose bounds checks block SIMD.
			ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
			ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);

			// Split rather than a folded-away branch inside one loop: even when the guard
			// condition is a JIT constant, a conditional return in the body blocks the
			// vectorization this scan depends on. IsReferenceOrContainsReferences is a JIT
			// constant, so exactly ONE of these loops is emitted per closed type — value-typed
			// TIndex gets main's tight loop verbatim.
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>()) {
				for (var i = pos; i < count; i++) {
					var key = Unsafe.Add(ref keys, i); // COPY, not ref: the guard, the bound compare
				// and the emit must all see one value — a racing shrink can null the slot
				// between three separate reads of a ref (reproduced: 1036 null keys emitted).
					if (key is null) // raced a vacated slot — see RangeCore
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
			}
			else {
				for (var i = pos; i < count; i++)
					agg.Add(Unsafe.Add(ref keys, i), Unsafe.Add(ref values, i));
			}

			leaf = leaf.Next;
			pos = 0;
			count = leaf != null ? Volatile.Read(ref leaf.Count) : 0;
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
		// One acquire-read of Count per leaf serves the bound search, the hop check
		// AND the emission — see RangeCore.
		var count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
		int pos;
		if (includeFrom) {
			pos = LeafLowerBound(leaf, from, count);
		}
		else {
			// Exclusive lower bound: skip the run of keys equal to `from` across leaves
			// (see RangeFromExclusive).
			pos = LeafUpperBound(leaf, from, count);
			while (pos == count) {
				var next = leaf.Next;
				if (next == null)
					return;
				leaf = next;
				count = Volatile.Read(ref leaf.Count); // acquire: pairs with InsertIntoLeaf's release
				pos = LeafUpperBound(leaf, from, count);
			}
		}

		while (leaf != null) {
			ref var keys = ref MemoryMarshal.GetArrayDataReference(leaf.Keys);
			ref var values = ref MemoryMarshal.GetArrayDataReference(leaf.Values);

			// Ref/value split on a JIT constant — see RangeCore.
			if (RuntimeHelpers.IsReferenceOrContainsReferences<TIndex>()) {
				for (var i = pos; i < count; i++) {
					var key = Unsafe.Add(ref keys, i); // COPY, not ref — see RangeCore.
					if (key is null) // raced a vacated slot — see RangeCore
						return;
					// `to` is the receiver — see RangeCore.
					var cmp = to.CompareTo(key);
					if (includeTo ? cmp < 0 : cmp <= 0)
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
			}
			else {
				for (var i = pos; i < count; i++) {
					ref var key = ref Unsafe.Add(ref keys, i);
					var cmp = key.CompareTo(to);
					if (includeTo ? cmp > 0 : cmp >= 0)
						return;
					agg.Add(key, Unsafe.Add(ref values, i));
				}
			}

			leaf = leaf.Next;
			pos = 0;
			count = leaf != null ? Volatile.Read(ref leaf.Count) : 0;
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
