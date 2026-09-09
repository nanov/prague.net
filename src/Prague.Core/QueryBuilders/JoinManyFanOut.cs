namespace Prague.Core;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Prague.Core.Collections;

// ── JoinMany fan-out: one pair set, right → extra-lefts chains ─────────────
//
// A paired core stores its candidates as ValueSet<JoinedKeyPair<TLeftKey, TRightKey>> whose identity
// is the RIGHT key only, so one set cannot carry (L1, r) and (L2, r) at the same time. Every JoinMany
// resolver therefore keeps ONE pair per distinct right in the set — its JoinedKey is the first left
// that recorded the right, which is enough for the user's filter (UseIndex / Where / Or all narrow by
// right key) and for one paired execute — and records on the side which OTHER lefts each right belongs
// to: a chain of left keys per pair slot, in pooled arrays. Delivery runs the slot-reporting paired
// execute once: the store hands the container (first left, pair slot, value) per surviving right, the
// container adds the value to the first left and then to every left in the slot's chain — no lookup.
//
// The chains are lazy. While no right is recorded by a second left — the ordinary FK join, where every
// right belongs to exactly one left — nothing but the pair itself is written and no chain array is
// rented, so the fan-out costs what a plain pair set does; the resolver then skips the delivery as
// well and runs the plain paired execute (see SingleLeftPerRight). The first shared right rents the
// chain arrays and gives every slot recorded so far an empty chain.
//
// Recording is the per-pair hot loop of every JoinMany query, so RecordBucket is written for the
// unshared shape: while no chain exists it does one AddOrFind per right and nothing else — no chain
// bookkeeping, no per-right counter, no per-right test of whether chains exist — and only switches
// to the chain-maintaining loop once a right is actually shared. Measured against the pre-fan-out
// UnionWith loop this is what closes the gap (#72): the fan-out's per-pair extras were a handful of
// instructions, and on 10 000 pairs a handful of instructions is 5%. The bucket's stored hash is
// deliberately NOT reused for the pair: the bucket enumerator's (value, hash) copy is not atomic
// against a concurrent remove + reuse of the slot, so the hash can belong to a key the slot no
// longer holds — and reusing it measured no faster anyway.
//
// Pair slots are stable while nothing is removed (the recording phase only adds) and survive the set's
// growth, and the user filter can only remove pairs — in place — so a slot recorded here still names
// the same right when the surviving pairs are delivered.
//
// A right the bucket enumerator yields twice for the same left (removed and re-added under the walk)
// is recorded once: the walk is per left, so the most recent recorder of that right — the pair's
// first left, or the head of its chain — is the current left exactly when the sighting is a repeat.
// Slot capacities therefore equal the adds a slot can receive.

