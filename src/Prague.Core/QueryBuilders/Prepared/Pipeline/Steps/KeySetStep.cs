namespace Prague.Core;

using Collections;

/// <summary>
///   Key-set (predicate) index. Seed: the keys copied under the index's lock, as the eager
///   <c>AddKeyTo</c> does. Probe: value-side — the index's predicate on the fetched value (no lock);
///   key-side <c>Contains</c> (a lock per probe) under <see cref="FrozenOptions.IndexSideProbes" />.
/// </summary>
internal sealed class KeySetStep<TKey, TValue, TArgs> : PipelineStepBase<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly CacheKeySetIndex<TKey, TValue> _index;
	private readonly bool _keySide;

	internal KeySetStep(CacheKeySetIndex<TKey, TValue> index, bool indexSideProbes) {
		_index = index;
		_keySide = indexSideProbes;
	}

	public override NarrowerKind Kind => NarrowerKind.KeySet;

	public override ProbeSide Side => _keySide ? ProbeSide.Key : ProbeSide.Value;

	public override StepActivation Bind(in TArgs args, ref StepBinding binding) => StepActivation.Active;

	public override int Signal(in StepBinding binding) => _index.Count;

	public override void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe) => _index.CopyKeysTo(ref seed);

	public override bool ProbeKey(TKey key, in StepBinding binding) => _index.Contains(key);

	public override bool ProbeValue(TKey key, TValue value, in StepBinding binding) => _index.Matches(key, value);
}
