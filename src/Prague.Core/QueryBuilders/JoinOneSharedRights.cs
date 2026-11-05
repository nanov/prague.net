namespace Prague.Core;

using System.Runtime.CompilerServices;
using Prague.Core.Collections;

// ── JoinOne shared rights: the lefts a pair set cannot hold ──────────────────
//
// A filtered or inner JoinOne seeds a pair set ValueSet<JoinedKeyPair<TJoined, TRightKey>> whose identity
// is the RIGHT key alone — the filter callback's UseIndex / Where / Or narrow it by right key — so two
// joined keys that reach one right cannot both be in it: the second add is refused, and its lefts used to
// come back without the right (an outer join) or not at all (an inner one). It happens whenever the
// left → right map is not injective: a key selector folding several left keys onto one right key (the
// compound-PK selector FK), a unique left index holding one value under several lefts (a OneToOne FK
// over many-to-one data), or a left-symmetric selector folding several index values onto one right (the
// joined key is then the bucket's left set, and two buckets reach one right). The filtered outer
// left-symmetric join does not come here: it fills its rows by pair slot, so rows sharing a right share the
// slot and need no chain.
//
// This records each refused joined key in a chain hung off the slot of the pair that holds its right —
// the JoinManyFanOut construction, minus the slot sizing a JoinOne does not need. Delivery runs the
// slot-reporting paired execute: the store hands (pair's joined key, slot, value) per surviving right, and
// the value goes to the pair's joined key and to every joined key in the slot's chain. Same right key ⇒
// same index narrowing and same predicate, so that is exact; the store lookup and the predicate still run
// once per distinct right, and nothing is written for a right the store missed or the filter rejected —
// a delivery IS the match, whatever TRightValue is (a struct right has no null to test).
//
// Every joined key is offered once (a left key is; a left-symmetric seeding offers each lookup key's bucket
// once), so a refusal is one O(1) chain record — no chain is ever searched, however many keys share a right.
// Pair slots are stable while the seeding only adds, survive the set's growth, and the filter only removes
// pairs in place, so a recorded slot still names the same right when the surviving pairs are delivered.
// Nothing is rented until a joined key is refused: an injective join pays AddOrFind where it paid Add.

