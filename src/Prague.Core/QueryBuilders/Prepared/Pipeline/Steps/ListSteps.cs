namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   List-index equality (bound or parameterized). Seed: the bucket walked once under its gate pin.
///   Probe: value-side through the index's scalar <c>KeySelector</c> on the fetched value (a field read
///   and a compare); key-side <c>bucket.Contains</c> for collection-backed indexes, which have no scalar
///   selector, and under <see cref="FrozenOptions.IndexSideProbes" /> (the eager staleness window). The
///   bucket is looked up once per execution at bind, as the eager step looks it up once; its live count
///   is the step's signal and its slot order the step's seed order (the small-probe seed, design §3.4).
/// </summary>
internal sealed class ListEqStep<TKey, TValue, TIndexKey, TArgs> : PipelineStepBase<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueListIndex<TKey, TValue, TIndexKey> _index;
	private readonly TIndexKey _value;
	private readonly Func<TArgs, TIndexKey>? _selector;
	private readonly bool _keySide;

	internal ListEqStep(CacheKeyValueListIndex<TKey, TValue, TIndexKey> index, TIndexKey value, Func<TArgs, TIndexKey>? selector, bool indexSideProbes) {
		_index = index;
		_value = value;
		_selector = selector;
		_keySide = indexSideProbes || !index.HasKeySelector;
	}

	public override NarrowerKind Kind => NarrowerKind.ListEq;

	public override ProbeSide Side => _keySide ? ProbeSide.Key : ProbeSide.Value;

	public override bool SlotAddressable => true;

	public override StepActivation Bind(in TArgs args, ref StepBinding binding) {
		var key = _selector is null ? _value : _selector(args);
		if (_selector is not null)
			binding.Write(0, key);
		binding.Ref1 = _index.TryGetBucket(key, out var bucket) ? bucket : null;
		return StepActivation.Active;
	}

	private TIndexKey Key(in StepBinding binding) => _selector is null ? _value : binding.Read<TIndexKey>(0);

	private static PooledSet<TKey, DefaultKeyComparer<TKey>>? Bucket(in StepBinding binding) => Unsafe.As<PooledSet<TKey, DefaultKeyComparer<TKey>>?>(binding.Ref1);

	public override int Signal(in StepBinding binding) => Bucket(in binding)?.Count ?? 0;

	public override void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe) {
		if (Bucket(in binding) is { } bucket)
			SeedBucket(bucket, ref seed, ref dedupe, false);
	}

	public override bool TryGetSlot(TKey key, in StepBinding binding, out int slot) {
		if (Bucket(in binding) is { } bucket)
			return bucket.TryGetSlot(key, out slot);
		slot = -1;
		return false;
	}

	public override bool ProbeKey(TKey key, in StepBinding binding)
		=> Bucket(in binding) is { } bucket && bucket.Contains(key);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override bool ProbeValue(TKey key, TValue value, in StepBinding binding)
		=> EqualityComparer<TIndexKey>.Default.Equals(_index.KeySelector(key, value), Key(in binding));
}

/// <summary>
///   List-index membership in a span of index keys: bound, parameterized, or projected from a bound
///   span of foreign values through a key selector (the eager <c>UseIndex(list, values, keySelector)</c>).
///   An empty span is an inactive step (the eager <c>return</c> before <c>_first = false</c>). Seed: the
///   buckets in span order, deduplicated through a set exactly as the eager <c>UnionWith</c> chain is.
///   Probe: value-side — the fetched value's key compared against the span; key-side <c>Contains</c>
///   over each bucket for collection-backed indexes and under <see cref="FrozenOptions.IndexSideProbes" />.
///   The projected form projects once per execution into a rented buffer, returned by <see cref="Release" />.
/// </summary>
internal sealed class ListInStep<TKey, TValue, TIndexKey, TOtherValue, TArgs> : PipelineStepBase<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : notnull {
	private readonly CacheKeyValueListIndex<TKey, TValue, TIndexKey> _index;
	private readonly bool _keySide;
	private ReadOnlyMemory<TIndexKey> _values;
	private Func<TArgs, ReadOnlyMemory<TIndexKey>>? _selector;
	private ReadOnlyMemory<TOtherValue> _otherValues;
	private Func<TOtherValue, TIndexKey>? _keySelector;