/// <summary>
/// Pair set plus right → extra-lefts chains for a JoinMany execution: the resolver records every
/// (left, right) pair, hands the set to one paired core and delivers every surviving right to all of
/// its lefts — straight from the store when no right is shared, through
/// <see cref="Delivery{TRightValue,TContainer}"/> otherwise.
/// </summary>
/// <typeparam name="TLeftKey">Left cache's key type (the pair's joined key).</typeparam>
/// <typeparam name="TRightKey">Right cache's key type (the pair's identity).</typeparam>
internal ref struct JoinManyFanOut<TLeftKey, TRightKey>
	where TLeftKey : notnull
	where TRightKey : notnull, IEquatable<TRightKey> {

	internal const int NoChain = -1;
	private const int MinCapacity = 16;

	private ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>> _pairs;
	// True while this instance must dispose the pair set; cleared by the hand-off to the paired core
	// (which disposes its copy) and by Dispose itself. The set is never zeroed — it is ~1 KB of inline
	// storage — its Count stays readable after the hand-off because nothing here touches it again.
	private bool _pairsOwned;
	// Rented by the first shared right. _heads[slot]: first chain node of the pair stored at that slot,
	// or NoChain; a node carries a left recorded for the right AFTER its first one, and the next node.
	private int[]? _heads;
	private TLeftKey[]? _nodeLeft;
	private int[]? _nodeNext;
	private int _nodeCount;

	/// <param name="expectedPairs">Capacity hint for the pair set.</param>
	public JoinManyFanOut(int expectedPairs) {
		_pairs = new ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>>(Math.Max(expectedPairs, MinCapacity));
		_pairsOwned = true;
		_heads = null;
		_nodeLeft = null;
		_nodeNext = null;
		_nodeCount = 0;
	}

	/// <summary>
	/// Distinct rights recorded — the pair set's size as recorded, before any filter narrowed the core's
	/// copy. Still readable after the hand-off, and still the recorded count, never the surviving one.
	/// </summary>
	/// <remarks>
	/// Not <c>readonly</c>, nor is <see cref="PairCount"/>: <c>ValueSet.Count</c> is an auto-property, so a
	/// readonly accessor would copy the whole set (inline storage included) before reading it.
	/// </remarks>
	public int DistinctRights {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _pairs.Count;
	}

	/// <summary>(left, right) pairs recorded so far, repeat sightings excluded. See <see cref="DistinctRights"/>.</summary>
	public int PairCount {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _pairs.Count + _nodeCount;
	}

	/// <summary>
	/// True while every recorded right belongs to exactly one left, so a pair's JoinedKey is its whole
	/// chain: no chain array has been rented and the resolver runs the plain paired execute instead of
	/// a <see cref="Delivery{TRightValue,TContainer}"/>.
	/// </summary>
	public readonly bool SingleLeftPerRight => _nodeCount == 0;

	/// <summary>Whether the chain arrays exist — rented by the first right a second left recorded.</summary>
	internal readonly bool HasChains => _heads is not null;

	/// <summary>
	/// The pair set — one pair per distinct right, its JoinedKey the first left that recorded it. The
	/// resolver builds the paired core over a copy and then calls <see cref="MarkPairsHandedOff"/>.
	/// </summary>
	public ref ValueSet<JoinedKeyPair<TLeftKey, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TLeftKey, TRightKey>>> Pairs {
		[UnscopedRef]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get {
			// After the hand-off the core owns the arrays this copy still points at; a write through here
			// would land in memory another query may have rented since.
			Debug.Assert(_pairsOwned, "pair set accessed after the hand-off");
			return ref _pairs;
		}
	}

	/// <summary>
	/// Records (left, right) for every right in <paramref name="bucket"/> — one enumeration, the
	/// bucket's own hash-free walk — and returns how many were recorded, repeat sightings excluded:
	/// the exact capacity the left's slot must reserve.
	/// </summary>
	public int RecordBucket(TLeftKey left, PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket) {
		Debug.Assert(_pairsOwned, "recording after the hand-off");
		ref var pairs = ref _pairs;
		var before = pairs.Count + _nodeCount;
		using var rights = bucket.GetEnumerator();

		if (_heads is null) {
			// No right is shared yet: a new right is one insert and nothing else. The first shared
			// right rents the chains, after which every new slot needs its head set — the loop below
			// takes over the rest of this bucket.
			while (rights.MoveNext()) {
				if (pairs.AddOrFind(new(left, rights.Current), out var slot))
					continue;

				RecordShared(left, slot);
				if (_heads is not null)
					goto chained;
			}

			return pairs.Count + _nodeCount - before;
		}

		chained:
		while (rights.MoveNext()) {
			if (pairs.AddOrFind(new(left, rights.Current), out var slot))
				SetEmptyHead(slot);
			else
				RecordShared(left, slot);
		}

		return pairs.Count + _nodeCount - before;
	}

	/// <summary>
	/// Records one (left, right). False when the same left already recorded this right — the bucket
	/// enumerator yielded it twice — so the caller leaves it out of the slot capacity. Production
	/// records through <see cref="RecordBucket"/>; this is the test-side entry point that drives the same
	/// <see cref="SetEmptyHead"/> / <see cref="RecordShared"/> contract one pair at a time.
	/// </summary>
	internal bool Record(TLeftKey left, TRightKey right) {
		Debug.Assert(_pairsOwned, "recording after the hand-off");
		if (_pairs.AddOrFind(new(left, right), out var slot)) {
			if (_heads is not null)
				SetEmptyHead(slot);

			return true;
		}

		return RecordShared(left, slot);
	}

	// Chains exist, so a new slot needs an empty one. Nothing is removed while recording: new rights
	// take slots 0, 1, 2, … in order.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SetEmptyHead(int slot) {
		var heads = _heads!;
		if ((uint)slot >= (uint)heads.Length)
			heads = GrowHeads();

		heads[slot] = NoChain;
	}

	// The right already belongs to a left: cold next to the FK shape, where every right is new. A repeat
	// sighting for the same left records nothing; another left joins the right's chain.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool RecordShared(TLeftKey left, int slot) {
		var head = _heads is null ? NoChain : _heads[slot];
		var latest = head == NoChain ? _pairs.ValueAt(slot).JoinedKey : _nodeLeft![head];
		if (EqualityComparer<TLeftKey>.Default.Equals(latest, left))
			return false;

		if (_heads is null)
			RentChains();
		else if (_nodeCount == _nodeLeft!.Length)
			GrowNodes();

		var node = _nodeCount;
		_nodeLeft![node] = left;
		_nodeNext![node] = head;
		_heads![slot] = node;
		_nodeCount = node + 1;
		return true;
	}

	/// <summary>
	/// The pair set now belongs to the paired core that received a copy of it (the core disposes it):
	/// <see cref="Dispose"/> leaves the pairs alone from here on and only returns the chains.
	/// </summary>
	public void MarkPairsHandedOff() {
		// SingleLeftPerRight (no chain node) and HasChains (arrays rented) are two views of one fact: the
		// first shared right rents the chains and writes its node in the same call.
		Debug.Assert(HasChains == !SingleLeftPerRight, "chain arrays without a node, or a node without arrays");
		_pairsOwned = false;
	}

	// Chain storage for a Delivery, valid once some right is shared (!SingleLeftPerRight); the arrays
	// stay owned (and returned) by this instance.
	internal readonly int[] Heads => _heads!;
	internal readonly TLeftKey[] NodeLefts => _nodeLeft!;
	internal readonly int[] NodeNexts => _nodeNext!;

	public void Dispose() {
		if (_pairsOwned && _pairs.IsInitlized)
			_pairs.Dispose();

		_pairsOwned = false;
		if (_heads is not null) {
			PragueArrayPool<int>.Pool.Return(_heads);
			_heads = null;
		}

		if (_nodeLeft is not null) {
			PragueArrayPool<TLeftKey>.Pool.Return(_nodeLeft, RuntimeHelpers.IsReferenceOrContainsReferences<TLeftKey>());
			_nodeLeft = null;
		}

		if (_nodeNext is not null) {
			PragueArrayPool<int>.Pool.Return(_nodeNext);
			_nodeNext = null;
		}

		_nodeCount = 0;
	}

	// First shared right: rent the chains and give every slot recorded so far an empty one. Slots that
	// arrive later get theirs in SetEmptyHead.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RentChains() {
		var recorded = _pairs.Count;
		var heads = PragueArrayPool<int>.Pool.Rent(Math.Max(recorded, MinCapacity));
		heads.AsSpan(0, recorded).Fill(NoChain);
		_heads = heads;
		_nodeLeft = PragueArrayPool<TLeftKey>.Pool.Rent(MinCapacity);
		_nodeNext = PragueArrayPool<int>.Pool.Rent(MinCapacity);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private int[] GrowHeads() {
		var heads = _heads!;
		var grown = PragueArrayPool<int>.Pool.Rent(heads.Length * 2);
		Array.Copy(heads, grown, heads.Length);
		PragueArrayPool<int>.Pool.Return(heads);
		_heads = grown;
		return grown;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void GrowNodes() {
		var nodeLeft = _nodeLeft!;
		var nodeNext = _nodeNext!;
		var length = nodeLeft.Length;
		var lefts = PragueArrayPool<TLeftKey>.Pool.Rent(length * 2);
		int[] nexts;
		try {
			nexts = PragueArrayPool<int>.Pool.Rent(length * 2);
		} catch {
			// The first rental has no owner yet; hand it back before the second one's failure propagates.
			PragueArrayPool<TLeftKey>.Pool.Return(lefts, RuntimeHelpers.IsReferenceOrContainsReferences<TLeftKey>());
			throw;
		}

		Array.Copy(nodeLeft, lefts, length);
		Array.Copy(nodeNext, nexts, length);
		PragueArrayPool<TLeftKey>.Pool.Return(nodeLeft, RuntimeHelpers.IsReferenceOrContainsReferences<TLeftKey>());
		PragueArrayPool<int>.Pool.Return(nodeNext);
		_nodeLeft = lefts;
		_nodeNext = nexts;
	}

	/// <summary>
	/// Container for the slot-reporting paired execute: receives one surviving right at a time — as the
	/// first left that recorded it, the pair's slot and the value — and adds the value to that left and
	/// to every left in the slot's chain. The slot is the one the right took when it was recorded: the
	/// user filter only removes pairs in place, so survivors keep their slots and no lookup is needed.
	/// Holds the target container by value — a ref field cannot refer to a ref struct — so the caller
	/// copies <see cref="Inner"/> back once the execute returns. A resolver builds one only when some
	/// right is shared (see <see cref="SingleLeftPerRight"/>), so the chain arrays exist.
	/// </summary>
	internal ref struct Delivery<TRightValue, TContainer> : IJoinedSlotResultContainer<TLeftKey, TRightValue>
		where TContainer : struct, IJoinedResultContainer<TLeftKey, TRightValue>, allows ref struct {
		public TContainer Inner;
		private readonly int[] _heads;
		private readonly TLeftKey[] _nodeLeft;
		private readonly int[] _nodeNext;

		/// <param name="inner">The resolver's result container.</param>
		/// <param name="heads">Per pair slot: the first chain node, or <see cref="NoChain"/>.</param>
		/// <param name="nodeLefts">Chain nodes: a left recorded for the right after its first one.</param>
		/// <param name="nodeNexts">Chain nodes: the next node of the same right, or <see cref="NoChain"/>.</param>
		public Delivery(TContainer inner, int[] heads, TLeftKey[] nodeLefts, int[] nodeNexts) {
			Inner = inner;
			_heads = heads;
			_nodeLeft = nodeLefts;
			_nodeNext = nodeNexts;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Add(TLeftKey firstLeft, int slot, TRightValue value) {
			// The pair's JoinedKey is the first left that recorded the right; it always receives.
			Inner.Add(firstLeft, value);

			// Every slot the walk can report was recorded, and every recorded slot has a head (RentChains
			// covers the slots before it, SetEmptyHead the ones after). A slot past the heads would mean a
			// filter rebuilt the pair set — a query never fails, so the right reaches its first left only.
			var heads = _heads;
			Debug.Assert((uint)slot < (uint)heads.Length, "a delivered slot the recording never saw");
			if ((uint)slot >= (uint)heads.Length)
				return;

			for (var node = heads[slot]; node != NoChain; node = _nodeNext[node])
				Inner.Add(_nodeLeft[node], value);
		}
	}
}
