# PR56 Perf-Neutral Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the PooledBTree equal-key-run leak, PooledSet use-after-return corruption, and CacheRangeIndex drift masking — keeping all pooling and with zero regression on currently-correct hot paths.

**Architecture:** Fast-path-preserving descent fixes in PooledBTree (slow paths gated by the `pos == 0` definitive-miss invariant); a new `ReaderGate` asymmetric reclamation utility (per-thread padded slots, plain-store pins, writer-side `Interlocked.MemoryBarrierProcessWide` + grace-period limbo) protecting pooled-memory reuse from lock-free readers; PooledSet rebuilt around a volatile-published `Tables` generation with a pins/Retired/Returned state word for escaping enumerators.

**Tech Stack:** .NET 9 (`net9.0`+`net10.0` test targets), NUnit, BenchmarkDotNet, ArrayPool.

**Spec:** `docs/superpowers/specs/2026-07-10-pr56-perf-neutral-port-design.md`

## Global Constraints

- Branch: `fix/pr56-perf-neutral-port` (already exists; repro tests + spec committed).
- House style: tabs (width 2), file-scoped namespaces with `using`s **inside** the namespace, K&R braces, `var` everywhere, `_camelCase` fields, no `this.`.
- Hot-path rules (high-performance-net skill): no LINQ, no per-iteration allocations, no closures in hot loops; cold paths get `[MethodImpl(MethodImplOptions.NoInlining)]`.
- Never hand-edit `*.generated.cs`.
- All test projects use NUnit (`[TestFixture]`/`[Test]`/`Assert.That`).
- Repro tests already committed and MUST NOT be weakened: `tests/Prague.Generated.Tests/Cache/PooledBTreeTests.cs` (7 duplicate-run tests), `tests/Prague.Core.Tests/DataStructures/PooledSetLifetimeTests.cs` (5 lifetime tests; the two `set.Slots` usages get mechanically ported to `GetSnapshot` in Task 7).
- Verify commands: `dotnet test tests/Prague.Generated.Tests --filter "FullyQualifiedName~PooledBTreeTests"`, `dotnet test tests/Prague.Core.Tests`, full: `dotnet test Prague.Tests.slnf`.

---

### Task 1: PooledBTree Remove — cross-leaf run slow path

**Files:**
- Modify: `src/Prague.Core/Collections/PooledBTree.cs` (RemoveCore at ~line 527; add FindChildIndexLeft after FindChildIndex ~line 219; add RemoveFromSubtree + RemoveFromLeaf)
- Test (exists, failing): `tests/Prague.Generated.Tests/Cache/PooledBTreeTests.cs`

**Interfaces:**
- Produces: `private static int FindChildIndexLeft(InternalNode node, TIndex index)` (used by Task 6 nothing else; internal to this file), `private void RemoveFromLeaf(LeafNode leaf, int exactPos, InternalNode?[] ancestors, int[] childIndices, int depth)`.
- The definitive-miss invariant used by Tasks 1–2: if `LeafLowerBound(leaf, index) > 0`, a key `< index` precedes the run inside this leaf, so the run cannot extend into earlier leaves.

- [ ] **Step 1: Confirm the three Remove tests fail**

Run: `dotnet test tests/Prague.Generated.Tests --filter "FullyQualifiedName~PooledBTreeTests&(FullyQualifiedName~Remove_DuplicateRun|FullyQualifiedName~UpdateChurn)" --framework net9.0`
Expected: 3 FAILED (`Remove_DuplicateRunSpanningLeaves_RemovesEveryPair`, `Remove_DuplicateRunWithNeighbors_RemovesFromLeftLeavesOfRun`, `UpdateChurn_RangeIndexPattern_LengthStaysBounded`).

- [ ] **Step 2: Add `FindChildIndexLeft` immediately after `FindChildIndex` (~line 219)**

```csharp
	/// <summary>
	///   Binary search within an internal node for the LEFTMOST child that can contain
	///   the given key. Keys equal to a separator route LEFT (the run of equal keys may
	///   start in the child before the separator).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FindChildIndexLeft(InternalNode node, TIndex index) {
		var lo = 0;
		var hi = node.KeyCount - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >>> 1;
			var cmp = index.CompareTo(node.Keys[mid]);
			if (cmp > 0) // equal goes LEFT (to find leftmost child with this key)
				lo = mid + 1;
			else
				hi = mid - 1;
		}

		return lo;
	}
```

- [ ] **Step 3: Rewrite `RemoveCore` — fast path unchanged, extract `RemoveFromLeaf`, add DFS slow path**

Replace the whole `RemoveCore` method (currently lines 527–557) with:

```csharp
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
		if (node.IsLeaf) {
			var leaf = Unsafe.As<LeafNode>(node);
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
```

Note: `Unsafe.As` requires `using System.Runtime.CompilerServices;` — already imported in this file.

- [ ] **Step 4: Run the Remove tests**

Run: `dotnet test tests/Prague.Generated.Tests --filter "FullyQualifiedName~PooledBTreeTests&(FullyQualifiedName~Remove_DuplicateRun|FullyQualifiedName~UpdateChurn)"`
Expected: 3 PASS (both frameworks). Also run the full fixture to catch regressions: `dotnet test tests/Prague.Generated.Tests --filter "FullyQualifiedName~PooledBTreeTests"` — expected: only `Contains_DuplicateRunSpanningLeaves_FindsEveryPair`, `Add_DuplicatePairInSpanningRun_ReturnsFalse`, `RangeFromExclusive_DuplicateRunSpanningLeaves_ExcludesAllEqualKeys`, `RangeCustom_ExclusiveFrom_DuplicateRunSpanningLeaves_ExcludesAllEqualKeys` still fail (fixed in Tasks 2–3).

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/Collections/PooledBTree.cs
git commit -m "fix: PooledBTree.Remove finds pairs in equal-key runs spanning leaves

Fast path (single rightmost descent + FindExact) is unchanged; the DFS
slow path runs only when pos == 0 and the previous leaf ends with an
equal key — the case that silently returned false and leaked forever."
```

---

### Task 2: PooledBTree Contains + Add duplicate check — Prev-walk slow path

**Files:**
- Modify: `src/Prague.Core/Collections/PooledBTree.cs` (Contains ~line 624; AddCore duplicate check ~line 320; add ContainsInPrevLeaves helper)

**Interfaces:**
- Produces: `private static bool ContainsInPrevLeaves(LeafNode leaf, TIndex index, TValue value)` — walks `leaf.Prev` chain while the previous leaf's tail keys equal `index`. Used by both `Contains` and `AddCore`.

- [ ] **Step 1: Confirm the two tests fail**

Run: `dotnet test tests/Prague.Generated.Tests --filter "FullyQualifiedName~PooledBTreeTests&(FullyQualifiedName~Contains_DuplicateRun|FullyQualifiedName~Add_DuplicatePair)" --framework net9.0`
Expected: 2 FAILED.

- [ ] **Step 2: Add the shared slow-path helper (place after `FindExact`, ~line 302)**

```csharp
	/// <summary>
	///   Cold path for runs of equal keys spanning leaves: the fast-path leaf's run
	///   portion missed, and pos == 0 means the run may extend into earlier leaves.
	///   Walks backwards while the previous leaf's last key still equals the index.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ContainsInPrevLeaves(LeafNode leaf, TIndex index, TValue value) {
		var prev = leaf.Prev;
		while (prev != null && prev.Count > 0 && prev.Keys[prev.Count - 1].CompareTo(index) == 0) {
			for (var i = prev.Count - 1; i >= 0; i--) {
				if (prev.Keys[i].CompareTo(index) != 0)
					break;
				if (value.Equals(prev.Values[i]))
					return true;
			}

			prev = prev.Prev;
		}

		return false;
	}