/// <summary>
/// The joined keys a <c>JoinOne</c> pair set refused because their right key was already paired, chained
/// by the slot of the pair that holds the right. <see cref="Deliver{TRightValue,TContainer}"/> hands every
/// delivered right to the pair's joined key and to the refused keys of its slot.
/// </summary>
/// <typeparam name="TJoined">The pair's joined key: a left key, or a left-symmetric bucket's left set.</typeparam>
internal ref struct JoinOneSharedRights<TJoined>
	where TJoined : notnull {

	internal const int NoNode = -1;
	private const int MinCapacity = 16;

	// Rented by the first refused key. _heads[slot]: first chain node of the pair at that slot, or NoNode.
	private int[]? _heads;
	private TJoined[]? _nodeJoined;
	private int[]? _nodeNext;
	private int _nodeCount;

	/// <summary>True once a joined key was refused — only then does the paired execute need a delivery.</summary>
	public readonly bool Any => _nodeCount != 0;

	/// <summary>
	/// Pairs <paramref name="joined"/> with <paramref name="right"/>; when <paramref name="right"/> is already
	/// paired, chains <paramref name="joined"/> to that pair instead. The caller offers each joined key once.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Add<TRightKey>(
		ref ValueSet<JoinedKeyPair<TJoined, TRightKey>, DefaultKeyComparer<JoinedKeyPair<TJoined, TRightKey>>> pairs,
		TJoined joined,
		TRightKey right)
		where TRightKey : notnull, IEquatable<TRightKey> {
		if (!pairs.AddOrFind(new JoinedKeyPair<TJoined, TRightKey>(joined, right), out var slot))
			Record(slot, joined);
	}

	/// <summary>A slot-reporting container that delivers to <paramref name="container"/> as described above. Only valid while <see cref="Any"/>.</summary>
	public readonly Delivery<TRightValue, TContainer> Deliver<TRightValue, TContainer>(TContainer container)
		where TContainer : struct, IJoinedResultContainer<TJoined, TRightValue>, allows ref struct
		=> new(container, _heads!, _nodeJoined!, _nodeNext!);

	public void Dispose() {
		if (_heads is not null) {
			PragueArrayPool<int>.Pool.Return(_heads);
			_heads = null;
		}

		if (_nodeJoined is not null) {
			PragueArrayPool<TJoined>.Pool.Return(_nodeJoined, RuntimeHelpers.IsReferenceOrContainsReferences<TJoined>());
			_nodeJoined = null;
		}

		if (_nodeNext is not null) {
			PragueArrayPool<int>.Pool.Return(_nodeNext);
			_nodeNext = null;
		}

		_nodeCount = 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Record(int slot, TJoined joined) {
		EnsureHead(slot);
		if (_nodeJoined is null) {
			_nodeJoined = PragueArrayPool<TJoined>.Pool.Rent(MinCapacity);
			_nodeNext = PragueArrayPool<int>.Pool.Rent(MinCapacity);
		} else if (_nodeCount == _nodeJoined.Length) {
			GrowNodes();
		}

		_nodeJoined[_nodeCount] = joined;
		_nodeNext![_nodeCount] = _heads![slot];
		_heads[slot] = _nodeCount;
		_nodeCount++;
	}

	private void EnsureHead(int slot) {
		if (_heads is null) {
			_heads = PragueArrayPool<int>.Pool.Rent(Math.Max(slot + 1, MinCapacity));
			_heads.AsSpan().Fill(NoNode);
			return;
		}

		if (slot < _heads.Length)
			return;
		var grown = PragueArrayPool<int>.Pool.Rent(Math.Max(slot + 1, _heads.Length * 2));
		_heads.AsSpan().CopyTo(grown);
		grown.AsSpan(_heads.Length).Fill(NoNode);
		PragueArrayPool<int>.Pool.Return(_heads);
		_heads = grown;
	}

	private void GrowNodes() {
		var length = _nodeJoined!.Length;
		var joined = PragueArrayPool<TJoined>.Pool.Rent(length * 2);
		int[] next;
		try {
			next = PragueArrayPool<int>.Pool.Rent(length * 2);
		} catch {
			PragueArrayPool<TJoined>.Pool.Return(joined, RuntimeHelpers.IsReferenceOrContainsReferences<TJoined>());
			throw;
		}

		Array.Copy(_nodeJoined, joined, _nodeCount);
		Array.Copy(_nodeNext!, next, _nodeCount);
		PragueArrayPool<TJoined>.Pool.Return(_nodeJoined, RuntimeHelpers.IsReferenceOrContainsReferences<TJoined>());
		PragueArrayPool<int>.Pool.Return(_nodeNext!);
		_nodeJoined = joined;
		_nodeNext = next;
	}

	/// <summary>
	/// The slot-reporting container of a shared-right execute: each delivered right goes to the pair's joined
	/// key and to every joined key chained to the pair's slot, through the resolver's own container.
	/// Holds the container by value (a ref field cannot refer to a ref struct); <see cref="Inner"/> reads it back.
	/// </summary>
	internal ref struct Delivery<TRightValue, TContainer> : IJoinedSlotResultContainer<TJoined, TRightValue>
		where TContainer : struct, IJoinedResultContainer<TJoined, TRightValue>, allows ref struct {
		private TContainer _inner;
		private readonly int[] _heads;
		private readonly TJoined[] _nodeJoined;
		private readonly int[] _nodeNext;

		internal Delivery(TContainer inner, int[] heads, TJoined[] nodeJoined, int[] nodeNext) {
			_inner = inner;
			_heads = heads;
			_nodeJoined = nodeJoined;
			_nodeNext = nodeNext;
		}

		public readonly TContainer Inner => _inner;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Add(TJoined foreignKey, int slot, TRightValue result) {
			_inner.Add(foreignKey, result);
			if ((uint)slot >= (uint)_heads.Length)
				return;
			for (var node = _heads[slot]; node != NoNode; node = _nodeNext[node])
				_inner.Add(_nodeJoined[node], result);
		}
	}
}

