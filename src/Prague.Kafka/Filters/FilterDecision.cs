namespace Prague.Kafka.Filters;

/// <summary>
/// Outcome of evaluating a composed filter chain (key or value) against a message.
/// </summary>
internal enum FilterDecision : byte {
	/// <summary>All filters accepted — process the message normally.</summary>
	Accept,
	/// <summary>A filter rejected the message — drop it without touching the cache.</summary>
	Skip,
	/// <summary>A <c>treatAsDelete</c> filter rejected the message — treat it as a tombstone for the key.</summary>
	Delete
}