```

- [ ] **Step 3: Extend `Contains` (keep fast path first)**

Replace the `Contains` method body:

```csharp
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(TIndex index, TValue value) {
		var leaf = FindLeaf(index);
		var pos = LeafLowerBound(leaf, index);
		if (FindExact(leaf, index, value, pos) >= 0)
			return true;

		// pos > 0 ⇒ a smaller key precedes the run in this leaf ⇒ miss is definitive.
		if (pos > 0)
			return false;

		return ContainsInPrevLeaves(leaf, index, value);
	}
```

- [ ] **Step 4: Extend `AddCore`'s duplicate check**

In `AddCore`, replace:

```csharp
		// Check for duplicate
		var pos = LeafLowerBound(leaf, index);
		if (FindExact(leaf, index, value, pos) >= 0)
			return false;
```

with:

```csharp
		// Check for duplicate — including earlier leaves when the equal-key run may
		// span a leaf boundary (pos == 0; see ContainsInPrevLeaves)
		var pos = LeafLowerBound(leaf, index);
		if (FindExact(leaf, index, value, pos) >= 0)
			return false;
		if (pos == 0 && ContainsInPrevLeaves(leaf, index, value))
			return false;
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Prague.Generated.Tests --filter "FullyQualifiedName~PooledBTreeTests"`
Expected: only the two Range* duplicate-run tests still fail.

- [ ] **Step 6: Commit**

```bash
git add src/Prague.Core/Collections/PooledBTree.cs
git commit -m "fix: PooledBTree Contains/Add duplicate check scan equal-key runs across leaves"
```

---

### Task 3: PooledBTree exclusive-lower-bound range scans

**Files:**
- Modify: `src/Prague.Core/Collections/PooledBTree.cs` (`RangeFromExclusive` ~line 764, `RangeCustom` ~line 786)

- [ ] **Step 1: Confirm the two tests fail**

Run: `dotnet test tests/Prague.Generated.Tests --filter "FullyQualifiedName~PooledBTreeTests&FullyQualifiedName~ExcludesAllEqualKeys" --framework net9.0`
Expected: 2 FAILED.

- [ ] **Step 2: Fix `RangeFromExclusive` with a skip-run prologue (main loop untouched)**

```csharp
	/// <summary>
	///   Range query (start, ∞) — from start exclusive.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void RangeFromExclusive<TResultsAggregator>(TIndex start, ref TResultsAggregator agg)
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
			var keys = leaf.Keys;
			var values = leaf.Values;
			var count = leaf.Count;

			for (var i = pos; i < count; i++)
				agg.Add(keys[i], values[i]);

			leaf = leaf.Next;
			pos = 0;
		}
	}
```

- [ ] **Step 3: Fix `RangeCustom` the same way (prologue only for the exclusive-from case)**

```csharp
	/// <summary>
	///   Range query with custom inclusive/exclusive bounds.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public void RangeCustom<TResultsAggregator>(TIndex from, TIndex to, bool includeFrom, bool includeTo,
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
			var keys = leaf.Keys;
			var values = leaf.Values;
			var count = leaf.Count;

			for (var i = pos; i < count; i++) {
				var cmp = keys[i].CompareTo(to);
				if (includeTo ? cmp > 0 : cmp >= 0)
					return;
				agg.Add(keys[i], values[i]);
			}

			leaf = leaf.Next;
			pos = 0;
		}
	}
```

- [ ] **Step 4: Run the full B-tree fixture — all 7 ported tests plus the pre-existing 58 must pass**

Run: `dotnet test tests/Prague.Generated.Tests --filter "FullyQualifiedName~PooledBTreeTests"`
Expected: 65 PASS / 0 FAIL per framework.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/Collections/PooledBTree.cs
git commit -m "fix: exclusive-lower-bound range scans skip equal-key runs spanning leaves"
```

---

### Task 4: CacheRangeIndex reports real B-tree length

**Files:**
- Modify: `src/Prague.Core/InMemoryDataCache.cs:520-550` (class `CacheRangeIndex<TKey, TValue, TIndexKey>` only — do NOT touch the other `ApproximateCount` properties in `Indexing.cs` or the key-set index)

- [ ] **Step 1: Delete the `ApproximateCount` property and its bookkeeping in `CacheRangeIndex`**

Remove the property (lines ~520-524), the `ApproximateCount++` in `Add`, the `ApproximateCount--` in `Remove`, and replace `GetCounters`:

```csharp
	public void Add(TKey key, TValue value, long timestampMs) {
		_index.Add(_keySelector(key, value), key);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Remove(TKey key, TValue value, long timestamp) {
		_index.Remove(_keySelector(key, value), key);
	}
```

```csharp
	public ulong GetCounters(out ulong vlaues) {
		// Report the real B-tree size, not a logically-maintained counter: a divergence
		// between the two is exactly how stale (leaked) index entries manifest.
		vlaues = (ulong)_index.Length;
		return (ulong)_index.Length;
	}
```

Note: `Update` (lines ~536-544) needs no change — it calls `Add`/`Remove` on `_index` directly and only touched `ApproximateCount` if it did; verify and remove any `ApproximateCount` reference left in the class.

- [ ] **Step 2: Build and run Core + Generated test suites**

Run: `dotnet build Prague.sln && dotnet test tests/Prague.Core.Tests && dotnet test tests/Prague.Generated.Tests`
Expected: build clean; all tests pass (no test asserts on `CacheRangeIndex.ApproximateCount` — verified by grep).

- [ ] **Step 3: Commit**

```bash
git add src/Prague.Core/InMemoryDataCache.cs
git commit -m "fix: CacheRangeIndex.GetCounters reports real PooledBTree.Length

Index drift becomes visible in statistics instead of being masked by
the logically-maintained ApproximateCount (109K vs 9.1M upstream)."
```

---

### Task 5: ReaderGate — asymmetric reclamation utility

**Files:**
- Create: `src/Prague.Core/Collections/ReaderGate.cs`
- Create: `tests/Prague.Core.Tests/DataStructures/ReaderGateTests.cs`

**Interfaces (produced — Tasks 6 and 7 depend on these exact signatures):**
- `internal static class ReaderGate`
  - `static ReaderGate.Slot Enter()` — reader pin; zero atomic ops; must be paired with `Exit` in `finally`.
  - `static void Exit(ReaderGate.Slot slot)` — reader unpin.
  - `static void Retire(ReaderGate.IRetirable item)` — writer-side: park retired pooled memory; reclaims immediately when no reader is pinned (after a process-wide barrier), otherwise defers to the grace-period limbo.
  - `static void TryDrain()` — best-effort limbo drain (used by Dispose paths and tests).
  - `internal interface IRetirable { void ReclaimToPool(); }` — implementors return their arrays to the pool exactly once when called.
  - `internal static int RegisteredSlotCount` — test observability.

