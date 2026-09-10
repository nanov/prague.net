namespace Prague.Core;

using Collections;

/// <summary>
///   Unique-index equality (bound or parameterized). Seed: 0 or 1 keys. Probe: key-side on purpose —
///   the index maps two keys to one entity while an update is in flight, and the eager step reads the
///   index too, so this keeps eager's exact staleness (design §14.1).
/// </summary>
internal sealed class UniqueEqStep<TKey, TValue, TIndexKey, TArgs> : PipelineStepBase<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueIndex<TKey, TValue, TIndexKey> _index;
	private readonly TIndexKey _value;
	private readonly Func<TArgs, TIndexKey>? _selector;

	internal UniqueEqStep(CacheKeyValueIndex<TKey, TValue, TIndexKey> index, TIndexKey value, Func<TArgs, TIndexKey>? selector) {
		_index = index;
		_value = value;
		_selector = selector;
	}

	public override NarrowerKind Kind => NarrowerKind.UniqueEq;

	public override ProbeSide Side => ProbeSide.Key;

	public override StepActivation Bind(in TArgs args, ref StepBinding binding) {
		if (_selector is not null)
			binding.Write(0, _selector(args));
		return StepActivation.Active;
	}

	private TIndexKey Key(in StepBinding binding) => _selector is null ? _value : binding.Read<TIndexKey>(0);

	public override int Signal(in StepBinding binding) => _index.TryGetValue(Key(in binding), out _) ? 1 : 0;

	public override void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe) {
		if (_index.TryGetValue(Key(in binding), out var entityKey))
			seed.Add(entityKey);
	}

	public override bool ProbeKey(TKey key, in StepBinding binding)
		=> _index.TryGetValue(Key(in binding), out var entityKey) && entityKey.Equals(key);
}

/// <summary>
///   Unique-index membership in a span of values (bound or parameterized). An empty span empties the
///   query (the eager <c>Clear()</c>). Seed: the span's keys in span order, deduplicated when there is
///   more than one (an in-flight update can map two index keys to one entity). Probe: key-side, one
///   index read per span value until one maps to the candidate.
/// </summary>
internal sealed class UniqueInStep<TKey, TValue, TIndexKey, TArgs> : PipelineStepBase<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueIndex<TKey, TValue, TIndexKey> _index;
	private readonly ReadOnlyMemory<TIndexKey> _values;
	private readonly Func<TArgs, ReadOnlyMemory<TIndexKey>>? _selector;

	internal UniqueInStep(CacheKeyValueIndex<TKey, TValue, TIndexKey> index, ReadOnlyMemory<TIndexKey> values, Func<TArgs, ReadOnlyMemory<TIndexKey>>? selector) {
		_index = index;
		_values = values;
		_selector = selector;
	}

	public override NarrowerKind Kind => NarrowerKind.UniqueIn;

	public override ProbeSide Side => ProbeSide.Key;

	public override StepActivation Bind(in TArgs args, ref StepBinding binding) {
		var values = _selector is null ? _values : _selector(args);
		if (_selector is not null)
			BindingMemory.Write(ref binding, values);
		return values.Length == 0 ? StepActivation.Empty : StepActivation.Active;
	}

	private ReadOnlySpan<TIndexKey> Values(in StepBinding binding) => _selector is null ? _values.Span : BindingMemory.Read<TIndexKey>(in binding);

	// The span length: an upper bound (a missing value maps to no row) that costs no probe.
	public override int Signal(in StepBinding binding) => Values(in binding).Length;

	public override void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe) {
		var values = Values(in binding);
		if (values.Length == 1) {
			if (_index.TryGetValue(values[0], out var only))
				seed.Add(only);
			return;
		}

		if (!dedupe.IsInitlized)
			dedupe = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		for (var i = 0; i < values.Length; i++)
			if (_index.TryGetValue(values[i], out var entityKey) && dedupe.Add(entityKey))
				seed.Add(entityKey);
	}

	public override bool ProbeKey(TKey key, in StepBinding binding) {
		var values = Values(in binding);
		for (var i = 0; i < values.Length; i++)
			if (_index.TryGetValue(values[i], out var entityKey) && entityKey.Equals(key))
				return true;
		return false;
	}
}
