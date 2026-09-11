namespace Prague.Core;

using Collections;

/// <summary>
///   <c>updatedAfter</c> / <c>updatedAfter .. updatedUntilInclusive</c> against a
///   <see cref="LastUpdatedIndex{TKey}" /> (the raw index or a global adapter's), bounds fixed at build
///   (already converted to unix ms, as the eager extensions convert) or selected per execution. Seed:
///   the eager tree walk — <c>(after, +∞)</c>, or <c>[after, until]</c> minus the keys stamped exactly
///   <c>after</c>. Probe: key-side, one lookup of the key's own timestamp in the index's store — the
///   same source the eager walk reads, O(1) against O(rows updated since <c>after</c>).
/// </summary>
internal sealed class LastUpdatedStep<TKey, TValue, TArgs> : PipelineStepBase<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TArgs : struct {
	private readonly LastUpdatedIndex<TKey> _index;
	private readonly long _after;
	private readonly long _until;
	private readonly Func<TArgs, long>? _afterSelector;
	private readonly Func<TArgs, long>? _untilSelector;
	private readonly bool _between;

	internal LastUpdatedStep(LastUpdatedIndex<TKey> index, long after, Func<TArgs, long>? afterSelector) {
		_index = index;
		_after = after;
		_afterSelector = afterSelector;
	}

	internal LastUpdatedStep(LastUpdatedIndex<TKey> index, long after, long until, Func<TArgs, long>? afterSelector, Func<TArgs, long>? untilSelector) {
		_index = index;
		_after = after;
		_until = until;
		_afterSelector = afterSelector;
		_untilSelector = untilSelector;
		_between = true;
	}

	public override NarrowerKind Kind => _between ? NarrowerKind.LastUpdatedBetween : NarrowerKind.LastUpdatedAfter;

	public override ProbeSide Side => ProbeSide.Key;

	public override bool ExactSignal => false;

	public override StepActivation Bind(in TArgs args, ref StepBinding binding) {
		binding.Bits[0] = _afterSelector is null ? _after : _afterSelector(args);
		binding.Bits[1] = _untilSelector is null ? _until : _untilSelector(args);
		return StepActivation.Active;
	}

	public override int Signal(in StepBinding binding)
		=> _between ? _index.EstimateCount(binding.Bits[0], binding.Bits[1]) : _index.EstimateCount(binding.Bits[0]);

	public override void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe) {
		var after = binding.Bits[0];
		if (!_between) {
			var agg = new SeedAggregators<TKey, long>.Plain(ref seed);
			_index.GetValuesGt(after, ref agg);
			return;
		}

		var skipping = new SeedAggregators<TKey, long>.SkipOne(ref seed, after);
		_index.GetValuesBetween(after, binding.Bits[1], ref skipping);
	}

	public override bool ProbeKey(TKey key, in StepBinding binding)
		=> _index.TryGetLastUpdated(key, out var timestampMs) && timestampMs > binding.Bits[0] && (!_between || timestampMs <= binding.Bits[1]);
}
