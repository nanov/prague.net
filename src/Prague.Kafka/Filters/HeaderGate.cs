namespace Prague.Kafka.Filters;

/// <summary>
/// Outcome of the header gate — <i>why</i> it rejected, not just whether, so the consume loop can treat the
/// reasons differently. Only <see cref="MissingRequiredHeader" /> is waived for a tombstone.
/// </summary>
internal enum HeaderGate : byte {
	/// <summary>No header filter objected — dispatch the message.</summary>
	Accept,

	/// <summary>
	/// This process produced the message (<c>X-Producer-Id</c> == our instance id). Always dropped, tombstone or
	/// not: a producer must not re-consume its own writes.
	/// </summary>
	SelfProduced,

	/// <summary>
	/// A filter saw its header and rejected the value it carried. Always dropped, tombstone or not — this is how a
	/// consumer selects a sub-stream of a shared topic, and honouring a foreign tombstone would let any producer
	/// evict another stream's key.
	/// </summary>
	Rejected,

	/// <summary>
	/// A header required by <c>WithHeaderExistsFilter</c> never appeared. Dropped — <b>unless</b> the message is a
	/// tombstone, which carries no headers to satisfy the requirement with and must still remove the key.
	/// <para>
	/// This is the only reject reason that describes the message's <i>shape</i> rather than a judgement about its
	/// content, which is why it is the only one waived. The equivalence it rests on — that an incomplete
	/// requirement mask at the post-loop return can only mean "a required header never appeared" — holds because
	/// <c>KafkaHeaderExistsFilter</c> is the sole executor whose <c>RequiresHeader</c> is true. Adding another one
	/// (a <c>WithHeaderNotExistsFilter</c> builder, say) would silently start admitting tombstones that violated
	/// an explicit rule.
	/// </para>
	/// </summary>
	MissingRequiredHeader
}
