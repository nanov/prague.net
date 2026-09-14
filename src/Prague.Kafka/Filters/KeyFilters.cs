namespace Prague.Kafka.Filters;

using System.Runtime.CompilerServices;

internal sealed class KafkaKeyFilters<TKey> {
	private static readonly KafkaKeyFilters<TKey> _empty = new(Array.Empty<KafkaKeyFilter<TKey>>());

	private readonly KafkaKeyFilter<TKey>[] _filters;

	private KafkaKeyFilters(KafkaKeyFilter<TKey>[] filters) {
		_filters = filters;
	}

	internal bool IsEmpty {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _filters.Length == 0;
	}

	/// <summary>
	///   Takes ownership of <paramref name="filters" /> — the caller must not retain or mutate it afterwards.
	///   The only caller builds the array fresh per handler, so the defensive copy would be pure waste.
	/// </summary>
	internal static KafkaKeyFilters<TKey> Create(KafkaKeyFilter<TKey>[]? filters)
		=> filters is null || filters.Length == 0 ? _empty : new KafkaKeyFilters<TKey>(filters);

	/// <summary>
	/// Evaluates all filters in registration order (AND composition). The first rejecting filter
	/// decides the outcome: a <c>treatAsDelete</c> filter yields <see cref="FilterDecision.Delete"/>,
	/// any other yields <see cref="FilterDecision.Skip"/>. Returns <see cref="FilterDecision.Accept"/>
	/// only when every filter accepts.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal FilterDecision Evaluate(TKey key) {
		foreach (var filter in _filters)
			if (!filter.ShouldProcess(key))
				return filter.TreatAsDelete ? FilterDecision.Delete : FilterDecision.Skip;
		return FilterDecision.Accept;
	}
}

internal abstract class KafkaKeyFilter<TKey> {
	internal abstract bool TreatAsDelete { get; }
	public abstract bool ShouldProcess(TKey key);
}

internal sealed class KafkaKeyPredicateFilter<TKey> : KafkaKeyFilter<TKey> {
	private readonly Func<TKey, bool> _predicate;
	private readonly bool _treatAsDelete;

	public KafkaKeyPredicateFilter(Func<TKey, bool> predicate, bool treatAsDelete) {
		_predicate = predicate;
		_treatAsDelete = treatAsDelete;
	}

	internal override bool TreatAsDelete => _treatAsDelete;

	public override bool ShouldProcess(TKey key) => _predicate(key);
}

/// <summary>
///   Key filter whose predicate reads state resolved from DI once, while the handler was built. The state is passed
///   as an argument rather than captured, so the predicate can be a <c>static</c> lambda: it then closes over nothing
///   and is cached in a static field, allocating exactly one delegate per process.
/// </summary>
internal sealed class KafkaKeyStatePredicateFilter<TState, TKey> : KafkaKeyFilter<TKey> {
	private readonly Func<TState, TKey, bool> _predicate;
	private readonly TState _state;
	private readonly bool _treatAsDelete;

	public KafkaKeyStatePredicateFilter(TState state, Func<TState, TKey, bool> predicate, bool treatAsDelete) {
		_state = state;
		_predicate = predicate;
		_treatAsDelete = treatAsDelete;
	}

	internal override bool TreatAsDelete => _treatAsDelete;

	public override bool ShouldProcess(TKey key) => _predicate(_state, key);
}