	private ListInStep(CacheKeyValueListIndex<TKey, TValue, TIndexKey> index, bool indexSideProbes) {
		_index = index;
		_keySide = indexSideProbes || !index.HasKeySelector;
	}

	/// <summary>The bound (<paramref name="selector" /> null) or parameterized span of index keys.</summary>
	internal static ListInStep<TKey, TValue, TIndexKey, TOtherValue, TArgs> Keyed(CacheKeyValueListIndex<TKey, TValue, TIndexKey> index, ReadOnlyMemory<TIndexKey> values,
		Func<TArgs, ReadOnlyMemory<TIndexKey>>? selector, bool indexSideProbes)
		=> new(index, indexSideProbes) { _values = values, _selector = selector };

	/// <summary>The bound span of foreign values, each projected to an index key per execution.</summary>
	internal static ListInStep<TKey, TValue, TIndexKey, TOtherValue, TArgs> Projected(CacheKeyValueListIndex<TKey, TValue, TIndexKey> index, ReadOnlyMemory<TOtherValue> otherValues,
		Func<TOtherValue, TIndexKey> keySelector, bool indexSideProbes)
		=> new(index, indexSideProbes) { _otherValues = otherValues, _keySelector = keySelector };

	public override NarrowerKind Kind => _keySelector is null ? NarrowerKind.ListIn : NarrowerKind.ListInProjected;

	public override ProbeSide Side => _keySide ? ProbeSide.Key : ProbeSide.Value;

	public override bool NeedsRelease => _keySelector is not null;

	public override StepActivation Bind(in TArgs args, ref StepBinding binding) {
		if (_keySelector is not null) {
			var others = _otherValues.Span;
			if (others.Length == 0)
				return StepActivation.Inactive;
			var keys = PragueArrayPool<TIndexKey>.Pool.Rent(others.Length);
			binding.Ref0 = keys;
			binding.Int0 = 0;
			binding.Int1 = others.Length;
			for (var i = 0; i < others.Length; i++)
				keys[i] = _keySelector(others[i]);
			return StepActivation.Active;
		}

		var values = _selector is null ? _values : _selector(args);
		if (_selector is not null)
			BindingMemory.Write(ref binding, values);
		return values.Length == 0 ? StepActivation.Inactive : StepActivation.Active;
	}

	private ReadOnlySpan<TIndexKey> Keys(in StepBinding binding) => _selector is null && _keySelector is null ? _values.Span : BindingMemory.Read<TIndexKey>(in binding);

	// The sum of the live bucket counts: an upper bound when buckets overlap (collection-backed indexes).
	public override int Signal(in StepBinding binding) {
		var keys = Keys(in binding);
		var total = 0L;
		for (var i = 0; i < keys.Length; i++)
			total += _index.TryGetCount(keys[i]);
		return (int)Math.Min(total, int.MaxValue);
	}

	public override void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe) {
		var keys = Keys(in binding);
		var dedupeKeys = keys.Length > 1;
		for (var i = 0; i < keys.Length; i++)
			if (_index.TryGetBucket(keys[i], out var bucket))
				SeedBucket(bucket, ref seed, ref dedupe, dedupeKeys);
	}

	public override bool ProbeKey(TKey key, in StepBinding binding) {
		var keys = Keys(in binding);
		for (var i = 0; i < keys.Length; i++)
			if (_index.TryGetBucket(keys[i], out var bucket) && bucket.Contains(key))
				return true;
		return false;
	}

	public override bool ProbeValue(TKey key, TValue value, in StepBinding binding) {
		var actual = _index.KeySelector(key, value);
		var keys = Keys(in binding);
		for (var i = 0; i < keys.Length; i++)
			if (EqualityComparer<TIndexKey>.Default.Equals(actual, keys[i]))
				return true;
		return false;
	}

	public override void Release(ref StepBinding binding) {
		if (_keySelector is null || binding.Ref0 is not TIndexKey[] keys)
			return;
		binding.Ref0 = null;
		PragueArrayPool<TIndexKey>.Pool.Return(keys, RuntimeHelpers.IsReferenceOrContainsReferences<TIndexKey>());
	}
}