- [ ] **Step 1: Write the failing unit tests**

Create `tests/Prague.Core.Tests/DataStructures/ReaderGateTests.cs`:

```csharp
namespace Prague.Core.Tests.DataStructures;

	using Prague.Core.Collections;
	using NUnit.Framework;

[TestFixture]
[NonParallelizable] // the gate is process-global; these tests reason about pin state
public class ReaderGateTests {
	private sealed class TrackedRetirable : ReaderGate.IRetirable {
		private int _reclaims;
		public int Reclaims => Volatile.Read(ref _reclaims);
		public void ReclaimToPool() => Interlocked.Increment(ref _reclaims);
	}

	[SetUp]
	public void FlushLimbo() {
		// Drain anything a previous test (or fixture) left parked so counts start clean.
		ReaderGate.TryDrain();
		ReaderGate.TryDrain();
	}

	[Test]
	public void Retire_NoPinnedReaders_ReclaimsImmediately() {
		var item = new TrackedRetirable();
		ReaderGate.Retire(item);
		Assert.That(item.Reclaims, Is.EqualTo(1));
	}

	[Test]
	public void Retire_WhilePinned_DefersUntilUnpin() {
		var item = new TrackedRetirable();
		var slot = ReaderGate.Enter();
		try {
			ReaderGate.Retire(item);
			Assert.That(item.Reclaims, Is.EqualTo(0), "must not reclaim under an active pin");
			ReaderGate.TryDrain();
			Assert.That(item.Reclaims, Is.EqualTo(0), "grace has not passed while still pinned");
		}
		finally {
			ReaderGate.Exit(slot);
		}

		ReaderGate.TryDrain();
		Assert.That(item.Reclaims, Is.EqualTo(1));
	}

	[Test]
	public void Retire_ReaderRepinned_SequenceAdvanceCountsAsGrace() {
		var item = new TrackedRetirable();
		var slot = ReaderGate.Enter();
		ReaderGate.Retire(item);
		Assert.That(item.Reclaims, Is.EqualTo(0));
		ReaderGate.Exit(slot);

		// Re-pin before the drain: the CURRENT pin postdates the park barrier, so it
		// cannot reach the parked item — sequence advance proves the grace period.
		var slot2 = ReaderGate.Enter();
		try {
			ReaderGate.TryDrain();
			Assert.That(item.Reclaims, Is.EqualTo(1));
		}
		finally {
			ReaderGate.Exit(slot2);
		}
	}

	[Test]
	public void Retire_ManyItems_EachReclaimedExactlyOnce() {
		var items = new TrackedRetirable[64];
		for (var i = 0; i < items.Length; i++)
			items[i] = new TrackedRetirable();

		var slot = ReaderGate.Enter();
		for (var i = 0; i < 32; i++)
			ReaderGate.Retire(items[i]);
		ReaderGate.Exit(slot);

		for (var i = 32; i < 64; i++)
			ReaderGate.Retire(items[i]);
		ReaderGate.TryDrain();
		ReaderGate.TryDrain(); // two drains: reclaim sealed, then seal+reclaim the rest

		for (var i = 0; i < items.Length; i++)
			Assert.That(items[i].Reclaims, Is.EqualTo(1), $"item {i}");
	}

	[Test]
	public void NestedEnter_SingleThread_OuterExitReleases() {
		var item = new TrackedRetirable();
		var outer = ReaderGate.Enter();
		var inner = ReaderGate.Enter();
		ReaderGate.Retire(item);
		ReaderGate.Exit(inner);
		ReaderGate.TryDrain();
		Assert.That(item.Reclaims, Is.EqualTo(0), "outer pin still active");
		ReaderGate.Exit(outer);
		ReaderGate.TryDrain();
		Assert.That(item.Reclaims, Is.EqualTo(1));
	}

	[Test]
	public void SlotRecycling_DeadThreads_DoNotGrowRegistryUnboundedly() {
		// warm-up: current thread's slot
		ReaderGate.Exit(ReaderGate.Enter());
		var before = ReaderGate.RegisteredSlotCount;

		for (var i = 0; i < 16; i++) {
			var t = new Thread(static () => ReaderGate.Exit(ReaderGate.Enter()));
			t.Start();
			t.Join();
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		Assert.That(ReaderGate.RegisteredSlotCount, Is.LessThanOrEqualTo(before + 3),
			"dead threads' slots must be recycled through the free list, not accumulated");
	}
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Prague.Core.Tests --filter "FullyQualifiedName~ReaderGateTests" --framework net9.0`
Expected: FAIL to compile (`ReaderGate` does not exist).

- [ ] **Step 3: Implement `src/Prague.Core/Collections/ReaderGate.cs`**

