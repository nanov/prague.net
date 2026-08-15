namespace Prague.Core.Collections;

/// <summary>
///   Receives rented-slot-capacity deltas from a <see cref="PooledSet{T, TKeyComparer}" />.
///   Fired only from the set's cold paths — first table allocation, Grow and Dispose —
///   so an owner can aggregate live capacity across many sets (one per index key in a
///   list index) at zero Add/Remove/Contains cost. Callbacks arrive on the writer
///   thread of the individual set; owners aggregating across sets must use an atomic add.
/// </summary>
internal interface IPooledSetCapacityListener {
	void OnPooledSetCapacityChanged(int deltaSlots);
}
