namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   The bucket a <c>JoinMany</c> family resolves for one left — the family's own index reads in the
///   family's own order (right-list: the selector then the right list index; left-symmetric: the left
///   index's reverse map, the selector, the right list index; collection: the index half) — so the frozen
///   fill's staleness window is the eager fan-out's. A struct type parameter: the call devirtualizes.
/// </summary>
internal interface IManyBucketSource<TLeftKey, TRightKey>
	where TRightKey : notnull, IEquatable<TRightKey> {
	/// <summary>False when the left has no bucket or an empty one; <paramref name="bucket" /> is then unspecified.</summary>
	bool TryGetBucket(TLeftKey leftKey, out PooledSet<TRightKey, DefaultKeyComparer<TRightKey>> bucket);
}

/// <summary>The last <see cref="JoinManyFusedFill.Window" /> right keys appended to the slot being filled: the linear half of the repeat-sighting check.</summary>
[InlineArray(JoinManyFusedFill.Window)]
internal struct ManyKeyWindow<TRightKey> {
	private TRightKey _k0;
}

internal static class JoinManyFusedFill {
	/// <summary>Keys of one slot compared linearly before the dedupe set takes over.</summary>
	internal const int Window = 16;

	/// <summary>The smallest buffer rented; the size hint (last execution's total) usually makes the first rental the only one.</summary>
	internal const int MinCapacity = 64;
}

