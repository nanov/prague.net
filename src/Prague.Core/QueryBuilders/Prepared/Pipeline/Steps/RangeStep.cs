namespace Prague.Core;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   Range-index narrowing — fixed, parameterized or optional bounds, all reduced to two
///   <see cref="RangeValue{TIndexKey}" />s per execution by the narrower's bounds function. Seed: the
///   very tree calls the eager <c>UseIndexCore</c> makes for the same bound kinds, with the same
///   excluded-bound filters, so the walk and its order are the eager ones. Probe: value-side — the
///   fetched value's index key (<see cref="CacheRangeIndex{TKey,TValue,TIndexKey}.KeyOf" />) compared to
///   the bounds, O(1) against the eager O(|window|) tree walk (the `ListRange` row). Both bounds absent
///   is an inactive step. The key-side twin is a window walk; a plan that asks for it replays (§14.1).
/// </summary>
internal sealed class RangeStep<TKey, TValue, TIndexKey, TArgs> : PipelineStepBase<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TIndexKey : IComparable<TIndexKey> {
	private readonly CacheRangeIndex<TKey, TValue, TIndexKey> _index;
	private readonly Func<TArgs, (RangeValue<TIndexKey> From, RangeValue<TIndexKey> To)> _bounds;

	internal RangeStep(CacheRangeIndex<TKey, TValue, TIndexKey> index, Func<TArgs, (RangeValue<TIndexKey> From, RangeValue<TIndexKey> To)> bounds) {
		_index = index;
		_bounds = bounds;
	}

	public override NarrowerKind Kind => NarrowerKind.Range;

	public override ProbeSide Side => ProbeSide.Value;

	public override StepActivation Bind(in TArgs args, ref StepBinding binding) {
		var (from, to) = _bounds(args);
		if (from.Type == RangeValueType.None && to.Type == RangeValueType.None)
			return StepActivation.Inactive;
		binding.Int0 = (int)from.Type;
		binding.Int1 = (int)to.Type;
		if (from.Type != RangeValueType.None)
			binding.Write(0, from.Value);
		if (to.Type != RangeValueType.None)
			binding.Write(1, to.Value);
		return StepActivation.Active;
	}

	public override void Seed(in StepBinding binding, ref SeedKeys<TKey> seed, ref ValueSet<TKey, DefaultKeyComparer<TKey>> dedupe) {
		var fromType = (RangeValueType)binding.Int0;
		var toType = (RangeValueType)binding.Int1;
		var from = fromType != RangeValueType.None ? binding.Read<TIndexKey>(0) : default!;
		var to = toType != RangeValueType.None ? binding.Read<TIndexKey>(1) : default!;
		switch (fromType, toType) {
			case (RangeValueType.ThanOrEqual, RangeValueType.None): {
				var agg = new SeedAggregators<TKey, TIndexKey>.Plain(ref seed);
				_index.GetValuesGte(from, ref agg);
				break;
			}
			case (RangeValueType.ThanOrEqual, RangeValueType.Than): {
				var agg = new SeedAggregators<TKey, TIndexKey>.SkipOne(ref seed, to);
				_index.GetValuesBetween(from, to, ref agg);
				break;
			}
			case (RangeValueType.ThanOrEqual, RangeValueType.ThanOrEqual): {
				var agg = new SeedAggregators<TKey, TIndexKey>.Plain(ref seed);
				_index.GetValuesBetween(from, to, ref agg);
				break;
			}
			case (RangeValueType.Than, RangeValueType.None): {
				var agg = new SeedAggregators<TKey, TIndexKey>.SkipOne(ref seed, from);
				_index.GetValuesGte(from, ref agg);
				break;
			}
			case (RangeValueType.Than, RangeValueType.Than): {
				var agg = new SeedAggregators<TKey, TIndexKey>.SkipTwo(ref seed, from, to);
				_index.GetValuesBetween(from, to, ref agg);
				break;
			}
			case (RangeValueType.Than, RangeValueType.ThanOrEqual): {
				var agg = new SeedAggregators<TKey, TIndexKey>.SkipOne(ref seed, from);
				_index.GetValuesBetween(from, to, ref agg);
				break;
			}
			case (RangeValueType.None, RangeValueType.ThanOrEqual): {
				var agg = new SeedAggregators<TKey, TIndexKey>.Plain(ref seed);
				_index.GetValuesLte(to, ref agg);
				break;
			}
			case (RangeValueType.None, RangeValueType.Than): {
				var agg = new SeedAggregators<TKey, TIndexKey>.SkipOne(ref seed, to);
				_index.GetValuesLte(to, ref agg);
				break;
			}
			default: throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override bool ProbeValue(TKey key, TValue value, in StepBinding binding) {
		var actual = _index.KeyOf(key, value);
		// A reference key is never null in the tree (Add would have thrown); a null here is a value the
		// index never accepted, so it is not in the window. The IsValueType guard keeps the unoptimized
		// JIT from boxing a value-type key for the null test; the optimized one folds the whole line.
		if (!typeof(TIndexKey).IsValueType && actual is null)
			return false;
		var fromType = (RangeValueType)binding.Int0;
		if (fromType != RangeValueType.None) {
			var cmp = actual.CompareTo(binding.Read<TIndexKey>(0));
			if (fromType == RangeValueType.ThanOrEqual ? cmp < 0 : cmp <= 0)
				return false;
		}

		var toType = (RangeValueType)binding.Int1;
		if (toType != RangeValueType.None) {
			var cmp = actual.CompareTo(binding.Read<TIndexKey>(1));
			if (toType == RangeValueType.ThanOrEqual ? cmp > 0 : cmp >= 0)
				return false;
		}

		return true;
	}
}
