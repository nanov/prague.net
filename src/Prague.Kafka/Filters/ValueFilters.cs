namespace Prague.Kafka.Filters;

using System.Runtime.CompilerServices;

internal sealed class KafkaValueFilters<TValue> {
	private static readonly KafkaValueFilters<TValue> _empty = new(Array.Empty<KafkaValueFilter<TValue>>());

	private readonly KafkaValueFilter<TValue>[] _filters;

	private KafkaValueFilters(KafkaValueFilter<TValue>[] filters) {
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
	internal static KafkaValueFilters<TValue> Create(KafkaValueFilter<TValue>[]? filters)
		=> filters is null || filters.Length == 0 ? _empty : new KafkaValueFilters<TValue>(filters);

	/// <summary>
	/// Evaluates all filters in registration order (AND composition). The first rejecting filter
	/// decides the outcome: a <c>treatAsDelete</c> filter yields <see cref="FilterDecision.Delete"/>,
	/// any other yields <see cref="FilterDecision.Skip"/>. Returns <see cref="FilterDecision.Accept"/>
	/// only when every filter accepts.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal FilterDecision Evaluate(TValue value) {
		foreach (var filter in _filters)
			if (!filter.ShouldProcess(value))
				return filter.TreatAsDelete ? FilterDecision.Delete : FilterDecision.Skip;
		return FilterDecision.Accept;
	}
}

internal abstract class KafkaValueFilter<TValue> {
	internal abstract bool TreatAsDelete { get; }
	public abstract bool ShouldProcess(TValue value);
}

internal sealed class KafkaValuePredicateFilter<TValue> : KafkaValueFilter<TValue> {
	private readonly Func<TValue, bool> _predicate;
	private readonly bool _treatAsDelete;

	public KafkaValuePredicateFilter(Func<TValue, bool> predicate, bool treatAsDelete) {
		_predicate = predicate;
		_treatAsDelete = treatAsDelete;
	}

	internal override bool TreatAsDelete => _treatAsDelete;

	public override bool ShouldProcess(TValue value) => _predicate(value);
}

/// <summary>
///   Value filter whose predicate reads state resolved from DI once, while the handler was built. The state is passed
///   as an argument rather than captured, so the predicate can be a <c>static</c> lambda: it then closes over nothing
///   and is cached in a static field, allocating exactly one delegate per process.
/// </summary>
internal sealed class KafkaValueStatePredicateFilter<TState, TValue> : KafkaValueFilter<TValue> {
	private readonly Func<TState, TValue, bool> _predicate;
	private readonly TState _state;
	private readonly bool _treatAsDelete;

	public KafkaValueStatePredicateFilter(TState state, Func<TState, TValue, bool> predicate, bool treatAsDelete) {
		_state = state;
		_predicate = predicate;
		_treatAsDelete = treatAsDelete;
	}

	internal override bool TreatAsDelete => _treatAsDelete;

	public override bool ShouldProcess(TValue value) => _predicate(_state, value);
}