/// <summary>
///   The frozen pipeline's <c>JoinMany</c> fill (stage 3 step 8; design §7.2 as implemented): one pass over
///   the rows the pass formed — per left the family's bucket, per right one store lookup — appending the
///   rights into one append-only buffer <i>in row order</i>, so every slot is a contiguous run whose
///   length is known the moment its left is done. No pair set, no shared-buffer partitioning up front, no
///   dictionary lookup per delivery: the rows are addressed by index. The slots are given the buffer once
///   the fill is complete (a grown buffer relocates), as (offset, count) runs of it, and a pooled buffer is
///   registered with the result's disposer exactly as the eager fan-out's shared buffer is. Exactness holds
///   by construction — a slot holds exactly the rights appended for it — so nothing is dropped or marked
///   <c>Truncated</c>, whatever the index writer does concurrently. A right the bucket enumerator yields
///   twice for one left (removed and re-added under the walk) is appended once, the eager fan-out's
///   "delivered once" rule: the slot's last <see cref="JoinManyFusedFill.Window" /> keys are compared
///   linearly and a larger slot dedupes through a set. Only resolvers without a filter callback fill this
///   way (<see cref="IJoinResolver.CanFuse" />): the callback is a builder lambda over the paired core and
///   needs the pair set. An inner resolver drops the rows whose slot stayed empty afterwards.
/// </summary>
internal static class JoinManyFusedFill<TLeftKey, TRightKey, TRightValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TRightKey : notnull, IEquatable<TRightKey>
	where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
	/// <summary>
	///   Fills every row's slot of <paramref name="accessor" /> and returns true when rows were dropped (an
	///   inner resolver's lefts without a right). <paramref name="sizeHint" /> is the previous execution's
	///   total, read to size the first rental and written back (advisory: a plain int).
	/// </summary>
	internal static bool Fill<TAccessor, TSource>(ref TAccessor accessor, TSource source, InMemoryDataCache<TRightKey, TRightValue> store, bool cloneOnAdd, bool inner,
		ref QueryResultsDisposer disposer, ref int sizeHint)
		where TAccessor : struct, IUnsafeValueAccessor, allows ref struct
		where TSource : struct, IManyBucketSource<TLeftKey, TRightKey> {
		var keys = accessor.GetKeys<TLeftKey>();
		var pooled = disposer.IsActive;
		var capacity = Math.Max(sizeHint, JoinManyFusedFill.MinCapacity);
		var buffer = pooled ? PragueArrayPool<TRightValue>.Pool.Rent(capacity) : new TRightValue[capacity];
		var count = 0;
		var empties = 0;
		var handedOff = false;
		var dedupe = default(ValueSet<TRightKey, DefaultKeyComparer<TRightKey>>);
		try {
			var window = default(ManyKeyWindow<TRightKey>);
			for (var i = 0; i < keys.Length; i++) {
				var start = count;
				if (source.TryGetBucket(keys[i], out var bucket)) {
					using var rights = bucket.GetEnumerator();
					while (rights.MoveNext()) {
						var rightKey = rights.Current;
						var n = count - start;
						if (n != 0 && Seen(rightKey, ref window, n, ref dedupe))
							continue;
						if (!store.TryGet(rightKey, out var right))
							continue;
						Remember(rightKey, ref window, n, ref dedupe);
						if (count == buffer.Length)
							buffer = Grow(buffer, count, pooled);
						buffer[count++] = cloneOnAdd ? right.Clone() : right;
					}
				}

				var filled = count - start;
				if (filled == 0)
					empties++;
				accessor.GetSlotAt<QueryResults<TRightValue>>(i).SetFilled(filled);
			}

			// Nothing left here can throw: hand the buffer to the disposer (or back to the pool when nothing
			// was delivered) and give every slot its run.
			var array = buffer;
			if (count == 0) {
				array = [];
				if (pooled)
					PragueArrayPool<TRightValue>.Pool.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<TRightValue>());
			} else if (pooled) {
				disposer.AddPooledBuffer(buffer);
			}

			handedOff = true;
			sizeHint = count;
			var offset = 0;
			for (var i = 0; i < keys.Length; i++)
				offset = accessor.GetSlotAt<QueryResults<TRightValue>>(i).AssignSharedBuffer(array, offset);
		} finally {
			// A user Clone() that throws mid-fill leaves the rental with this frame.
			if (!handedOff && pooled)
				PragueArrayPool<TRightValue>.Pool.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<TRightValue>());
			if (dedupe.IsInitlized)
				dedupe.Dispose();
		}

		if (!inner || empties == 0)
			return false;
		accessor.PruneEmptyManySlots<TRightValue>();
		return true;
	}

	/// <summary>
	///   The inner narrowing without a fill (a count, the bounded flow's pre-heap pass): keeps, in order,
	///   the keys whose bucket holds at least one right the store has — the first hit decides.
	/// </summary>
	internal static int Narrow<TSource, TKey, TValue>(Span<TKey> keys, Span<TValue> values, TSource source, InMemoryDataCache<TRightKey, TRightValue> store)
		where TSource : struct, IManyBucketSource<TLeftKey, TRightKey>
		where TKey : notnull, IEquatable<TKey> {
		var n = 0;
		for (var i = 0; i < keys.Length; i++) {
			if (!HasRight(Unsafe.As<TKey, TLeftKey>(ref keys[i]), ref source, store))
				continue;
			keys[n] = keys[i];
			if (values.Length != 0)
				values[n] = values[i];
			n++;
		}

		return n;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool HasRight<TSource>(TLeftKey leftKey, ref TSource source, InMemoryDataCache<TRightKey, TRightValue> store)
		where TSource : struct, IManyBucketSource<TLeftKey, TRightKey> {
		if (!source.TryGetBucket(leftKey, out var bucket))
			return false;
		using var rights = bucket.GetEnumerator();
		while (rights.MoveNext())
			if (store.TryGet(rights.Current, out _))
				return true;
		return false;
	}

	// n: the rights appended to the slot so far. Up to Window of them sit in the window; past that the
	// set holds them all (SeedDedupe moved the window in).
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool Seen(TRightKey key, ref ManyKeyWindow<TRightKey> window, int n, ref ValueSet<TRightKey, DefaultKeyComparer<TRightKey>> dedupe) {
		if (n > JoinManyFusedFill.Window)
			return dedupe.Contains(key);
		for (var j = 0; j < n; j++)
			if (EqualityComparer<TRightKey>.Default.Equals(window[j], key))
				return true;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Remember(TRightKey key, ref ManyKeyWindow<TRightKey> window, int n, ref ValueSet<TRightKey, DefaultKeyComparer<TRightKey>> dedupe) {
		if (n < JoinManyFusedFill.Window) {
			window[n] = key;
			return;
		}

		if (n == JoinManyFusedFill.Window)
			SeedDedupe(ref window, ref dedupe);
		dedupe.Add(key);
	}

	// The slot outgrew the window: the set takes over for this slot, seeded with the window's keys.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SeedDedupe(ref ManyKeyWindow<TRightKey> window, ref ValueSet<TRightKey, DefaultKeyComparer<TRightKey>> dedupe) {
		if (!dedupe.IsInitlized)
			dedupe = new ValueSet<TRightKey, DefaultKeyComparer<TRightKey>>(JoinManyFusedFill.Window * 4);
		else
			dedupe.Clear();
		for (var j = 0; j < JoinManyFusedFill.Window; j++)
			dedupe.Add(window[j]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TRightValue[] Grow(TRightValue[] buffer, int count, bool pooled) {
		var grown = pooled ? PragueArrayPool<TRightValue>.Pool.Rent(buffer.Length * 2) : new TRightValue[buffer.Length * 2];
		Array.Copy(buffer, grown, count);
		if (pooled)
			PragueArrayPool<TRightValue>.Pool.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<TRightValue>());
		return grown;
	}
}
