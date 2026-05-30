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

	internal static KafkaValueFilters<TValue> Create(IReadOnlyList<KafkaValueFilter<TValue>>? filters) {
		if (filters is null || filters.Count == 0)
			return _empty;
		var arr = new KafkaValueFilter<TValue>[filters.Count];
		for (var i = 0; i < filters.Count; i++)
			arr[i] = filters[i];
		return new KafkaValueFilters<TValue>(arr);
	}

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