/// <summary>The single-left helpers of a shared-right inner execute.</summary>
internal static class JoinOneSharedRows {
	/// <summary>
	/// The inner phase's paired execute through a match map, for when lefts share a right (the delivery
	/// records the pair's left and the lefts chained to it) or a struct right meets rows an earlier inner join
	/// created (no null slot tells a miss): <see cref="FillRowsInCandidateOrder"/> creates the matched lefts'
	/// rows, and the post-walk keeps exactly those rows, by key — or, for the first inner join of the chain,
	/// whose rows are all its own matches, only narrows the candidates. <paramref name="handedOff"/> flips
	/// immediately before the execute, as in the plain path.
	/// </summary>
	public static void ExecuteInner<TAccessor, TLeftKey, TRightKey, TRightValue>(
		ref TAccessor accessor,
		ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> candidates,
		ref PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue> core,
		ref JoinOneSharedRights<TLeftKey> shared,
		bool cloneOnAdd,
		bool isFirst,
		ref bool handedOff)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
		var values = new ValueDictionary<TLeftKey, TRightValue, DefaultKeyComparer<TLeftKey>>(true, candidates.Count);
		try {
			var map = new JoinOneValueMap<TLeftKey, TRightValue>(ref values);
			handedOff = true;
			if (shared.Any) {
				var delivery = shared.Deliver<TRightValue, JoinOneValueMap<TLeftKey, TRightValue>>(map);
				core.ExecutePairedJoined(ref delivery);
			} else {
				core.ExecutePaired(ref map);
			}

			FillRowsInCandidateOrder(ref accessor, ref candidates, ref values, cloneOnAdd);
			// Rows an earlier chained inner join created stay only if this join matched them too; the first
			// inner join created no row but its own matches, so it only narrows the candidates.
			if (isFirst)
				accessor.NarrowCandidatesToRows(ref candidates);
			else
				accessor.RetainRows<TLeftKey, KeepRowsInMap<TLeftKey, TRightValue>>(new KeepRowsInMap<TLeftKey, TRightValue>(values), ref candidates);
		} finally {
			values.Dispose(withValues: true);
		}
	}

	/// <summary>
	/// The inner post-walk of a join that matched nothing: every row an earlier chained inner join created
	/// goes, and the candidates are narrowed to nothing. By slot for a reference right (all null), by key for a
	/// struct right (no null to test).
	/// </summary>
	public static void RetainNoRows<TAccessor, TLeftKey, TRightValue>(
		ref TAccessor accessor,
		ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> candidates)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct
		where TLeftKey : notnull, IEquatable<TLeftKey> {
		if (typeof(TRightValue).IsValueType)
			accessor.RetainRows<TLeftKey, KeepNoRows<TLeftKey>>(default, ref candidates);
		else
			accessor.RetainNonNullSlots<TLeftKey, TRightValue>(ref candidates);
	}

	/// <summary>
	/// Creates the rows of the lefts the execute matched — those in <paramref name="values"/> — in candidate
	/// order, writing the right. Rows are created per match, never per candidate, and the order is the eager
	/// inner phase's (candidate order), which the fused frozen fill reproduces.
	/// </summary>
	public static void FillRowsInCandidateOrder<TAccessor, TLeftKey, TRightValue>(
		ref TAccessor accessor,
		ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> candidates,
		ref ValueDictionary<TLeftKey, TRightValue, DefaultKeyComparer<TLeftKey>> values,
		bool cloneOnAdd)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TRightValue : ICacheClonable<TRightValue> {
		foreach (var lk in candidates) {
			if (values.TryGetValue(lk, out var value))
				accessor.GetValueRefOrAddDefault<TLeftKey, TRightValue>(lk, out _) = cloneOnAdd ? value.Clone() : value;
		}
	}
}

/// <summary>A container that records each delivered (left, right) in a map — the matches of an execute, independent of the right's type.</summary>
internal ref struct JoinOneValueMap<TLeftKey, TRightValue> : IJoinedResultContainer<TLeftKey, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey> {
	private ref ValueDictionary<TLeftKey, TRightValue, DefaultKeyComparer<TLeftKey>> _values;

	public JoinOneValueMap(ref ValueDictionary<TLeftKey, TRightValue, DefaultKeyComparer<TLeftKey>> values) {
		_values = ref values;
	}

	public int TotalCount => 1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Add(TLeftKey foreignKey, TRightValue result) {
		_values.GetValueRefOrAddDefault(foreignKey, out _) = result;
		return 0;
	}
}
