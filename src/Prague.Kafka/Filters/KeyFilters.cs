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

	internal static KafkaKeyFilters<TKey> Create(IReadOnlyList<KafkaKeyFilter<TKey>>? filters) {
		if (filters is null || filters.Count == 0)
			return _empty;
		var arr = new KafkaKeyFilter<TKey>[filters.Count];
		for (var i = 0; i < filters.Count; i++)
			arr[i] = filters[i];
		return new KafkaKeyFilters<TKey>(arr);
	}

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