```csharp
namespace Prague.Core.Collections;

	using System.Runtime.CompilerServices;
	using System.Runtime.InteropServices;

/// <summary>
///   Asymmetric reclamation gate for single-writer / lock-free-reader pooled structures.
///   Readers pin around each scoped scan with two plain stores to a thread-owned,
///   cache-line-padded slot — zero atomic operations, zero fences on the reader side.
///   Writers retiring pooled memory call <see cref="Retire" />: after an
///   <see cref="Interlocked.MemoryBarrierProcessWide" /> (which makes every in-flight
///   reader pin visible and guarantees later readers observe the unlink), memory is
///   reclaimed immediately when no pin is held, or parked in a limbo batch stamped with
///   the pinned slots' sequence numbers. A batch is reclaimed once every stamped slot
///   has unpinned at least once (Depth == 0 or Sequence advanced) — the RCU
///   grace-period argument: a pin taken after the barrier starts from the structure's
///   root and can no longer reach the unlinked memory, so it never blocks reclamation.
///   Slots are recycled through a finalizer-backed free list, bounding gate memory by
///   peak concurrent reader threads.
/// </summary>
internal static class ReaderGate {
	/// <summary>Retired pooled memory awaiting a reader grace period.</summary>
	internal interface IRetirable {
		/// <summary>Returns the backing memory to its pool. Called exactly once.</summary>
		void ReclaimToPool();
	}

	[StructLayout(LayoutKind.Sequential)]
	internal sealed class Slot {
		// Padding isolates the hot fields on their own cache line: the object header +
		// _pad0 fill the line before Depth/Sequence, _pad1 fills the line after, so
		// writer-side snapshot reads never false-share with another thread's pins.
		private readonly Padding _pad0;
		public int Depth;
		public int Sequence;
		private readonly Padding _pad1;
		public Slot? NextFree;

		[StructLayout(LayoutKind.Explicit, Size = 56)]
		private readonly struct Padding { }
	}

	private sealed class SlotOwner {
		public readonly Slot Slot;

		public SlotOwner(Slot slot) {
			Slot = slot;
		}

		~SlotOwner() => ReturnSlot(Slot);
	}

	[ThreadStatic] private static SlotOwner? _owner;

	private static readonly Lock _registryLock = new();
	private static Slot[] _slots = [];
	private static Slot? _freeSlots;

	private static readonly Lock _limboLock = new();
	private static List<IRetirable> _open = new();
	private static List<IRetirable>? _sealed;
	private static Slot[]? _sealedSlots;
	private static int[]? _sealedSeqs;

	internal static int RegisteredSlotCount => Volatile.Read(ref _slots).Length;

	// ───────────────────── Reader side ─────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Slot Enter() {
		var owner = _owner ?? CreateOwner();
		var slot = owner.Slot;
		slot.Sequence++;
		// The volatile write/read pair is compiler ordering only (free on x64): the JIT
		// must not sink the pin store below, nor hoist the guarded data loads above,
		// this point. Hardware store-load reordering (the pin store parked in a store
		// buffer while data loads execute) is closed by the writer's process-wide
		// barrier in SnapshotPins.
		Volatile.Write(ref slot.Depth, slot.Depth + 1);
		_ = Volatile.Read(ref slot.Depth);
		return slot;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Exit(Slot slot) {
		// Release store: the guarded data loads complete before the unpin is visible.
		Volatile.Write(ref slot.Depth, slot.Depth - 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static SlotOwner CreateOwner() {
		var owner = new SlotOwner(RentSlot());
		_owner = owner;
		return owner;
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
		if (Volatile.Read(ref slot.Depth) != 0)
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
		// 1. Reclaim the sealed batch once its grace period has passed. No barrier
		//    needed here: a stale "still pinned" read just defers to the next drain.
		if (_sealed != null && GracePassed(_sealedSlots!, _sealedSeqs!)) {
			for (var i = 0; i < _sealed.Count; i++)
				_sealed[i].ReclaimToPool();
			_sealed = null;
			_sealedSlots = null;
			_sealedSeqs = null;
		}

		// 2. Seal the open batch. One outstanding sealed batch at a time: each
		//    process-wide barrier is amortized over everything parked during the
		//    previous grace period.
		if (_sealed == null && _open.Count > 0) {
			var pinned = SnapshotPins(out var seqs);
			if (pinned.Length == 0) {
				for (var i = 0; i < _open.Count; i++)
					_open[i].ReclaimToPool();
				_open.Clear();
				return;
			}

			_sealed = _open;
			_sealedSlots = pinned;
			_sealedSeqs = seqs;
			_open = new List<IRetirable>();
		}
	}

	private static Slot[] SnapshotPins(out int[] seqs) {
		// Serialize every core: pins still sitting in a store buffer become visible,
		// and any pin taken after this point observes the already-unlinked structures,
		// so it can never reach parked memory and needs no tracking.
		Interlocked.MemoryBarrierProcessWide();

		var slots = Volatile.Read(ref _slots);
		List<Slot>? pinned = null;
		List<int>? pinnedSeqs = null;
		for (var i = 0; i < slots.Length; i++) {
			var slot = slots[i];
			if (Volatile.Read(ref slot.Depth) > 0) {
				pinned ??= new List<Slot>();
				pinnedSeqs ??= new List<int>();
				pinned.Add(slot);
				pinnedSeqs.Add(Volatile.Read(ref slot.Sequence));
			}
		}

		if (pinned == null) {
			seqs = [];
			return [];
		}

		seqs = pinnedSeqs!.ToArray();
		return pinned.ToArray();
	}

	private static bool GracePassed(Slot[] slots, int[] seqs) {
		for (var i = 0; i < slots.Length; i++) {
			var slot = slots[i];
			if (Volatile.Read(ref slot.Depth) > 0 && Volatile.Read(ref slot.Sequence) == seqs[i])
				return false;
		}

		return true;
	}
}
```

- [ ] **Step 4: Run the gate tests**

Run: `dotnet test tests/Prague.Core.Tests --filter "FullyQualifiedName~ReaderGateTests"`
Expected: 7 PASS per framework. If `SlotRecycling_DeadThreads_DoNotGrowRegistryUnboundedly` is flaky on finalization timing, add a second `GC.Collect(); GC.WaitForPendingFinalizers();` round inside the loop — do NOT loosen the assertion beyond `before + 3`.

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/Collections/ReaderGate.cs tests/Prague.Core.Tests/DataStructures/ReaderGateTests.cs
git commit -m "feat: ReaderGate — asymmetric grace-period reclamation for pooled memory

Readers pin with two plain stores to a padded per-thread slot; writers
reclaim after Interlocked.MemoryBarrierProcessWide, immediately when
quiescent or via a sequence-stamped limbo batch. Slots recycle through
a finalizer-backed free list (bounded by peak concurrent readers)."
```

---

### Task 6: PooledBTree — gate all lock-free readers, retire nodes through the gate

**Files:**
- Modify: `src/Prague.Core/Collections/PooledBTree.cs`

**Interfaces:**
- Consumes: `ReaderGate.Enter/Exit/Retire`, `ReaderGate.IRetirable` (Task 5).
- Public API of PooledBTree unchanged.

- [ ] **Step 1: Make nodes retirable**

Change the `Node` hierarchy — the abstract base implements `ReaderGate.IRetirable`, each node forwards to its existing `ReturnToPool`:

```csharp
	private abstract class Node : ReaderGate.IRetirable {
		public abstract bool IsLeaf { get; }

		public abstract void ReclaimToPool();
	}
```

In `LeafNode` add:

```csharp
		public override void ReclaimToPool() => ReturnToPool();
```

In `InternalNode` add:

```csharp
		public override void ReclaimToPool() => ReturnToPool();
```

- [ ] **Step 2: Route structural removals through the gate**

In `RemoveEmptyLeaf` (~line 574) replace:

```csharp
		// Return arrays to pool
		leaf.ReturnToPool();
```

with:

```csharp
		// Defer the pool return past the reader grace period: lock-free readers
		// (Range*, TryGetMin/Max, Contains) may still hold this node. Its Keys/Values
		// and Next/Prev stay intact until reclamation, so a parked reader continues
		// the chain correctly (documented staleness: it may re-see or miss the
		// removed entries).
		ReaderGate.Retire(leaf);
```

In `RemoveFromParent` replace both `parent.ReturnToPool();` occurrences (root-collapse branch and empty-non-root branch) with:

```csharp
			ReaderGate.Retire(parent);
```

`Dispose()` keeps calling `ReturnToPool()` directly (contract: the caller guarantees no readers after Dispose; nodes already retired earlier were unlinked from the chain and tree, so the Dispose walk cannot double-return them). Add one line at the end of `Dispose()`:

```csharp
		ReaderGate.TryDrain();
```

- [ ] **Step 3: Gate the read methods**

Rename each existing public read method body to a private `*Core` method and add a gated public wrapper. The wrappers deliberately drop `AggressiveInlining` (try/finally blocks inlining; one extra call per query is the accepted cost), the `*Core` methods keep the original attributes. Apply this exact pattern to all eight readers — `Contains`, `TryGetMin`, `TryGetMax`, `Range`, `RangeFrom`, `RangeTo`, `RangeToExclusive`, `RangeFromExclusive`, `RangeCustom`:

```csharp
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool Contains(TIndex index, TValue value) {
		var slot = ReaderGate.Enter();
		try {
			return ContainsCore(index, value);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
```

```csharp
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public bool TryGetMin(out TIndex index, out TValue value) {
		var slot = ReaderGate.Enter();
		try {
			return TryGetMinCore(out index, out value);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool TryGetMinCore(out TIndex index, out TValue value) {
		var leaf = _firstLeaf;
		if (leaf.Count > 0) {
			index = leaf.Keys[0];
			value = leaf.Values[0];
			return true;
		}

		index = default!;
		value = default!;
		return false;
	}
```

(TryGetMax identical shape.) For the generic range methods the wrapper keeps the generic signature and forwards the aggregator by ref:

```csharp
	/// <summary>
	///   Range query [from, to] — inclusive on both bounds.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private void RangeCore<TResultsAggregator>(TIndex from, TIndex to, ref TResultsAggregator agg)
		where TResultsAggregator : struct, IResultAggregator, allows ref struct {
		// ...existing body of Range, unchanged...
	}
```

Writer-side internals (`AddCore`, `RemoveCore`, `RemoveFromSubtree`, `ContainsInPrevLeaves`, `FindLeaf*`) are NOT gated — the writer cannot race its own retire, and gating them would double-pin.

- [ ] **Step 4: Run the full B-tree fixture + Core tests**

Run: `dotnet test tests/Prague.Generated.Tests --filter "FullyQualifiedName~PooledBTreeTests" && dotnet test tests/Prague.Core.Tests`
Expected: all PASS (65 B-tree per framework; Core suite green including ReaderGateTests).

- [ ] **Step 5: Commit**

```bash
git add src/Prague.Core/Collections/PooledBTree.cs
git commit -m "feat: PooledBTree retires nodes via ReaderGate; readers pin around scans

Node pooling is preserved: emptied leaves/collapsed parents park in the
gate limbo and return to ArrayPool after the reader grace period."
```

---

### Task 7: PooledSet — Tables generation with refcounted pooling

**Files:**
- Rewrite: `src/Prague.Core/Collections/PooledSet.cs` (complete file below)
- Modify: `src/Prague.Core/Collections/ValueSet.cs:1275-1289` and `:1424-1438` (the two `IntersectWith(PooledSet…)` overloads)
- Modify: `tests/Prague.Core.Tests/DataStructures/PooledSetLifetimeTests.cs` (two `set.Slots` reads → `GetSnapshot`)
- Possibly modify: `tests/Prague.Core.Tests/DataStructures/PooledSetTests.cs` (only if it references the removed `Slots`/`LastIndex` internals — port mechanically to `GetSnapshot`, do not weaken assertions)

**Interfaces:**
- Consumes: `ReaderGate.Enter/Exit/Retire`, `ReaderGate.IRetirable` (Task 5).
- Produces: `internal void GetSnapshot(out HashSlot<T>[] slots, out int[]? versions, out int lastIndex)` — replaces the removed `Slots`/`LastIndex` properties (Task 8 stress tests and ValueSet use it). `versions` is null exactly when `T` copies atomically.
- Public API otherwise unchanged: `Add`, `Remove`, `Contains`, `Count`, `IsEmpty`, `GetEnumerator` (ref struct), `IEnumerable<T>`, `Dispose`, `Empty`.

- [ ] **Step 1: Replace `src/Prague.Core/Collections/PooledSet.cs` with the new implementation**

```csharp
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

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal bool ContainsWithHashCode(T item, int hashCode) {
		var slot = ReaderGate.Enter();
		try {
			return ContainsCore(item, hashCode);
		}
		finally {
			ReaderGate.Exit(slot);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
```

- [ ] **Step 2: Update the two `ValueSet.IntersectWith(PooledSet…)` overloads**

At `ValueSet.cs` ~line 1275 (the `TFrom` variant inside the two-type `IncrementalIntersecter<TFrom, TInto>`), replace the method with:

```csharp
		[MethodImpl(MethodImplOptions.AggressiveOptimization)]
		public void IntersectWith(PooledSet<TFrom, DefaultKeyComparer<TFrom>> other) {
			if (IsCleared) return;
			var gate = ReaderGate.Enter();
			try {
				// Single consistent snapshot — reading through separate properties could
				// straddle a concurrent Grow and go out of bounds. The gate pin keeps the
				// arrays out of the pool for the whole scan.
				other.GetSnapshot(out var slots, out var versions, out var lastIndex);
				ref var start = ref MemoryMarshal.GetArrayDataReference(slots);
				for (var i = 0; i < lastIndex; i++) {
					ref var slot = ref Unsafe.Add(ref start, i);
					if (versions == null) {
						// Atomically-copyable T: stale-or-new, never torn.
						var hashCode = Volatile.Read(ref slot.HashCode);
						if (hashCode < 0) continue;
						var index = _self.InternalIndexOf(_into.Into(slot.Value));
						if (index >= 0)
							_bitHelper.MarkBit(index);
					}
					else {
						// Version-guarded copy-out (multi-word T): rejects torn copies
						// even under remove + re-add with an equal hash (ABA).
						var version = Volatile.Read(ref versions[i]);
						var hashCode = Volatile.Read(ref slot.HashCode);
						if (hashCode < 0) continue;
						var value = slot.Value;
						if (Volatile.Read(ref versions[i]) != version) continue;
						var index = _self.InternalIndexOf(_into.Into(value));
						if (index >= 0)
							_bitHelper.MarkBit(index);
					}
				}
			}
			finally {
				ReaderGate.Exit(gate);
			}
		}
```

At ~line 1424 (the single-type variant), same shape but the lookup uses the hash-code overload:

```csharp
		[MethodImpl(MethodImplOptions.AggressiveOptimization)]
		public void IntersectWith(PooledSet<T, DefaultKeyComparer<T>> other) {
			if (IsCleared) return;
			var gate = ReaderGate.Enter();
			try {
				other.GetSnapshot(out var slots, out var versions, out var lastIndex);
				ref var start = ref MemoryMarshal.GetArrayDataReference(slots);
				for (var i = 0; i < lastIndex; i++) {
					ref var slot = ref Unsafe.Add(ref start, i);
					if (versions == null) {
						var hashCode = Volatile.Read(ref slot.HashCode);
						if (hashCode < 0) continue;
						var index = _self.InternalIndexOf(slot.Value, hashCode);
						if (index >= 0)
							_bitHelper.MarkBit(index);
					}
					else {
						var version = Volatile.Read(ref versions[i]);
						var hashCode = Volatile.Read(ref slot.HashCode);
						if (hashCode < 0) continue;
						var value = slot.Value;
						if (Volatile.Read(ref versions[i]) != version) continue;
						var index = _self.InternalIndexOf(value, hashCode);
						if (index >= 0)
							_bitHelper.MarkBit(index);
					}
				}
			}
			finally {
				ReaderGate.Exit(gate);
			}
		}
```

- [ ] **Step 3: Port the two `set.Slots` reads in `PooledSetLifetimeTests.cs`**

Replace both occurrences of:

```csharp
		var liveSlots = set.Slots;
```

with:

```csharp
		set.GetSnapshot(out var liveSlots, out _, out _);
```

and in `StructEnumerator_GrowMidIteration_KeepsServingCapturedSnapshot` replace:

```csharp
		var slotsBeforeGrow = set.Slots;
```
```csharp
			var slotsAfterGrow = set.Slots;
```

with:

```csharp
		set.GetSnapshot(out var slotsBeforeGrow, out _, out _);
```
```csharp
			set.GetSnapshot(out var slotsAfterGrow, out _, out _);
```

Do not change any assertion.

- [ ] **Step 4: Build; mechanically fix any `Slots`/`LastIndex` references in `PooledSetTests.cs`**

Run: `dotnet build Prague.sln`
If `tests/Prague.Core.Tests/DataStructures/PooledSetTests.cs` references the removed members, port them to `GetSnapshot` without weakening assertions. Any other compile error means a missed call site — fix by the same pattern (GetSnapshot + gate if it scans the arrays).

- [ ] **Step 5: Run the PooledSet suites — all 5 lifetime tests must now pass**

Run: `dotnet test tests/Prague.Core.Tests --filter "FullyQualifiedName~PooledSet"`
Expected: all PASS, including `BoxedEnumerator_DisposeMidIteration_KeepsServingOriginalKeys` and `BoxedEnumerator_DisposeMidIteration_NextRenterCannotOverwriteLiveView` (failing until now).

- [ ] **Step 6: Run the full test filter for joins/queries that consume PooledSet**

Run: `dotnet test tests/Prague.Core.Tests && dotnet test tests/Prague.Generated.Tests && dotnet test tests/Prague.DI.Tests`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Prague.Core/Collections/PooledSet.cs src/Prague.Core/Collections/ValueSet.cs tests/Prague.Core.Tests/DataStructures/PooledSetLifetimeTests.cs tests/Prague.Core.Tests/DataStructures/PooledSetTests.cs
git commit -m "fix: PooledSet generation-refcounted pooling ends use-after-return

Tables generation published with one volatile store; escaping enumerators
pin it (boxed enumerator finally participates — the corruption bug),
scoped readers ride the ReaderGate grace period, Grow retires the old
generation (previously leaked from the pool forever). Version-guarded
copy-out compiles only for multi-word T; long/int/string keys keep the
plain read loop. Arrays remain ArrayPool-rented throughout."
```

---

### Task 8: Concurrency stress tests

**Files:**
- Create: `tests/Prague.Core.Tests/DataStructures/ConcurrentReclamationStressTests.cs`

**Interfaces:**
- Consumes: `PooledBTree` public API, `PooledSet` public API, `ReaderGate.TryDrain` (Tasks 5–7).

- [ ] **Step 1: Write the stress tests**

```csharp
namespace Prague.Core.Tests.DataStructures;

	using Prague.Core.Collections;
	using NUnit.Framework;

/// <summary>
///   Single writer churning structure state (forcing node retirement / generation
///   turnover) against parallel lock-free readers. Values carry a high-bit marker so
///   any recycled/foreign memory served to a reader is detected immediately: writers
///   only ever store marker-tagged values, so an untagged value can only come from a
///   pool-recycled array. Reads may be STALE (that is the documented model) — the
///   assertions check integrity, never freshness.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ConcurrentReclamationStressTests {
	private const long Marker = 0x5AA5_0000_0000_0000;
	private const int Items = 512;

	private static long Encode(int i) => Marker | (uint)i;

	private static bool IsValid(long v)
		=> (v & unchecked((long)0xFFFF_0000_0000_0000)) == Marker && (v & 0xFFFF_FFFF) < Items;

	private struct CollectingAggregator : PooledBTree<long, long>.IResultAggregator {
		public List<(long Index, long Value)> Items;

		public void Add(long index, long value) => Items.Add((index, value));

		public void Dispose() { }
	}

	private static TimeSpan Duration => TimeSpan.FromSeconds(2);

	[Test]
	public void BTree_WriterChurn_ConcurrentReaders_NeverServeCorruptPairs() {
		var tree = new PooledBTree<long, long>();
		var current = new long[Items];
		for (var i = 0; i < Items; i++) {
			current[i] = 1000;
			tree.Add(1000, Encode(i));
		}

		var stop = false;
		Exception? writerError = null;
		var writer = new Thread(() => {
			try {
				var round = 1;
				while (!Volatile.Read(ref stop)) {
					// The incident pattern: shared keys migrate, leaves drain and
					// retire, splits rent new nodes — maximum reclamation churn.
					var newKey = 1000 + round;
					for (var i = 0; i < Items; i++) {
						tree.Add(newKey, Encode(i));
						if (!tree.Remove(current[i], Encode(i)))
							throw new InvalidOperationException($"leak: round {round}, item {i}");
						current[i] = newKey;
					}

					round++;
				}
			}
			catch (Exception ex) {
				writerError = ex;
			}
		});

		var readerErrors = new List<Exception>();
		var readers = new Thread[4];
		for (var r = 0; r < readers.Length; r++) {
			readers[r] = new Thread(() => {
				try {
					var agg = new CollectingAggregator { Items = new List<(long, long)>(Items * 4) };
					while (!Volatile.Read(ref stop)) {
						agg.Items.Clear();
						tree.RangeFrom(0, ref agg);
						for (var i = 0; i < agg.Items.Count; i++) {
							var (key, value) = agg.Items[i];
							if (!IsValid(value))
								throw new InvalidOperationException(
									$"foreign pair served: ({key}, 0x{value:X}) — recycled memory reached a reader");
						}

						tree.Contains(1000, Encode(0));
						tree.TryGetMin(out _, out _);
						tree.TryGetMax(out _, out _);
					}
				}
				catch (Exception ex) {
					lock (readerErrors) {
						readerErrors.Add(ex);
					}
				}
			});
		}

		writer.Start();
		foreach (var t in readers)
			t.Start();
		Thread.Sleep(Duration);
		Volatile.Write(ref stop, true);
		writer.Join();
		foreach (var t in readers)
			t.Join();

		Assert.That(writerError, Is.Null, writerError?.ToString());
		Assert.That(readerErrors, Is.Empty, readerErrors.Count > 0 ? readerErrors[0].ToString() : null);
		Assert.That(tree.Length, Is.EqualTo(Items), "every round is add-then-remove balanced");
		tree.Dispose();
		ReaderGate.TryDrain();
	}

	[Test]
	public void PooledSet_ChurnGrowDispose_ConcurrentReaders_NeverServeForeignValues() {
		var published = new PooledSet<long, DefaultKeyComparer<long>>();
		for (var i = 0; i < Items; i++)
			published.Add(Encode(i));

		var stop = false;
		Exception? writerError = null;
		var writer = new Thread(() => {
			try {
				while (!Volatile.Read(ref stop)) {
					// Bucket lifecycle churn: fill past DefaultCapacity (forces Grow),
					// drain to zero, dispose (retires the generation), replace.
					var replacement = new PooledSet<long, DefaultKeyComparer<long>>();
					for (var i = 0; i < Items; i++)
						replacement.Add(Encode(i));

					var old = Interlocked.Exchange(ref published, replacement);
					for (var i = 0; i < Items; i++) {
						if (!old.Remove(Encode(i)))
							throw new InvalidOperationException($"lost value {i}");
					}

					old.Dispose();
				}
			}
			catch (Exception ex) {
				writerError = ex;
			}
		});

		var readerErrors = new List<Exception>();
		var readers = new Thread[4];
		for (var r = 0; r < readers.Length; r++) {
			readers[r] = new Thread(() => {
				try {
					while (!Volatile.Read(ref stop)) {
						var set = Volatile.Read(ref published);

						// struct enumerator (pinned generation)
						foreach (var value in set) {
							if (!IsValid(value))
								throw new InvalidOperationException($"foreign value 0x{value:X} (struct enum)");
						}

						// boxed enumerator — the GetValues path that corrupted upstream
						IEnumerable<long> view = set;
						foreach (var value in view) {
							if (!IsValid(value))
								throw new InvalidOperationException($"foreign value 0x{value:X} (boxed enum)");
						}

						// gate-protected scoped reader
						set.Contains(Encode(7));
					}
				}
				catch (Exception ex) {
					lock (readerErrors) {
						readerErrors.Add(ex);
					}
				}
			});
		}

		writer.Start();
		foreach (var t in readers)
			t.Start();
		Thread.Sleep(Duration);
		Volatile.Write(ref stop, true);
		writer.Join();
		foreach (var t in readers)
			t.Join();

		Assert.That(writerError, Is.Null, writerError?.ToString());
		Assert.That(readerErrors, Is.Empty, readerErrors.Count > 0 ? readerErrors[0].ToString() : null);
		published.Dispose();
		ReaderGate.TryDrain();
	}
}
```

Note: the lambdas capture `published`/`stop`/`current` — C# hoists them into a display class, so `Volatile.Read(ref …)` / `Interlocked.Exchange(ref …)` compile against the captured fields. If the compiler complains, hoist those locals to private fixture fields — do not weaken the volatile handoff.

- [ ] **Step 2: Run repeatedly**

Run: `for i in 1 2 3; do dotnet test tests/Prague.Core.Tests --filter "FullyQualifiedName~ConcurrentReclamationStressTests" --framework net9.0 || break; done`
Expected: 3× PASS. Any failure is a real protocol bug — debug it, do not retry-until-green.

- [ ] **Step 3: Commit**

```bash
git add tests/Prague.Core.Tests/DataStructures/ConcurrentReclamationStressTests.cs
git commit -m "test: writer-churn vs lock-free-reader stress for gate/generation reclamation"
```

---

### Task 9: Benchmarks + baseline comparison (the acceptance gate)

**Files:**
- Create: `benchmarks/Prague.Benchmarks/PooledSetBenchmarks.cs`
- Create: `benchmarks/Prague.Benchmarks/BTreeChurnBenchmarks.cs`
- Baseline runs in a separate git worktree on `main`; both new benchmark files use only API that exists on both branches (`Add`/`Remove`/`Contains`/`foreach`/`Dispose`/`Range*`).

- [ ] **Step 1: Write `PooledSetBenchmarks.cs`**

```csharp
namespace Prague.Benchmarks;

	using Prague.Core.Collections;
	using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class PooledSetBenchmarks {
	private PooledSet<long, DefaultKeyComparer<long>> _set = null!;

	[Params(100, 5000)] public int ItemCount { get; set; }

	[GlobalSetup]
	public void Setup() {
		_set = new PooledSet<long, DefaultKeyComparer<long>>();
		for (long i = 0; i < ItemCount; i++)
			_set.Add(i);
	}

	[GlobalCleanup]
	public void Cleanup() => _set.Dispose();

	[Benchmark]
	public void AddRemoveChurn() {
		for (long i = 0; i < 128; i++)
			_set.Add(1_000_000 + i);
		for (long i = 0; i < 128; i++)
			_set.Remove(1_000_000 + i);
	}

	[Benchmark]
	public int Contains_HitAndMiss() {
		var hits = 0;
		for (long i = 0; i < 128; i++) {
			if (_set.Contains(i))
				hits++;
			if (_set.Contains(-1 - i))
				hits++;
		}

		return hits;
	}

	[Benchmark]
	public long EnumerateStruct() {
		long sum = 0;
		foreach (var v in _set)
			sum += v;
		return sum;
	}

	[Benchmark]
	public long EnumerateBoxed() {
		long sum = 0;
		IEnumerable<long> view = _set;
		foreach (var v in view)
			sum += v;
		return sum;
	}

	// The per-message Many-index bucket lifecycle: a key appears (set created, one
	// item), then leaves (bucket empties, set disposed). This is THE pooling
	// round-trip the fix must not regress.
	[Benchmark]
	public void CreateAddDispose_SingletonBucket() {
		for (var i = 0; i < 16; i++) {
			var set = new PooledSet<long, DefaultKeyComparer<long>>();
			set.Add(42);
			set.Remove(42);
			set.Dispose();
		}
	}
}
```

- [ ] **Step 2: Write `BTreeChurnBenchmarks.cs`**

```csharp
namespace Prague.Benchmarks;

	using Prague.Core.Collections;
	using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class BTreeChurnBenchmarks {
	private PooledBTree<long, long> _uniqueTree = null!;
	private PooledBTree<long, long> _sharedTree = null!;
	private long _round;

	[Params(1000)] public int Items { get; set; }

	private struct CountingAggregator : PooledBTree<long, long>.IResultAggregator {
		public long Sum;

		public void Add(long index, long value) => Sum += value;

		public void Dispose() { }
	}

	[GlobalSetup]
	public void Setup() {
		_uniqueTree = new PooledBTree<long, long>();
		_sharedTree = new PooledBTree<long, long>();
		for (long i = 0; i < Items; i++) {
			_uniqueTree.Add(i * 1000, i); // unique keys — the fast-path-only workload
			_sharedTree.Add(1000, i); // one shared key — cross-leaf run workload
		}

		_round = 1;
	}

	[GlobalCleanup]
	public void Cleanup() {
		_uniqueTree.Dispose();
		_sharedTree.Dispose();
	}

	// No-regression proof: unique keys never enter any slow path.
	[Benchmark]
	public void UpdateChurn_UniqueKeys() {
		var round = _round++;
		for (long i = 0; i < Items; i++) {
			_uniqueTree.Add(i * 1000 + round, i);
			_uniqueTree.Remove(i * 1000 + round - 1, i);
		}
	}

	// The incident workload — broken (silently leaking) before the fix, so this row
	// has no meaningful baseline; it documents the cost of correct behavior.
	[Benchmark]
	public void UpdateChurn_SharedKey() {
		var round = _round++;
		for (long i = 0; i < Items; i++) {
			_sharedTree.Add(1000 + round, i);
			_sharedTree.Remove(1000 + round - 1, i);
		}
	}

	[Benchmark]
	public long RangeFrom_ScanAll() {
		var agg = new CountingAggregator();
		_uniqueTree.RangeFrom(0, ref agg);
		return agg.Sum;
	}

	[Benchmark]
	public long RangeFromExclusive_ScanAll() {
		var agg = new CountingAggregator();
		_uniqueTree.RangeFromExclusive(0, ref agg);
		return agg.Sum;
	}

	[Benchmark]
	public int Contains_HitAndMiss() {
		var hits = 0;
		for (long i = 0; i < 128; i++) {
			if (_uniqueTree.Contains(i * 1000, i))
				hits++;
			if (_uniqueTree.Contains(i * 1000 + 1, i))
				hits++;
		}

		return hits;
	}
}
```

Note: `UpdateChurn_SharedKey` on the BASELINE leaks (Remove returns false, the tree grows every iteration), so its baseline numbers measure an ever-growing tree — report it but exclude it from the regression comparison. Apples-to-apples rows: `UpdateChurn_UniqueKeys`, `RangeFrom*`, `Contains_*`, and all PooledSet rows.

- [ ] **Step 3: Commit the benchmarks, run them on the fix branch**

```bash
git add benchmarks/Prague.Benchmarks/PooledSetBenchmarks.cs benchmarks/Prague.Benchmarks/BTreeChurnBenchmarks.cs
git commit -m "bench: PooledSet + BTree churn benchmarks for the perf-neutrality gate"
dotnet run -c Release --project benchmarks/Prague.Benchmarks -- \
  --filter "*PooledSetBenchmarks*" "*BTreeChurnBenchmarks*" --job short 2>&1 | tail -60
```

- [ ] **Step 4: Run the same benchmarks on `main` in a worktree**

```bash
git worktree add ../prague.net-baseline main
cp benchmarks/Prague.Benchmarks/PooledSetBenchmarks.cs benchmarks/Prague.Benchmarks/BTreeChurnBenchmarks.cs \
   ../prague.net-baseline/benchmarks/Prague.Benchmarks/
(cd ../prague.net-baseline && dotnet run -c Release --project benchmarks/Prague.Benchmarks -- \
  --filter "*PooledSetBenchmarks*" "*BTreeChurnBenchmarks*" --job short 2>&1 | tail -60)
git worktree remove --force ../prague.net-baseline
```

- [ ] **Step 5: Compare, record, gate**

Compare summaries row by row (Mean, Allocated). Acceptance gate from the spec:
- No throughput regression outside run-to-run noise on the apples-to-apples rows.
- Zero new allocs/op on those rows (limbo batches only allocate under reader-pinned retirement, which single-threaded benchmarks never trigger).

Write both tables plus a delta column into `docs/superpowers/plans/2026-07-10-pr56-bench-results.md` and commit. If a row regresses beyond noise: investigate (likely suspects: lost inlining on gated wrappers, the `_tables` indirection in Add/Remove), fix, re-run — never accept a regression silently.

```bash
git add docs/superpowers/plans/2026-07-10-pr56-bench-results.md
git commit -m "bench: baseline vs fix results for the perf-neutrality gate"
```

---

### Task 10: Contract docs, full suite, PR

**Files:**
- Modify: `src/Prague.Core/Collections/PooledBTree.cs` (header doc comment lines 12–13)
- Modify: `context/collections.md` (PooledSet/PooledBTree reclamation model)

- [ ] **Step 1: Update the PooledBTree header comment**

Replace the two `Thread safety:` lines of the class doc with:

```csharp
///   Thread safety: single writer serialized by the internal write lock; readers are
///   lock-free with documented staleness (a concurrent range scan may transiently skip
///   or double-see an entry during in-leaf shifts, and results reflect no single point
///   in time). Structurally removed nodes are retired through ReaderGate and return to
///   the pool only after the reader grace period, so a parked reader never observes
///   recycled memory.
```

- [ ] **Step 2: Update `context/collections.md`**

In the PooledSet and PooledBTree sections, replace descriptions of the old refcount/immediate-return behavior with 3–5 lines covering: Tables generation + pins for escaping enumerators, ReaderGate grace-period reclamation for scoped readers and B-tree nodes, the AtomicCopy specialization. Keep the file's thin-overview tone.

- [ ] **Step 3: Full solution build + every test project**

Run: `dotnet build Prague.sln && dotnet test Prague.Tests.slnf`
Expected: build clean, all suites PASS. Kafka integration tests may fail on missing local Kafka (connection refused) — note it and run remaining projects individually; never skip Core/Generated/DI/Kafka-unit.

- [ ] **Step 4: Commit docs, push, open PR**

```bash
git add src/Prague.Core/Collections/PooledBTree.cs context/collections.md
git commit -m "docs: state the single-writer / lock-free-reader reclamation contract"
git push -u origin fix/pr56-perf-neutral-port
gh pr create --title "Fix B-tree equal-key-run leak and PooledSet use-after-return (perf-neutral port of internal PR #56)" --body "$(cat <<'EOF'
Perf-neutral port of internal-be-kafka-cache-lib#56. Fixes, with all pooling preserved:

1. **PooledBTree leak**: Remove/Contains/Add missed pairs in equal-key runs spanning
   leaves (every CacheRangeIndex.Update on duplicate keys leaked an entry forever).
   Fast path unchanged; the cross-leaf slow path runs only where the old code
   returned wrong answers. Exclusive-bound range scans no longer leak equal keys.
2. **PooledSet use-after-return**: boxed enumerators (the GetValues path) now pin
   their Tables generation; Grow finally returns old arrays; scoped readers are
   covered by the new ReaderGate grace period. Deterministic single-threaded repro
   included (enumeration previously yielded foreign pool data).
3. **CacheRangeIndex.GetCounters** reports real PooledBTree.Length so index drift is
   visible instead of masked.

Reclamation design: ReaderGate — readers pin with two plain stores to a padded
per-thread slot (zero atomic ops); writers reclaim after
Interlocked.MemoryBarrierProcessWide via a sequence-stamped grace-period limbo.
Slots recycle through a finalizer-backed free list.

Spec: docs/superpowers/specs/2026-07-10-pr56-perf-neutral-port-design.md
Benchmarks (baseline vs fix): docs/superpowers/plans/2026-07-10-pr56-bench-results.md

## Test plan
- [x] 7 ported B-tree duplicate-run repro tests (failed before, pass after)
- [x] 5 PooledSet lifetime repro tests (2 failed before, pass after)
- [x] ReaderGate unit tests + writer-churn vs parallel-reader stress tests
- [x] Full Prague.Tests.slnf green
- [x] Benchmark gate: no regression outside noise, zero new allocs/op

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review Notes

- Spec coverage: §1 fast-path fixes → Tasks 1–3; §2 ReaderGate (grace period, slot recycling) → Task 5, B-tree integration → Task 6; §3 PooledSet Tables/pins/AtomicCopy/FreeNext/ordered publication → Task 7; §4 GetCounters → Task 4; §5 contract docs → Task 10; spec Testing → repro tests (already committed) + Tasks 5/7/8; spec Benchmarks → Task 9.
- Type consistency: `ReaderGate.Slot Enter()` / `Exit(Slot)` / `Retire(IRetirable)` / `TryDrain()` / `RegisteredSlotCount` used identically across Tasks 5–8. `GetSnapshot(out HashSlot<T>[], out int[]?, out int)` identical in Task 7's PooledSet, ValueSet, and lifetime tests.
- Executor watch-points: (a) gated wrappers lose inlining — benchmark rows `Contains_*`/`RangeFrom*` are the canary; (b) `allows ref struct` generic wrappers must compile on both `net9.0` and `net10.0`; (c) Task 8's closure-capture note; (d) stress failures are protocol bugs, never flakes to retry away.
