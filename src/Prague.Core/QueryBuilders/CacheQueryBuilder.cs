using Prague.Core.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Prague.Core;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TypeSystem;
using QueryBuilders;
using Utils;

public struct CacheQueryBuilderCoreCombined<TKey, TValue>
	: IDisposable, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>,
	  IOrCapable<TKey, TValue, CacheQueryBuilderCoreCombined<TKey, TValue>>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TKey : IEquatable<TKey> {

	internal readonly InMemoryDataCache<TKey, TValue> _dataCache;
	private Predicate<TValue>? _filter;
	private bool _disposed;
	private bool _isIntersecter;
	internal bool _first;
	internal ValueSet<TKey, DefaultKeyComparer<TKey>> Candidates;
	internal RefHolder<ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter> _incrementalIntersecter;

	// InMemoryDataCache<TKey, TValue> ICandidatesExecutor<TKey, TValue>.DataCache => _dataCache;

	ref ValueSet<TKey, DefaultKeyComparer<TKey>> ICandidatesExecutor<TKey, TValue>.Candidates {
		[UnscopedRef] get => ref Candidates;
	}

	void ICandidatesExecutor<TKey, TValue>.ExecuteBase<TContainer>(ref TContainer c) => Execute(ref c);
	int ICandidatesExecutor<TKey, TValue>.CountBase() => Count();

	public CacheQueryBuilderCoreCombined(InMemoryDataCache<TKey, TValue> dataCache) {
		_dataCache = dataCache;
		_filter = null;
		_disposed = false;
		_first = true;
		_isIntersecter = false;
	}

	internal CacheQueryBuilderCoreCombined(
		InMemoryDataCache<TKey, TValue> dataCache, ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter incrementalIntersecter) {
		_dataCache = dataCache;
		_filter = null;
		_disposed = false;
		_first = true;
		_incrementalIntersecter = new RefHolder<ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter>(ref incrementalIntersecter);
		_isIntersecter = true;
	}

	#region IOrCapable<CacheQueryBuilderCoreCombined<TKey, TValue>>

	[UnscopedRef]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter CreateIntersecter(Span<int> buffer)
		=> _isIntersecter
			? _incrementalIntersecter.Value.CreateChild(buffer)
			: new ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter(ref Candidates, buffer);

	public void OrWith<TCache, TResolverChain, TResult, TBranch>(in NarrowOnlyQuery<TCache> narrowOnly, TResolverChain resolverChain,  in TBranch b1, in TBranch b2)
		where TResolverChain : struct, IResolvers
		where TBranch : struct, IOrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>,  CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue,
			TResolverChain, TResult>> {

		// Auto-seed Candidates from the cache's full key set when we're the top-level
		// outer (not in intersecter mode) and the user is calling .Or(...) before any prior
		// narrowing. Branches mark bits on _self; without this seed, _self is empty and
		// branches' lookups land on zero slots → entire Or becomes a no-op.
		if (!_isIntersecter && _first) {
			if (!Candidates.IsInitlized) {
				var initContainer = new InitContainer(ref Candidates);
				_dataCache.EnumerateAllValuesInit(ref initContainer, _filter);
			}
			_first = false;
		}

		Span<int> twoBuffers = stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold * 2];
		var buffer1 = twoBuffers[..ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold];
		var buffer2 = twoBuffers[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold..];
		var intersector1 = CreateIntersecter(buffer1);
		var executor = new CacheQueryBuilderCoreCombined<TKey, TValue>(_dataCache, ref intersector1);
		var b1Builder = new CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>,  CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, TResolverChain, TResult>(
			narrowOnly, executor, resolverChain, 0);

		var intersector2 = CreateIntersecter(buffer2);
		var executor2 = new CacheQueryBuilderCoreCombined<TKey, TValue>(_dataCache, ref intersector2);
		var b2Builder = new CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>,  CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, TResolverChain, TResult>(
			narrowOnly, executor2, resolverChain, 0);

		try {
			b1Builder = b1.Apply(b1Builder);
			b2Builder = b2.Apply(b2Builder);

			intersector1.Union(ref intersector2);
			if (_isIntersecter)
				_incrementalIntersecter.Value.Union(intersector1.Bits);
		} finally {
			intersector1.Dispose(!_isIntersecter && ( !b1Builder._leftQuery._first || !b2Builder._leftQuery._first));
			intersector2.Dispose(false);
		}

	}
	#endregion

	#region UseIndex methods (internal — exposed via extension methods constrained on IBaseFilterable)

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		TIndexKey value) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) return;
			if (_first)
				index.IntersectValue(value, ref _incrementalIntersecter.Value);
			else
				index.RetainOnly(value, ref _incrementalIntersecter.Value);
			_first = false;
			return;
		}

		if (!Candidates.IsInitlized) Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		index.IntersectValue(value, ref Candidates, _first);
		_first = false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		List<TIndexKey> values) =>
		UseIndexInternal(index, CollectionsMarshal.AsSpan(values));

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TIndexKey> values) => UseIndexInternal(index, values);

	private void UseIndexInternal<TIndexKey>(CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TIndexKey> values) where TIndexKey : notnull {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) return;
			if (values.Length == 0) {
				intersector.Clear();
			} else if (_first) {
				if (values.Length == 1) index.IntersectValue(values[0], ref intersector);
				else index.IntersectValues(values, ref intersector);
			} else if (values.Length == 1) {
				index.RetainOnly(values[0], ref intersector);
			} else {
				// Multi-value prune: mark union into a sub-bitmap, AND into main.
				Span<int> subBuf = stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold];
				var sub = CreateIntersecter(subBuf);
				try {
					index.IntersectValues(values, ref sub);
					intersector.Intersect(sub.Bits);
				} finally {
					sub.Dispose(false);
				}
			}
			_first = false;
			return;
		}


		if (!Candidates.IsInitlized) Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>();

		if (values.Length == 0) {
			if (!_first)
				Candidates.Clear();
			_first = false;
			return;
		}

		if (values.Length == 1)
			index.IntersectValue(values[0], ref Candidates, _first);
		else
			index.IntersectValues(values, ref Candidates, _first);

		_first = false;
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		TIndexKey value) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) return;
			if (_first)
				index.IntersectValue(value, ref _incrementalIntersecter.Value);
			else
				index.RetainOnly(value, ref _incrementalIntersecter.Value);
			_first = false;
			return;
		}


		if (!Candidates.IsInitlized) Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		index.IntersectValues(value, ref Candidates, _first);
		_first = false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		List<TIndexKey> values) =>
		UseIndexInternal(index, CollectionsMarshal.AsSpan(values));


	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey, TOtherValue>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TOtherValue> values,
		Func<TOtherValue, TIndexKey> keySelector) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) {
				_first = false;
				return;
			}
			if (values.Length == 0)
				intersector.Clear();
			else if (values.Length == 1)
				index.IntersectValue(keySelector(values[0]), ref intersector);
			else
				index.IntersectValues(values, keySelector, ref intersector);
			_first = false;
			return;
		}

		if (values.Length == 0)
			return;

		if (!Candidates.IsInitlized) Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		if (values.Length == 1)
			index.IntersectValues(keySelector(values[0]), ref Candidates, _first);
		else
			index.IntersectValues(values, keySelector, ref Candidates, _first);

		_first = false;
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TIndexKey> values)
		=> UseIndexInternal(index, values);

	private void UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TIndexKey> values) where TIndexKey : notnull {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) return;
			if (values.Length == 0) {
				intersector.Clear();
			} else if (_first) {
				if (values.Length == 1) index.IntersectValue(values[0], ref intersector);
				else index.IntersectValues(values, ref intersector);
			} else if (values.Length == 1) {
				index.RetainOnly(values[0], ref intersector);
			} else {
				// Multi-value prune: mark union into a sub-bitmap, AND into main.
				Span<int> subBuf = stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold];
				var sub = CreateIntersecter(subBuf);
				try {
					index.IntersectValues(values, ref sub);
					intersector.Intersect(sub.Bits);
				} finally {
					sub.Dispose(false);
				}
			}
			_first = false;
			return;
		}

		if (values.Length == 0)
			return;

		if (!Candidates.IsInitlized) Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		if (values.Length == 1)
			index.IntersectValues(values[0], ref Candidates, _first);
		else
			index.IntersectValues(values, ref Candidates, _first);

		_first = false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter) =>
		UseIndexInternal(lastUpdatedIndex.Index, new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds(), out _);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter) =>
		UseIndexInternal(lastUpdatedIndex.Index, updatedAfter.ToUnixTimeMilliseconds(), out _);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		long updatedAfter) =>
		UseIndexInternal(lastUpdatedIndex.Index, updatedAfter, out _);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		out long max) =>
		UseIndexInternal(lastUpdatedIndex.Index, updatedAfter, out max);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter) =>
		UseIndexInternal(lastUpdatedIndex, new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds(), out _);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter) =>
		UseIndexInternal(lastUpdatedIndex, updatedAfter.ToUnixTimeMilliseconds(), out _);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter) =>
		UseIndexInternal(lastUpdatedIndex, updatedAfter, out _);

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter,
		out long max) =>
		UseIndexInternal(lastUpdatedIndex, new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds(), out max);

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter,
		out long max) =>
		UseIndexInternal(lastUpdatedIndex, updatedAfter.ToUnixTimeMilliseconds(), out max);

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		out long max) => UseIndexInternal(lastUpdatedIndex, updatedAfter, out max);

	private void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		out long max) {
		max = 0;
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) {
				_first = false;
				return;
			}
			var vm = new ValueMark<long>(ref intersector, ref max);
			lastUpdatedIndex.GetValuesGt(updatedAfter, ref vm);
			_first = false;
			return;
		}

		if (!Candidates.IsInitlized) Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		if (_first) {
			var va = new ValueAdd<long>(ref Candidates, ref max);
			lastUpdatedIndex.GetValuesGt(updatedAfter, ref va);
			va.Dispose();
		} else {
			Span<int> buffer = stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold];
			var va = new ValueIntersect<long>(ref Candidates, buffer, ref max);
			try {
				lastUpdatedIndex.GetValuesGt(updatedAfter, ref va);
			} finally {
				va.Dispose();
			}
		}

		_first = false;
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter,
		DateTime updatedUntilInclusive) {
		UseIndexInternal(lastUpdatedIndex.Index, new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds(),
			new DateTimeOffset(updatedUntilInclusive).ToUnixTimeMilliseconds());
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter,
		DateTimeOffset updatedUntilInclusive) {
		UseIndexInternal(lastUpdatedIndex.Index, updatedAfter.ToUnixTimeMilliseconds(),
			updatedUntilInclusive.ToUnixTimeMilliseconds());
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		long updatedUntilInclusive) =>
		UseIndexInternal(lastUpdatedIndex.Index, updatedAfter, updatedUntilInclusive);

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter,
		DateTime updatedUntilInclusive) {
		UseIndexInternal(lastUpdatedIndex, new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds(),
			new DateTimeOffset(updatedUntilInclusive).ToUnixTimeMilliseconds());
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter,
		DateTimeOffset updatedUntilInclusive) {
		UseIndexInternal(lastUpdatedIndex, updatedAfter.ToUnixTimeMilliseconds(),
			updatedUntilInclusive.ToUnixTimeMilliseconds());
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		long updatedUntilInclusive) => UseIndexInternal(lastUpdatedIndex, updatedAfter, updatedUntilInclusive);

	private void UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		long updatedUntilInclusive) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) {
				_first = false;
				return;
			}
			var vm = IndexSkip<long>.Create(updatedAfter, new ValueMark<long>(ref intersector));
			lastUpdatedIndex.GetValuesBetween(updatedAfter, updatedUntilInclusive, ref vm);
			_first = false;
			return;
		}

		if (!Candidates.IsInitlized) Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>();

		if (_first) {
			var va = IndexSkip<long>.Create(updatedAfter, new ValueAdd<long>(ref Candidates));
			lastUpdatedIndex.GetValuesBetween(updatedAfter, updatedUntilInclusive, ref va);
			va.Dispose();
		} else {
			Span<int> buffer = stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold];
			var va = IndexSkip<long>.Create(updatedAfter, new ValueIntersect<long>(ref Candidates, buffer));
			try {
				lastUpdatedIndex.GetValuesBetween(updatedAfter, updatedUntilInclusive, ref va);
			} finally {
				va.Dispose();
			}
		}

		_first = false;
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey, TQueryBuilder>(
		CacheRangeIndex<TKey, TValue, TIndexKey> index, Func<RangeQueryBuilder<TIndexKey>, TQueryBuilder> rangeBuilder)
		 => UseIndexCore(index, rangeBuilder(new RangeQueryBuilder<TIndexKey>()));

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey, TQueryBuilder, TArgs>(
		CacheRangeIndex<TKey, TValue, TIndexKey> index,
		Func<RangeQueryBuilder<TIndexKey>, TArgs, TQueryBuilder> rangeBuilder, TArgs args)
		 => UseIndexCore(index, rangeBuilder(new RangeQueryBuilder<TIndexKey>(), args));

	private void UseIndexCore<TIndexKey, TQueryBuilder>(
		CacheRangeIndex<TKey, TValue, TIndexKey> index, TQueryBuilder f)
		where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey>
		where TIndexKey : IComparable<TIndexKey> {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) {
				_first = false;
				return;
			}
			switch (f) {
				case ((RangeValueType.ThanOrEqual, var gte), (RangeValueType.None, _)): {
					var vm = new ValueMark<TIndexKey>(ref intersector);
					index.GetValuesGte(gte, ref vm);
					break;
				}
				case ((RangeValueType.ThanOrEqual, var gte), (RangeValueType.Than, var lt)): {
					var vm = IndexSkip<TIndexKey>.Create(lt, new ValueMark<TIndexKey>(ref intersector));
					index.GetValuesBetween(gte, lt, ref vm);
					break;
				}
				case ((RangeValueType.ThanOrEqual, var gte), (RangeValueType.ThanOrEqual, var lte)): {
					var vm = new ValueMark<TIndexKey>(ref intersector);
					index.GetValuesBetween(gte, lte, ref vm);
					break;
				}
				case ((RangeValueType.Than, var gt), (RangeValueType.None, _)): {
					var vm = IndexSkip<TIndexKey>.Create(gt, new ValueMark<TIndexKey>(ref intersector));
					index.GetValuesGte(gt, ref vm);
					break;
				}
				case ((RangeValueType.Than, var gt), (RangeValueType.Than, var lt)): {
					var vm = IndexSkip<TIndexKey>.Create(gt, lt, new ValueMark<TIndexKey>(ref intersector));
					index.GetValuesBetween(gt, lt, ref vm);
					break;
				}
				case ((RangeValueType.Than, var gt), (RangeValueType.ThanOrEqual, var lte)): {
					var vm = IndexSkip<TIndexKey>.Create(gt, new ValueMark<TIndexKey>(ref intersector));
					index.GetValuesBetween(gt, lte, ref vm);
					break;
				}
				case ((RangeValueType.None, _), (RangeValueType.ThanOrEqual, var lte)): {
					var vm = new ValueMark<TIndexKey>(ref intersector);
					index.GetValuesLte(lte, ref vm);
					break;
				}
				case ((RangeValueType.None, _), (RangeValueType.Than, var lt)): {
					var vm = IndexSkip<TIndexKey>.Create(lt, new ValueMark<TIndexKey>(ref intersector));
					index.GetValuesLte(lt, ref vm);
					break;
				}
				default: throw new UnreachableException();
			}
			_first = false;
			return;
		}

		if (!Candidates.IsInitlized) Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		// Stack buffer for the non-first (intersect) branches' IncrementalIntersecter — avoids an
		// ArrayPool rent for candidate sets within the threshold. At most one branch runs per call.
		Span<int> buffer = stackalloc int[ValueSet<TKey, DefaultKeyComparer<TKey>>.StackAllocThreshold];
		switch (f) {
			case ((RangeValueType.ThanOrEqual, var gte), (RangeValueType.None, _)):
				if (_first) {
					var va = new ValueAdd<TIndexKey>(ref Candidates);
					index.GetValuesGte(gte, ref va);
				} else {
					var va = new ValueIntersect<TIndexKey>(ref Candidates, buffer);
					try {
						index.GetValuesGte(gte, ref va);
					} finally {
						va.Dispose();
					}
				}

				break;
			case ((RangeValueType.ThanOrEqual, var gte), (RangeValueType.Than, var lt)):
				if (_first) {
					var va = IndexSkip<TIndexKey>.Create(lt, new ValueAdd<TIndexKey>(ref Candidates));
					index.GetValuesBetween(gte, lt, ref va);
				} else {
					var va = IndexSkip<TIndexKey>.Create(lt, new ValueIntersect<TIndexKey>(ref Candidates, buffer));
					try {
						index.GetValuesBetween(gte, lt, ref va);
					} finally {
						va.Dispose();
					}
				}

				break;
			case ((RangeValueType.ThanOrEqual, var gte), (RangeValueType.ThanOrEqual, var lte)):
				if (_first) {
					var va = new ValueAdd<TIndexKey>(ref Candidates);
					index.GetValuesBetween(gte, lte, ref va);
				} else {
					var va = new ValueIntersect<TIndexKey>(ref Candidates, buffer);
					try {
						index.GetValuesBetween(gte, lte, ref va);
					} finally {
						va.Dispose();
					}
				}

				break;
			case ((RangeValueType.Than, var gt), (RangeValueType.None, _)):
				if (_first) {
					var va = IndexSkip<TIndexKey>.Create(gt, new ValueAdd<TIndexKey>(ref Candidates));
					index.GetValuesGte(gt, ref va);
					va.Dispose();
				} else {
					var va = IndexSkip<TIndexKey>.Create(gt, new ValueIntersect<TIndexKey>(ref Candidates, buffer));
					try {
						index.GetValuesGte(gt, ref va);
					} finally {
						va.Dispose();
					}
				}

				break;
			case ((RangeValueType.Than, var gt), (RangeValueType.Than, var lt)):
				if (_first) {
					var va = IndexSkip<TIndexKey>.Create(gt, lt, new ValueAdd<TIndexKey>(ref Candidates));
					index.GetValuesBetween(gt, lt, ref va);
				} else {
					var va = IndexSkip<TIndexKey>.Create(gt, lt, new ValueIntersect<TIndexKey>(ref Candidates, buffer));
					try {
						index.GetValuesBetween(gt, lt, ref va);
					} finally {
						va.Dispose();
					}
				}

				break;
			case ((RangeValueType.Than, var gt), (RangeValueType.ThanOrEqual, var lte)):
				if (_first) {
					var va = IndexSkip<TIndexKey>.Create(gt, new ValueAdd<TIndexKey>(ref Candidates));
					index.GetValuesBetween(gt, lte, ref va);
				} else {
					var va = IndexSkip<TIndexKey>.Create(gt, new ValueIntersect<TIndexKey>(ref Candidates, buffer));
					try {
						index.GetValuesBetween(gt, lte, ref va);
					} finally {
						va.Dispose();
					}
				}

				break;
			case ((RangeValueType.None, _), (RangeValueType.ThanOrEqual, var lte)):
				if (_first) {
					var va = new ValueAdd<TIndexKey>(ref Candidates);
					index.GetValuesLte(lte, ref va);
				} else {
					var va = new ValueIntersect<TIndexKey>(ref Candidates, buffer);
					try {
						index.GetValuesLte(lte, ref va);
					} finally {
						va.Dispose();
					}
				}

				break;
			case ((RangeValueType.None, _), (RangeValueType.Than, var lt)):
				if (_first) {
					var va = IndexSkip<TIndexKey>.Create(lt, new ValueAdd<TIndexKey>(ref Candidates));
					index.GetValuesLte(lt, ref va);
				} else {
					var va = IndexSkip<TIndexKey>.Create(lt, new ValueIntersect<TIndexKey>(ref Candidates, buffer));
					try {
						index.GetValuesLte(lt, ref va);
					} finally {
						va.Dispose();
					}
				}

				break;
			default: throw new UnreachableException();
		}

		_first = false;
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(CacheKeySetIndex<TKey, TValue> index) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) {
				_first = false;
				return;
			}
			index.IntersectKeysWith(ref intersector);
			_first = false;
			return;
		}

		if (!Candidates.IsInitlized) {
			Candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>();
		}

		if (_first) {
			index.AddKeyTo(ref Candidates);
		} else {
			index.IntersectKeysWith(ref Candidates);
		}

		_first = false;
	}

	#endregion

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.WhereInternal(Predicate<TValue> predicate) {
		var currentFilter = _filter;
		_filter = currentFilter == null ? predicate : v => currentFilter(v) && predicate(v);
	}

	internal int Count() {
		if (_disposed)
			throw new ObjectDisposedException(nameof(CacheQueryBuilderCoreCombined<TKey, TValue>));

		try {
			if (!Candidates.IsInitlized)
				return _filter == null
					? _dataCache.Count
					: _dataCache.CountAllValues(_filter);

			return Candidates.Count == 0
				? 0
				: _dataCache.TryCount(ref Candidates, _filter);
		} finally {
			Dispose();
		}
	}


	internal void Execute<TContainer>(ref TContainer container)
		where TContainer : struct, IResultContainerInitializer<TKey, TValue>, allows ref struct {
		if (_disposed)
			throw new ObjectDisposedException(
				nameof(CacheQueryBuilderCoreCombined<TKey, TValue>));

		try {
			if (!Candidates.IsInitlized) {
				_dataCache.EnumerateAllValuesInit(ref container, _filter);
				return;
			}

			if (Candidates.Count == 0)
				return;

			container.Init(Candidates.Count);

			var actualCount = _dataCache.TryGet(ref container, ref Candidates, _filter);
			container.Seal(actualCount);
		} finally {
			Dispose();
		}
	}


	public void Dispose() {
		if (_disposed) return;
		Candidates.Dispose();
		_disposed = true;
	}

	#region Nested helper types

	private static class IndexSkip<TIndexKey>
		where TIndexKey : IComparable<TIndexKey> {
		public static IndexSkip<TIndexKey, TAgg> Create<TAgg>(TIndexKey index, TAgg agg)
			where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct =>
			new(agg, index);

		public static IndecesSkip<TIndexKey, TAgg> Create<TAgg>(TIndexKey index, TIndexKey index2, TAgg agg)
			where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct =>
			new(agg, index, index2);
	}

	private ref struct IndexSkip<TIndexKey, TAgg> : PooledBTree<TIndexKey, TKey>.IResultAggregator
		where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct
		where TIndexKey : IComparable<TIndexKey> {
		private readonly TIndexKey _value;
		private TAgg _agg;

		public IndexSkip(TAgg agg, TIndexKey value) {
			_agg = agg;
			_value = value;
		}

		public void Add(TIndexKey index, TKey value) {
			if (index.CompareTo(_value) != 0)
				_agg.Add(index, value);
		}

		public void Dispose() => _agg.Dispose();
	}

	private ref struct IndecesSkip<TIndexKey, TAgg> : PooledBTree<TIndexKey, TKey>.IResultAggregator
		where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct
		where TIndexKey : IComparable<TIndexKey> {
		private readonly TIndexKey _value1;
		private readonly TIndexKey _value2;
		private TAgg _agg;

		public IndecesSkip(TAgg agg, TIndexKey value1, TIndexKey value2) {
			_agg = agg;
			_value1 = value1;
			_value2 = value2;
		}

		public void Add(TIndexKey index, TKey value) {
			if (index.CompareTo(_value1) != 0 && index.CompareTo(_value2) != 0)
				_agg.Add(index, value);
		}

		public void Dispose() => _agg.Dispose();
	}

	private ref struct ValueAdd<TIndexKey> : PooledBTree<TIndexKey, TKey>.IResultAggregator
		where TIndexKey : IComparable<TIndexKey> {
		private static TIndexKey _dummyMax = default!;

		private ref ValueSet<TKey, DefaultKeyComparer<TKey>> _candidates;
		private readonly ref TIndexKey _max;

		public ValueAdd(ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates, ref TIndexKey max) {
			_candidates = ref candidates;
			_max = ref max;
		}

		public ValueAdd(ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates) {
			_candidates = ref candidates;
			_max = ref _dummyMax;
		}

		public void Add(TIndexKey index, TKey value) {
			_max = index;
			_candidates.Add(value);
		}

		public void Dispose() {
		}
	}

	private ref struct ValueIntersect<TIndexKey> : PooledBTree<TIndexKey, TKey>.IResultAggregator
		where TIndexKey : IComparable<TIndexKey> {
		private static TIndexKey _dummyMax = default!;
		private ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter _intersecter;
		private readonly ref TIndexKey _max;

		public ValueIntersect(ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates, Span<int> buffer) {
			_intersecter = new ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter(ref candidates, buffer);
			_max = ref _dummyMax;
		}

		public ValueIntersect(ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates, Span<int> buffer, ref TIndexKey max) {
			_intersecter = new ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter(ref candidates, buffer);
			_max = ref max;
		}

		public void Add(TIndexKey index, TKey value) {
			_max = index;
			_intersecter.IntersectWith(value);
		}

		public void Dispose() => _intersecter.Dispose();
	}

	// Aggregator for intersecter-mode (Or branches): marks bits on an externally-owned
	// IncrementalIntersecter for each (index, key) the BTree walk emits. The intersecter
	// is held by value — its bitmap is a Span<int> over caller-owned memory, so marks
	// propagate. No sweep on Dispose — lifecycle owned by the caller (OrWith).
	private ref struct ValueMark<TIndexKey> : PooledBTree<TIndexKey, TKey>.IResultAggregator
		where TIndexKey : IComparable<TIndexKey> {
		private static TIndexKey _dummyMax = default!;
		private ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter _intersecter;
		private readonly ref TIndexKey _max;

		public ValueMark(ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter intersecter) {
			_intersecter = intersecter;
			_max = ref _dummyMax;
		}

		public ValueMark(ref ValueSet<TKey, DefaultKeyComparer<TKey>>.IncrementalIntersecter intersecter, ref TIndexKey max) {
			_intersecter = intersecter;
			_max = ref max;
		}

		public void Add(TIndexKey index, TKey value) {
			_max = index;
			_intersecter.IntersectWith(value);
		}

		public void Dispose() { }
	}

	#endregion

	private ref struct InitContainer: IResultContainerInitializer<TKey, TValue> {
		private ref ValueSet<TKey, DefaultKeyComparer<TKey>> _candidates;

		public InitContainer(ref ValueSet<TKey, DefaultKeyComparer<TKey>> candidates) {
			_candidates = ref candidates;
		}

		public int Add(TKey foreignKey, TValue result) => _candidates.Add(foreignKey) ? 1 : 0;

		public int TotalCount { get; }
		public void Init(int maxCount) => _candidates = new ValueSet<TKey, DefaultKeyComparer<TKey>>(maxCount);

		public void Seal(int actualCount) { }
	}

	[UnscopedRef]
	ref ValueSet<TKey1, DefaultKeyComparer<TKey1>> IUnsafeCandidatesExecutor.GetCandidates<TKey1>() {
		if (!Candidates.IsInitlized) {
			var initContainer = new InitContainer(ref Candidates);
			_dataCache.EnumerateAllValuesInit(ref initContainer, _filter);
		}

		return ref Unsafe.As<ValueSet<TKey, DefaultKeyComparer<TKey>>, ValueSet<TKey1, DefaultKeyComparer<TKey1>>>(ref Unsafe.AsRef(in Candidates));
	}
}


/// <summary>
/// Paired variant of <see cref="CacheQueryBuilderCoreCombined{TKey,TValue}"/> that stores
/// candidates as <see cref="ValueSet{T}"/> of <see cref="JoinedKeyPair{TLeft,TKey}"/>.
/// After construction the candidate set is pre-seeded (typically via <c>UseIndexAsPairs</c>)
/// and all subsequent <c>UseIndex</c> calls are intersect-only — they never add new pairs.
/// </summary>
public struct PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>
	: IDisposable, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>,
	  IOrCapable<TKey, TValue, PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>>
	where TLeft : notnull
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TKey : notnull, IEquatable<TKey> {

	internal readonly InMemoryDataCache<TKey, TValue> _dataCache;
	private Predicate<TValue>? _filter;
	private bool _disposed;
	private bool _isIntersecter;
	internal bool _first;
	internal ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>> _candidates;
	internal RefHolder<ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>> _incrementalIntersecter;

	// Stub field — returned by the ICandidatesExecutor.Candidates getter which is only
	// used by the unpaired code paths (joins walk paired sets directly via ExecuteBase).
	private static ValueSet<TKey, DefaultKeyComparer<TKey>> _stubKeys;

	ref ValueSet<TKey, DefaultKeyComparer<TKey>> ICandidatesExecutor<TKey, TValue>.Candidates {
		[UnscopedRef] get => ref _stubKeys;
	}

void ICandidatesExecutor<TKey, TValue>.ExecuteBase<TContainer>(ref TContainer c) => Execute(ref c);
	int ICandidatesExecutor<TKey, TValue>.CountBase() => 0; // stub — not needed in Phase α

	/// <summary>
	/// Constructs with a pre-seeded candidate set.  The caller (typically <c>UseIndexAsPairs</c>)
	/// is responsible for creating and populating the set.
	/// </summary>
	internal PairedCacheQueryBuilderCoreCombined(
		InMemoryDataCache<TKey, TValue> dataCache,
		ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>> seededCandidates) {
		_dataCache = dataCache;
		_filter = null;
		_disposed = false;
		_first = false; // paired is always seeded
		_isIntersecter = false;
		_candidates = seededCandidates;
	}

	/// <summary>
	/// Intersecter-mode ctor: branch executor inside a paired Or branch. Operates on a
	/// shared external intersecter over outer's paired _candidates.
	/// </summary>
	internal PairedCacheQueryBuilderCoreCombined(
		InMemoryDataCache<TKey, TValue> dataCache,
		ref ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait> incrementalIntersecter) {
		_dataCache = dataCache;
		_filter = null;
		_disposed = false;
		_first = true;
		_incrementalIntersecter = new RefHolder<ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>>(ref incrementalIntersecter);
		_isIntersecter = true;
	}

	#region IOrCapable<PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>>

	[UnscopedRef]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait> CreateIntersecter(Span<int> buffer)
		=> _isIntersecter
			? _incrementalIntersecter.Value.CreateChild(buffer)
			: new ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
				JoinedKeyPair<TLeft, TKey>.Into, ref _candidates, buffer);

	public void OrWith<TCache, TResolverChain, TResult, TBranch>(in NarrowOnlyQuery<TCache> narrowOnly, TResolverChain resolverChain,
		in TBranch b1, in TBranch b2)
		where TResolverChain : struct, IResolvers
		where TBranch : struct, IOrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>, TKey, TValue, TResolverChain, TResult>> {

		Span<int> twoBuffers = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold * 2];
		var buffer1 = twoBuffers[..ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
		var buffer2 = twoBuffers[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold..];

		var intersector1 = CreateIntersecter(buffer1);
		var executor1 = new PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>(_dataCache, ref intersector1);
		var b1Builder = new CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>, TKey, TValue, TResolverChain, TResult>(
			narrowOnly, executor1, resolverChain, 0);

		var intersector2 = CreateIntersecter(buffer2);
		var executor2 = new PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>(_dataCache, ref intersector2);
		var b2Builder = new CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>, TKey, TValue, TResolverChain, TResult>(
			narrowOnly, executor2, resolverChain, 0);

		try {
			b1Builder = b1.Apply(b1Builder);
			b2Builder = b2.Apply(b2Builder);

			intersector1.Union(ref intersector2);
			if (_isIntersecter)
				_incrementalIntersecter.Value.Union(intersector1.Bits);
		} finally {
			intersector1.Dispose(!_isIntersecter && (!b1Builder._leftQuery._first || !b2Builder._leftQuery._first));
			intersector2.Dispose(false);
		}
	}

	#endregion

	#region UseIndex methods — intersect-only against the pre-seeded paired set

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		TIndexKey value) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) return;
			if (!index.TryGetValue(value, out var key)) {
				intersector.Clear();
			} else if (_first) {
				intersector.IntersectWith(key);
			} else {
				intersector.RetainOnly(key);
			}
			_first = false;
			return;
		}

		// Direct mode: mark-and-sweep via fresh intersecter; sweep on dispose.
		Span<int> stack = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
		using var intersecter = new ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
			JoinedKeyPair<TLeft, TKey>.Into, ref _candidates, stack);
		if (index.TryGetValue(value, out var k))
			intersecter.IntersectWith(k);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		List<TIndexKey> values) =>
		((ICandidatesFilterer<TKey, TValue>)this).UseIndexInternal(index, CollectionsMarshal.AsSpan(values));

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TIndexKey> values) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) return;
			if (values.Length == 0) {
				intersector.Clear();
			} else if (_first) {
				foreach (var v in values)
					if (index.TryGetValue(v, out var key))
						intersector.IntersectWith(key);
			} else if (values.Length == 1) {
				if (index.TryGetValue(values[0], out var key))
					intersector.RetainOnly(key);
				else
					intersector.Clear();
			} else {
				// Multi-value prune: mark union into sub-bitmap, AND into main.
				Span<int> subBuf = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
				var sub = CreateIntersecter(subBuf);
				try {
					foreach (var v in values)
						if (index.TryGetValue(v, out var key))
							sub.IntersectWith(key);
					intersector.Intersect(sub.Bits);
				} finally {
					sub.Dispose(false);
				}
			}
			_first = false;
			return;
		}

		// Direct mode: mark-and-sweep against _candidates.
		if (values.Length == 0) {
			_candidates.Clear();
			return;
		}
		Span<int> stack = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
		using var intersecter = new ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
			JoinedKeyPair<TLeft, TKey>.Into, ref _candidates, stack);
		foreach (var v in values)
			if (index.TryGetValue(v, out var k))
				intersecter.IntersectWith(k);
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		TIndexKey value) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) return;
			var pooled = index.GetValuesUnsafe(value);
			if (pooled.IsEmpty) {
				intersector.Clear();
			} else if (_first) {
				intersector.IntersectWith(pooled);
			} else {
				intersector.RetainOnly(pooled);
			}
			_first = false;
			return;
		}

		// Direct intersect against the index's stored PooledSet — no temp allocation.
		_candidates.IntersectWith<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
			JoinedKeyPair<TLeft, TKey>.Into,
			index.GetValuesUnsafe(value));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		List<TIndexKey> values) =>
		((ICandidatesFilterer<TKey, TValue>)this).UseIndexInternal(index, CollectionsMarshal.AsSpan(values));

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TIndexKey> values) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) return;
			if (values.Length == 0) {
				intersector.Clear();
			} else if (_first) {
				foreach (var v in values) {
					var pooled = index.GetValuesUnsafe(v);
					if (!pooled.IsEmpty) intersector.IntersectWith(pooled);
				}
			} else if (values.Length == 1) {
				var pooled = index.GetValuesUnsafe(values[0]);
				if (pooled.IsEmpty) intersector.Clear();
				else intersector.RetainOnly(pooled);
			} else {
				// Multi-value prune: mark union into sub-bitmap, AND into main.
				Span<int> subBuf = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
				var sub = CreateIntersecter(subBuf);
				try {
					foreach (var v in values) {
						var pooled = index.GetValuesUnsafe(v);
						if (!pooled.IsEmpty) sub.IntersectWith(pooled);
					}
					intersector.Intersect(sub.Bits);
				} finally {
					sub.Dispose(false);
				}
			}
			_first = false;
			return;
		}

		// Direct mode.
		if (values.Length == 0)
			return;

		if (values.Length == 1) {
			_candidates.IntersectWith<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
				JoinedKeyPair<TLeft, TKey>.Into,
				index.GetValuesUnsafe(values[0]));
			return;
		}

		Span<int> stack = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
		using var intersecter = new ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
			JoinedKeyPair<TLeft, TKey>.Into, ref _candidates, stack);
		foreach (var v in values) {
			var pooled = index.GetValuesUnsafe(v);
			if (!pooled.IsEmpty)
				intersecter.IntersectWith(pooled);
		}
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey, TOtherValue>(
		CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
		ReadOnlySpan<TOtherValue> values,
		Func<TOtherValue, TIndexKey> keySelector) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) return;
			if (values.Length == 0) {
				intersector.Clear();
			} else if (_first) {
				foreach (var v in values) {
					var pooled = index.GetValuesUnsafe(keySelector(v));
					if (!pooled.IsEmpty) intersector.IntersectWith(pooled);
				}
			} else if (values.Length == 1) {
				var pooled = index.GetValuesUnsafe(keySelector(values[0]));
				if (pooled.IsEmpty) intersector.Clear();
				else intersector.RetainOnly(pooled);
			} else {
				Span<int> subBuf = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
				var sub = CreateIntersecter(subBuf);
				try {
					foreach (var v in values) {
						var pooled = index.GetValuesUnsafe(keySelector(v));
						if (!pooled.IsEmpty) sub.IntersectWith(pooled);
					}
					intersector.Intersect(sub.Bits);
				} finally {
					sub.Dispose(false);
				}
			}
			_first = false;
			return;
		}

		// Direct mode.
		if (values.Length == 0)
			return;

		if (values.Length == 1) {
			_candidates.IntersectWith<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
				JoinedKeyPair<TLeft, TKey>.Into,
				index.GetValuesUnsafe(keySelector(values[0])));
			return;
		}

		Span<int> stack = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
		using var intersecter = new ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
			JoinedKeyPair<TLeft, TKey>.Into, ref _candidates, stack);
		foreach (var v in values) {
			var pooled = index.GetValuesUnsafe(keySelector(v));
			if (!pooled.IsEmpty)
				intersecter.IntersectWith(pooled);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter) =>
		UseIndexInternalLastUpdated(lastUpdatedIndex.Index, new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds());

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter) =>
		UseIndexInternalLastUpdated(lastUpdatedIndex.Index, updatedAfter.ToUnixTimeMilliseconds());

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		long updatedAfter) =>
		UseIndexInternalLastUpdated(lastUpdatedIndex.Index, updatedAfter);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		out long max) {
		max = 0;
		UseIndexInternalLastUpdated(lastUpdatedIndex.Index, updatedAfter);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter) =>
		UseIndexInternalLastUpdated(lastUpdatedIndex, new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds());

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter) =>
		UseIndexInternalLastUpdated(lastUpdatedIndex, updatedAfter.ToUnixTimeMilliseconds());

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter) =>
		UseIndexInternalLastUpdated(lastUpdatedIndex, updatedAfter);

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter,
		out long max) {
		max = 0;
		UseIndexInternalLastUpdated(lastUpdatedIndex, new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds());
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter,
		out long max) {
		max = 0;
		UseIndexInternalLastUpdated(lastUpdatedIndex, updatedAfter.ToUnixTimeMilliseconds());
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		out long max) {
		max = 0;
		UseIndexInternalLastUpdated(lastUpdatedIndex, updatedAfter);
	}

	private void UseIndexInternalLastUpdated(LastUpdatedIndex<TKey> lastUpdatedIndex, long updatedAfter) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) { _first = false; return; }
			long dummyMax = 0;
			if (_first) {
				var va = new PairedValueMark<long>(intersector, ref dummyMax);
				lastUpdatedIndex.GetValuesGt(updatedAfter, ref va);
			} else {
				Span<int> subBuf = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
				var sub = CreateIntersecter(subBuf);
				try {
					var va = new PairedValueMark<long>(sub, ref dummyMax);
					lastUpdatedIndex.GetValuesGt(updatedAfter, ref va);
					intersector.Intersect(sub.Bits);
				} finally {
					sub.Dispose(false);
				}
			}
			_first = false;
			return;
		}

		// Direct mode: mark-and-sweep via fresh intersecter.
		Span<int> stack = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
		using var intersecter = new ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
			JoinedKeyPair<TLeft, TKey>.Into, ref _candidates, stack);
		long dm = 0;
		var pv = new PairedValueMark<long>(intersecter, ref dm);
		lastUpdatedIndex.GetValuesGt(updatedAfter, ref pv);
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter,
		DateTime updatedUntilInclusive) =>
		UseIndexInternalLastUpdatedRange(lastUpdatedIndex.Index,
			new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds(),
			new DateTimeOffset(updatedUntilInclusive).ToUnixTimeMilliseconds());

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter,
		DateTimeOffset updatedUntilInclusive) =>
		UseIndexInternalLastUpdatedRange(lastUpdatedIndex.Index,
			updatedAfter.ToUnixTimeMilliseconds(),
			updatedUntilInclusive.ToUnixTimeMilliseconds());

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		long updatedUntilInclusive) =>
		UseIndexInternalLastUpdatedRange(lastUpdatedIndex.Index, updatedAfter, updatedUntilInclusive);

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTime updatedAfter,
		DateTime updatedUntilInclusive) =>
		UseIndexInternalLastUpdatedRange(lastUpdatedIndex,
			new DateTimeOffset(updatedAfter).ToUnixTimeMilliseconds(),
			new DateTimeOffset(updatedUntilInclusive).ToUnixTimeMilliseconds());

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		DateTimeOffset updatedAfter,
		DateTimeOffset updatedUntilInclusive) =>
		UseIndexInternalLastUpdatedRange(lastUpdatedIndex,
			updatedAfter.ToUnixTimeMilliseconds(),
			updatedUntilInclusive.ToUnixTimeMilliseconds());

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		long updatedUntilInclusive) =>
		UseIndexInternalLastUpdatedRange(lastUpdatedIndex, updatedAfter, updatedUntilInclusive);

	private void UseIndexInternalLastUpdatedRange(
		LastUpdatedIndex<TKey> lastUpdatedIndex,
		long updatedAfter,
		long updatedUntilInclusive) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) { _first = false; return; }
			if (_first) {
				var va = PairedIndexSkip<long>.Create(updatedAfter, new PairedValueMark<long>(intersector));
				lastUpdatedIndex.GetValuesBetween(updatedAfter, updatedUntilInclusive, ref va);
			} else {
				Span<int> subBuf = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
				var sub = CreateIntersecter(subBuf);
				try {
					var va = PairedIndexSkip<long>.Create(updatedAfter, new PairedValueMark<long>(sub));
					lastUpdatedIndex.GetValuesBetween(updatedAfter, updatedUntilInclusive, ref va);
					intersector.Intersect(sub.Bits);
				} finally {
					sub.Dispose(false);
				}
			}
			_first = false;
			return;
		}

		// Direct mode.
		Span<int> stack = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
		using var intersecter = new ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
			JoinedKeyPair<TLeft, TKey>.Into, ref _candidates, stack);
		var pv = PairedIndexSkip<long>.Create(updatedAfter, new PairedValueMark<long>(intersecter));
		lastUpdatedIndex.GetValuesBetween(updatedAfter, updatedUntilInclusive, ref pv);
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey, TQueryBuilder>(
		CacheRangeIndex<TKey, TValue, TIndexKey> index,
		Func<RangeQueryBuilder<TIndexKey>, TQueryBuilder> rangeBuilder) =>
		UseIndexCoreRange(index, rangeBuilder(new RangeQueryBuilder<TIndexKey>()));

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal<TIndexKey, TQueryBuilder, TArgs>(
		CacheRangeIndex<TKey, TValue, TIndexKey> index,
		Func<RangeQueryBuilder<TIndexKey>, TArgs, TQueryBuilder> rangeBuilder,
		TArgs args) =>
		UseIndexCoreRange(index, rangeBuilder(new RangeQueryBuilder<TIndexKey>(), args));

	private void UseIndexCoreRange<TIndexKey, TQueryBuilder>(
		CacheRangeIndex<TKey, TValue, TIndexKey> index, TQueryBuilder f)
		where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey>
		where TIndexKey : IComparable<TIndexKey> {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) { _first = false; return; }
			if (_first) {
				DispatchPairedRangeMark(index, f, ref intersector);
			} else {
				Span<int> subBuf = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
				var sub = CreateIntersecter(subBuf);
				try {
					DispatchPairedRangeMark(index, f, ref sub);
					intersector.Intersect(sub.Bits);
				} finally {
					sub.Dispose(false);
				}
			}
			_first = false;
			return;
		}

		// Direct mode: fresh intersecter on _candidates, sweep on dispose.
		Span<int> stack = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
		var intersecter = new ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait>(
			JoinedKeyPair<TLeft, TKey>.Into, ref _candidates, stack);
		try {
			DispatchPairedRangeMark(index, f, ref intersecter);
		} finally {
			intersecter.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void DispatchPairedRangeMark<TIndexKey, TQueryBuilder>(
		CacheRangeIndex<TKey, TValue, TIndexKey> index,
		TQueryBuilder f,
		scoped ref ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait> intersecter)
		where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey>
		where TIndexKey : IComparable<TIndexKey> {
		switch (f) {
			case ((RangeValueType.ThanOrEqual, var gte), (RangeValueType.None, _)): {
				var va = new PairedValueMark<TIndexKey>(intersecter);
				index.GetValuesGte(gte, ref va);
				break;
			}
			case ((RangeValueType.ThanOrEqual, var gte), (RangeValueType.Than, var lt)): {
				var va = PairedIndexSkip<TIndexKey>.Create(lt, new PairedValueMark<TIndexKey>(intersecter));
				index.GetValuesBetween(gte, lt, ref va);
				break;
			}
			case ((RangeValueType.ThanOrEqual, var gte), (RangeValueType.ThanOrEqual, var lte)): {
				var va = new PairedValueMark<TIndexKey>(intersecter);
				index.GetValuesBetween(gte, lte, ref va);
				break;
			}
			case ((RangeValueType.Than, var gt), (RangeValueType.None, _)): {
				var va = PairedIndexSkip<TIndexKey>.Create(gt, new PairedValueMark<TIndexKey>(intersecter));
				index.GetValuesGte(gt, ref va);
				break;
			}
			case ((RangeValueType.Than, var gt), (RangeValueType.Than, var lt)): {
				var va = PairedIndexSkip<TIndexKey>.Create(gt, lt, new PairedValueMark<TIndexKey>(intersecter));
				index.GetValuesBetween(gt, lt, ref va);
				break;
			}
			case ((RangeValueType.Than, var gt), (RangeValueType.ThanOrEqual, var lte)): {
				var va = PairedIndexSkip<TIndexKey>.Create(gt, new PairedValueMark<TIndexKey>(intersecter));
				index.GetValuesBetween(gt, lte, ref va);
				break;
			}
			case ((RangeValueType.None, _), (RangeValueType.ThanOrEqual, var lte)): {
				var va = new PairedValueMark<TIndexKey>(intersecter);
				index.GetValuesLte(lte, ref va);
				break;
			}
			case ((RangeValueType.None, _), (RangeValueType.Than, var lt)): {
				var va = PairedIndexSkip<TIndexKey>.Create(lt, new PairedValueMark<TIndexKey>(intersecter));
				index.GetValuesLte(lt, ref va);
				break;
			}
			default: throw new UnreachableException();
		}
	}

	void ICandidatesFilterer<TKey, TValue>.UseIndexInternal(CacheKeySetIndex<TKey, TValue> index) {
		if (_isIntersecter) {
			ref var intersector = ref _incrementalIntersecter.Value;
			if (intersector.IsCleared) { _first = false; return; }
			if (_first) {
				index.IntersectKeysWithPaired<TLeft>(ref intersector);
			} else {
				Span<int> subBuf = stackalloc int[ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.StackAllocThreshold];
				var sub = CreateIntersecter(subBuf);
				try {
					index.IntersectKeysWithPaired<TLeft>(ref sub);
					intersector.Intersect(sub.Bits);
				} finally {
					sub.Dispose(false);
				}
			}
			_first = false;
			return;
		}

		// Direct intersect against the index's stored PooledSet of keys (locked internally
		// by the index). No temp allocation.
		index.IntersectWithPaired<TLeft>(ref _candidates);
	}

	#endregion

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesFilterer<TKey, TValue>.WhereInternal(Predicate<TValue> predicate) {
		var currentFilter = _filter;
		_filter = currentFilter == null ? predicate : v => currentFilter(v) && predicate(v);
	}

	/// <summary>
	/// Executes the paired query. Projects the paired candidates to an unpaired
	/// <c>ValueSet&lt;TKey&gt;</c> and delegates to the standard unpaired
	/// <see cref="InMemoryDataCache{TKey,TValue}.TryGet"/> path, which calls the plain
	/// <c>IResultContainerInitializer&lt;TKey, TValue&gt;</c> contract already held by
	/// <typeparamref name="TContainer"/>.
	/// Auto-disposes after execution.
	/// </summary>
	internal void Execute<TContainer>(ref TContainer container)
		where TContainer : struct, IResultContainerInitializer<TKey, TValue>, allows ref struct {
		if (_disposed)
			throw new ObjectDisposedException(nameof(PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>));

		try {
			if (!_candidates.IsInitlized || _candidates.Count == 0)
				return;

			// Project pairs → plain keys so we can reuse the standard unpaired TryGet path.
			var keys = new ValueSet<TKey, DefaultKeyComparer<TKey>>(_candidates.Count);
			try {
				foreach (var pair in _candidates)
					keys.Add(pair.Key);

				if (keys.Count == 0)
					return;

				container.Init(keys.Count);
				var actualCount = _dataCache.TryGet(ref container, ref keys, _filter);
				container.Seal(actualCount);
			} finally {
				keys.Dispose();
			}
		} finally {
			Dispose();
		}
	}

	/// <summary>
	/// Executes the paired query writing directly to a <see cref="IJoinedResultContainer{TForeignKey,TValue}"/>.
	/// Each <see cref="JoinedKeyPair{TLeft,TKey}"/> is resolved: the cache is looked up by
	/// <c>pair.Key</c> (<typeparamref name="TKey"/>), and the result is written via
	/// <c>container.Add(pair.JoinedKey, value)</c> (<typeparamref name="TLeft"/> foreign key).
	/// No intermediate projection to an unpaired key set; no per-emit dictionary lookup.
	/// Auto-disposes after execution.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void ExecutePaired<TContainer>(ref TContainer container)
		where TContainer : struct, IJoinedResultContainer<TLeft, TValue>, allows ref struct {
		if (_disposed)
			throw new ObjectDisposedException(nameof(PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>));

		try {
			if (!_candidates.IsInitlized || _candidates.Count == 0)
				return;

			// Native paired read — InMemoryDataCache has the matching overload at InMemoryDataCache.cs:957.
			// Looks up by pair.Key (TKey/right key) and calls container.Add(pair.JoinedKey, value).
			_ = _dataCache.TryGet<TLeft, TContainer>(ref container, ref _candidates, _filter);
		} finally {
			Dispose();
		}
	}

public void Dispose() {
		if (_disposed) return;
		_candidates.Dispose();
		_disposed = true;
	}

	#region Nested helper types

	private static class PairedIndexSkip<TIndexKey>
		where TIndexKey : IComparable<TIndexKey> {
		public static PairedIndexSkipInner<TIndexKey, TAgg> Create<TAgg>(TIndexKey index, TAgg agg)
			where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct =>
			new(agg, index);

		public static PairedIndecesSkip<TIndexKey, TAgg> Create<TAgg>(TIndexKey index, TIndexKey index2, TAgg agg)
			where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct =>
			new(agg, index, index2);
	}

	private ref struct PairedIndexSkipInner<TIndexKey, TAgg> : PooledBTree<TIndexKey, TKey>.IResultAggregator
		where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct
		where TIndexKey : IComparable<TIndexKey> {
		private readonly TIndexKey _value;
		private TAgg _agg;

		public PairedIndexSkipInner(TAgg agg, TIndexKey value) {
			_agg = agg;
			_value = value;
		}

		public void Add(TIndexKey index, TKey value) {
			if (index.CompareTo(_value) != 0)
				_agg.Add(index, value);
		}

		public void Dispose() => _agg.Dispose();
	}

	private ref struct PairedIndecesSkip<TIndexKey, TAgg> : PooledBTree<TIndexKey, TKey>.IResultAggregator
		where TAgg : struct, PooledBTree<TIndexKey, TKey>.IResultAggregator, allows ref struct
		where TIndexKey : IComparable<TIndexKey> {
		private readonly TIndexKey _value1;
		private readonly TIndexKey _value2;
		private TAgg _agg;

		public PairedIndecesSkip(TAgg agg, TIndexKey value1, TIndexKey value2) {
			_agg = agg;
			_value1 = value1;
			_value2 = value2;
		}

		public void Add(TIndexKey index, TKey value) {
			if (index.CompareTo(_value1) != 0 && index.CompareTo(_value2) != 0)
				_agg.Add(index, value);
		}

		public void Dispose() => _agg.Dispose();
	}

	/// <summary>
	/// Aggregator that marks matching slots directly in <c>_candidates</c> via an
	/// <see cref="ValueSet{T}.IncrementalIntersecter{TFrom, TInto}"/> — no temp <see cref="ValueSet{TKey}"/>
	/// allocation. Each B-tree result calls <c>_intersecter.IntersectWith(value)</c> which marks
	/// the slot whose pair has <c>.Key == value</c>.
	/// <para>
	/// The intersecter is stored by value (C# forbids ref-to-ref-struct fields, CS9050).
	/// This is safe because the BitHelper inside the intersecter holds its bitmap as a
	/// <see cref="Span{T}"/> — value-copy duplicates the Span header but both copies reference
	/// the same underlying stack/pool buffer, so marks via either copy mutate the shared bitmap.
	/// Only the original intersecter's <c>Dispose</c> (via the caller's <c>using</c>) sweeps
	/// unmarked slots and returns the rented array; the copy's <c>Dispose</c> is empty.
	/// </para>
	/// </summary>
	private ref struct PairedValueMark<TIndexKey> : PooledBTree<TIndexKey, TKey>.IResultAggregator
		where TIndexKey : IComparable<TIndexKey> {
		private static TIndexKey _dummyMax = default!;
		private ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait> _intersecter;
		private readonly ref TIndexKey _max;

		public PairedValueMark(
			ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait> intersecter,
			ref TIndexKey max) {
			_intersecter = intersecter;
			_max = ref max;
		}

		public PairedValueMark(
			ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>.IncrementalIntersecter<TKey, JoinedKeyPair<TLeft, TKey>.IntoTrait> intersecter) {
			_intersecter = intersecter;
			_max = ref _dummyMax;
		}

		public void Add(TIndexKey index, TKey value) {
			_max = index;
			_intersecter.IntersectWith(value);
		}

		public void Dispose() { }
	}

	#endregion
}

public struct CacheQueryBuilderCombined<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, TResult>
	: ICandidatesExecutor<TLeftKey, TLeftValue>
	where TDiscriminator : struct
	where TLeftQuery : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
	where TLeftKey : notnull, IEquatable<TLeftKey>
	where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
	where TResolverChain : struct, IResolvers
{
	internal TDiscriminator _discriminator;
	internal TLeftQuery _leftQuery;
	public TResolverChain _resolverChain;
	internal readonly int _manyCount;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref TDiscriminator GetDiscriminator(ref CacheQueryBuilderCombined<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TResolverChain, TResult> builder)
		=> ref builder._discriminator;

	public CacheQueryBuilderCombined(TDiscriminator discriminator, TLeftQuery leftQuery, TResolverChain resolver1, int manyCount) {
		_discriminator = discriminator;
		_leftQuery = leftQuery;
		_resolverChain = resolver1;
		_manyCount = manyCount;
	}

	public CacheQueryBuilderCombined<TNewDescriminator, TLeftQuery, TLeftKey, TLeftValue, TNewResolverChain, TResult>
		AddResolver<TNewDescriminator, TNewResolverChain>(TNewDescriminator descriminator, TNewResolverChain resolver)
		where TNewResolverChain: struct, IResolvers
		where TNewDescriminator : struct {
		return
			new CacheQueryBuilderCombined<TNewDescriminator, TLeftQuery, TLeftKey, TLeftValue, TNewResolverChain, TResult>(
				descriminator,
				_leftQuery,
				resolver,
				_manyCount);
	}

	public CacheQueryBuilderCombined<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TNewResolverChain, TNewResult>
		AddResolver<TNewResolverChain, TNewResult>(TNewResolverChain resolver, bool isMany)
		where TNewResolverChain: struct, IResolvers {
		return
			new CacheQueryBuilderCombined<TDiscriminator, TLeftQuery, TLeftKey, TLeftValue, TNewResolverChain, TNewResult>(
				_discriminator,
				_leftQuery,
				resolver,
				isMany ? _manyCount+1 : _manyCount
				);
	}

	internal int CountCoreJoined<TJoinResult>()
		where TJoinResult : struct, IJoinResult<TLeftValue> {

		var container = new JoinedResultContaier<TLeftKey, TLeftValue, TResolverChain, TJoinResult>(
			ref _resolverChain, true, false, 0, int.MaxValue, _manyCount);
		try {
			container.PrepareIndexedInner(ref this);
			// Same narrowing Execute runs: indexed-inner resolvers drop non-matching lefts from
			// the candidate set (RetainNonNullSlots), so CountBase counts matched rows.
			container.ExecuteIndexedInner(ref this);
			return _leftQuery.CountBase();
		} finally {
			container.Dispose();
		}
	}

	internal QueryResults<TJoinResult> ExecuteCoreJoined<TJoinResult>(bool pool, bool clone, int skip, int take)
		where TJoinResult : struct, IJoinResult<TLeftValue> {

		var container = new JoinedResultContaier<TLeftKey, TLeftValue, TResolverChain, TJoinResult>(
			ref _resolverChain, pool, clone, skip, take, _manyCount);
		try {

			container.PrepareIndexedInner(ref this);
			container.ExecuteIndexedInner(ref this);
			_leftQuery.ExecuteBase(ref container);
			container.ExecuteJoins();

			return container.BuildResults();
		} finally {
			container.Dispose();
		}
	}

	// Nested-join seam: run the full joined pipeline but hand back the keyed result map
	// (desk key → JoinResult row) instead of the flattened QueryResults, plus the disposer
	// holding any pooled inner-Many buffers. The caller (JoinManyNestedResolver) seeds
	// Candidates with the union of the outer parents' children first, runs this once, then
	// scatters rows back to parents by key. Ownership of `results`/`disposer` moves to the caller.
	internal void ExecuteCoreJoinedKeyed<TJoinResult>(
		bool pool,
		bool clone,
		out ValueDictionary<TLeftKey, TJoinResult, DefaultKeyComparer<TLeftKey>> results,
		out QueryResultsDisposer disposer)
		where TJoinResult : struct, IJoinResult<TLeftValue> {

		// clone=true uses the inner plan's add-time cloning (null-safe: only matched values are
		// cloned), so the extracted rows are independent of the source caches.
		var container = new JoinedResultContaier<TLeftKey, TLeftValue, TResolverChain, TJoinResult>(
			ref _resolverChain, pool, clone, 0, int.MaxValue, _manyCount);
		try {
			container.PrepareIndexedInner(ref this);
			container.ExecuteIndexedInner(ref this);
			_leftQuery.ExecuteBase(ref container);
			container.ExecuteJoins();
			container.ExtractKeyedResults(out results, out disposer);
		} finally {
			// ExtractKeyedResults defaults the container's dict/disposer, so on success this
			// is a no-op; on a throw it returns everything the container still owns.
			container.Dispose();
			// A seeded/auto-populated candidate set the base execution never consumed is still
			// ours. Base execution disposes candidates BY REF through this same storage
			// (_leftQuery.Candidates is a ref to the leaf's field — verified Task 3 Step 1), so
			// after a legitimate consume the field's arrays are already null and this Dispose is a
			// no-op (ValueSet.Dispose is idempotent: ReturnArrays nulls + null-guards each array).
			// On a throw before base execution the arrays are still rented — this returns them
			// exactly once. IsInitlized only gates the never-seeded (default) case.
			var candidates = _leftQuery.Candidates;
			if (candidates.IsInitlized)
				candidates.Dispose();
		}
	}

	// Overwrite the candidate set the next Execute* walks. Used by the nested-join resolver
	// to constrain an inner plan to the union of the outer parents' children before executing it.
	internal void UnsafeSeedCandidates(ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> candidates) {
		_leftQuery.Candidates = candidates;
	}

	internal QueryResults<TLeftValue> ExecuteCoreSimple<TResolver>(ref TResolver resolver, bool pool, bool clone, int skip, int take)
		where TResolver: struct, IJoinResolver {
		var container = new SimpleResultContainer<TLeftKey, TLeftValue, TResolver>(resolver, pool, clone, skip, take);
		try {
			_leftQuery.ExecuteBase(ref container);
			return container.BuildResults();
		} finally {
			// No-op once BuildResults hands the buffer to the returned QueryResults; returns
			// the rented buffer on the empty-result and user-predicate-throw paths.
			container.Dispose();
		}
	}


	internal int CountCoreSimple() {
		return _leftQuery.CountBase();
	}

	// public InMemoryDataCache<TLeftKey, TLeftValue> DataCache
	// 	=> _leftQuery.DataCache;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void ICandidatesExecutor<TLeftKey, TLeftValue>.ExecuteBase<TContainer>(ref TContainer c) {
		_leftQuery.ExecuteBase(ref c);
	}

	int ICandidatesExecutor<TLeftKey, TLeftValue>.CountBase() => _leftQuery.CountBase();

	[UnscopedRef]
	ref ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>> ICandidatesExecutor<TLeftKey, TLeftValue>.Candidates => ref _leftQuery.Candidates;

	[UnscopedRef]
	ref ValueSet<TKey1, DefaultKeyComparer<TKey1>> IUnsafeCandidatesExecutor.GetCandidates<TKey1>() => ref _leftQuery.GetCandidates<TKey1>(); // Unsafe.As<ValueSet<TLeftKey, DefaultKeyComparer<TLeftKey>>, ValueSet<TKey1, DefaultKeyComparer<TKey1>>>(ref _leftQuery.Candidates);
}



#region CacheQueryBuilderNew Extension Methods

public static class CacheQueryBuilderCombinedFilterExtenisons {

	// CacheKeyValueIndex overloads

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
			TIndexKey value)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TIndexKey : notnull {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index, value);
		return builder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
			List<TIndexKey> values)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TIndexKey : notnull {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index, values);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
			ReadOnlySpan<TIndexKey> values)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TIndexKey : notnull {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index, values);
		return builder;
	}

	// CacheKeyValueListIndex overloads

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			TIndexKey value)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TIndexKey : notnull {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index, value);
		return builder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			List<TIndexKey> values)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TIndexKey : notnull {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index, values);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TIndexKey, TOtherValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			ReadOnlySpan<TOtherValue> values,
			Func<TOtherValue, TIndexKey> keySelector)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TIndexKey : notnull {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index, values, keySelector);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			ReadOnlySpan<TIndexKey> values)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TIndexKey : notnull {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index, values);
		return builder;
	}

	// IDataCacheGlobalLastUpdateIndex overloads

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			DateTime updatedAfter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter);
		return builder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			DateTimeOffset updatedAfter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter);
		return builder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			long updatedAfter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter);
		return builder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			long updatedAfter,
			out long max)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, out max);
		return builder;
	}

	// LastUpdatedIndex overloads

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTime updatedAfter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter);
		return builder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTimeOffset updatedAfter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter);
		return builder;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			long updatedAfter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTime updatedAfter,
			out long max)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, out max);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTimeOffset updatedAfter,
			out long max)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, out max);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			long updatedAfter,
			out long max)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, out max);
		return builder;
	}

	// IDataCacheGlobalLastUpdateIndex range overloads (updatedAfter, updatedUntilInclusive)

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			DateTime updatedAfter,
			DateTime updatedUntilInclusive)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, updatedUntilInclusive);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			DateTimeOffset updatedAfter,
			DateTimeOffset updatedUntilInclusive)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, updatedUntilInclusive);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			long updatedAfter,
			long updatedUntilInclusive)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, updatedUntilInclusive);
		return builder;
	}

	// LastUpdatedIndex range overloads (updatedAfter, updatedUntilInclusive)

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTime updatedAfter,
			DateTime updatedUntilInclusive)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, updatedUntilInclusive);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTimeOffset updatedAfter,
			DateTimeOffset updatedUntilInclusive)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, updatedUntilInclusive);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			long updatedAfter,
			long updatedUntilInclusive)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(lastUpdatedIndex, updatedAfter, updatedUntilInclusive);
		return builder;
	}

	// CacheRangeIndex overloads

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TIndexKey, TQueryBuilder>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheRangeIndex<TKey, TValue, TIndexKey> index,
			Func<RangeQueryBuilder<TIndexKey>, TQueryBuilder> rangeBuilder)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey>
		where TIndexKey : IComparable<TIndexKey> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index, rangeBuilder);
		return builder;
	}

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TIndexKey, TQueryBuilder, TArgs>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheRangeIndex<TKey, TValue, TIndexKey> index,
			Func<RangeQueryBuilder<TIndexKey>, TArgs, TQueryBuilder> rangeBuilder,
			TArgs args)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TQueryBuilder : struct, IRangeQueryBuilder<TIndexKey>
		where TIndexKey : IComparable<TIndexKey> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index, rangeBuilder, args);
		return builder;
	}

	// CacheKeySetIndex overload

	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeySetIndex<TKey, TValue> index)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder._leftQuery).UseIndexInternal(index);
		return builder;
	}

	// Where

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>
		Where<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			Predicate<TValue> predicate)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IBaseFilterable
		where TResolverChain : struct, IResolvers
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		Unsafe.AsRef(in builder)._leftQuery.WhereInternal(predicate);
		return builder;
	}
}

internal static class CacheQueryBuilderCombinedDiscriminatorExtensions {
	/// <summary>
	/// Swaps the discriminator from <see cref="ExecutableQuery{TCache}"/> to
	/// <see cref="NonExecutableQuery{TCache}"/>, preserving the cache carrier so
	/// filter callbacks can call <c>WithXxx</c> extensions while <c>Execute*</c>
	/// remains unreachable at the call site.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static CacheQueryBuilderCombined<NonExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>
		AsNonExecutable<TCache, TExecutor, TKey, TValue, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<ExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult> builder)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> =>
		new(
			new NonExecutableQuery<TCache>(builder._discriminator.Cache),
			builder._leftQuery,
			builder._resolverChain,
			0);
}

public static class CacheQueryBuilderCombinedSortExtensions {
	public static
		CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, Resolvers<SortResolver<TKey, TValue, TResult, TComparer>>, TResult>
		Sort<TDiscriminator, TExecutor, TResult, TKey, TValue, TComparer>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TResult> builder,
			TComparer comparer)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IBaseFilterable
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TComparer : IComparer<TResult>
		where TKey : IEquatable<TKey> {
		var resolver = new SortResolver<TKey, TValue, TResult, TComparer>(comparer);
		return Unsafe.AsRef(in builder).AddResolver(
			new SortedQuery<TDiscriminator>(builder._discriminator),
			new Resolvers<SortResolver<TKey, TValue, TResult, TComparer>>(resolver));
	}

	public static
		CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, Resolvers<TResolverChain, SortResolver<TKey, TValue, TResult, TComparer>>, TResult>
		Sort<TDiscriminator, TExecutor, TResolverChain, TResult, TKey, TValue, TComparer>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
			TComparer comparer)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>
		where TDiscriminator : struct, IBaseFilterable
		where TResolverChain : struct, IResolvers
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		where TComparer : IComparer<TResult>
		where TKey : IEquatable<TKey> {
		var resolver = new SortResolver<TKey, TValue, TResult, TComparer>(comparer);
		return Unsafe.AsRef(in builder).AddResolver(
			new SortedQuery<TDiscriminator>(builder._discriminator),
			new Resolvers<TResolverChain, SortResolver<TKey, TValue, TResult, TComparer>>(builder._resolverChain, resolver));
	}
}

public static class CacheQueryBuilderCombinedExecuteJoinedExtensions {

	public static int Count<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).CountCoreJoined<TResult>();

	public static QueryResults<TResult> Execute<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreJoined<TResult>(false, false, skip, take);

	public static QueryResults<TResult> ExecuteCloned<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreJoined<TResult>(false, true, skip, take);

	public static QueryResults<TResult> ExecutePooled<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreJoined<TResult>(true, false, skip, take);

	public static QueryResults<TResult> ExecutePooledCloned<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreJoined<TResult>(true, true, skip, take);

	public static int Count<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, TResolverChain, TResult> builder)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).CountCoreJoined<TResult>();

	public static QueryResults<TResult> Execute<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreJoined<TResult>(false, false, skip, take);

	public static QueryResults<TResult> ExecuteCloned<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreJoined<TResult>(false, true, skip, take);

	public static QueryResults<TResult> ExecutePooled<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreJoined<TResult>(true, false, skip, take);

	public static QueryResults<TResult> ExecutePooledCloned<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, TResolverChain, TResult> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TResult : struct, IJoinResult<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreJoined<TResult>(true, true, skip, take);
}
public static class CacheQueryBuilderCombinedExecuteSimpleExtensions {
	// Non-join builders (TResult pinned to TValue): no narrowing to run, so Count reports
	// the same candidate count Execute would materialize — CountCoreSimple is correct here.
	public static int Count<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, Resolvers<TResolver>, TValue> builder)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).CountCoreSimple();

	public static int Count<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolver>, TValue> builder)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).CountCoreSimple();

	public static QueryResults<TValue> Execute<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue, Resolvers<TResolver>, TValue> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreSimple(ref builder._resolverChain.Resolver, false, false, skip, take);

	public static QueryResults<TValue> ExecuteCloned<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue,  Resolvers<TResolver>, TValue> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreSimple(ref builder._resolverChain.Resolver,false, true, skip, take);

	public static QueryResults<TValue> ExecutePooled<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue,  Resolvers<TResolver>, TValue> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreSimple(ref builder._resolverChain.Resolver,true, false, skip, take);

	public static QueryResults<TValue> ExecutePooledCloned<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<SortedQuery<TDiscriminator>, TExecutor, TKey, TValue,  Resolvers<TResolver>, TValue> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreSimple(ref builder._resolverChain.Resolver,true, true, skip, take);

	public static QueryResults<TValue> Execute<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolver>, TValue> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreSimple(ref builder._resolverChain.Resolver, false, false, skip, take);

	public static QueryResults<TValue> ExecuteCloned<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,  Resolvers<TResolver>, TValue> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreSimple(ref builder._resolverChain.Resolver,false, true, skip, take);

	public static QueryResults<TValue> ExecutePooled<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,  Resolvers<TResolver>, TValue> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreSimple(ref builder._resolverChain.Resolver,true, false, skip, take);

	public static QueryResults<TValue> ExecutePooledCloned<TDiscriminator, TExecutor, TKey, TValue, TResolver>(
		this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,  Resolvers<TResolver>, TValue> builder,
		int skip = 0, int take = int.MaxValue)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IExecutableQuery
		where TResolver : struct, IJoinResolver
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> Unsafe.AsRef(in builder).ExecuteCoreSimple(ref builder._resolverChain.Resolver,true, true, skip, take);
}


// public static class CacheQueryBuilderCombinedJoinLevel1Extensions {
//
// 	/// <summary>Join one-to-one via reverse index (level 1 → 2).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-one via forward FK selector (level 1 → 2).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-many via reverse list index (level 1 → 2).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, QueryResults<TRightValue>>>
// 		JoinMany<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueListIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new ManyResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, QueryResults<TRightValue>>>(
// 			new Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), true);
// 	}
//
// 	/// <summary>Inner join one-to-one via reverse index (level 1 → 2), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Inner join one-to-one via forward FK selector (level 1 → 2), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-one via left index + foreign key selector (level 1 → 2).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, TIndexKey, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1>> builder,
// 			CacheKeyValueIndex<TIndexKey, TKey, TKey> leftIndex,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TIndexKey, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TIndexKey : notnull
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>(rightCache, leftIndex, foreignKeySelector, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Inner join one-to-one via left index + foreign key selector (level 1 → 2), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, TIndexKey, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1>> builder,
// 			CacheKeyValueIndex<TIndexKey, TKey, TKey> leftIndex,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TIndexKey, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TIndexKey : notnull
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>(rightCache, leftIndex, foreignKeySelector, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
// }
// public static class CacheQueryBuilderCombinedJoinLevel2Extensions {
//
// 	/// <summary>Join one-to-one via reverse index (level 2 → 3).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-one via forward FK selector (level 2 → 3).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-many via reverse list index (level 2 → 3).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, QueryResults<TRightValue>>>
// 		JoinMany<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueListIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new ManyResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, QueryResults<TRightValue>>>(
// 			new Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), true);
// 	}
//
// 	/// <summary>Inner join one-to-one via reverse index (level 2 → 3), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Inner join one-to-one via forward FK selector (level 2 → 3), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-one via left index + foreign key selector (level 2 → 3).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, TIndexKey, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2>> builder,
// 			CacheKeyValueIndex<TIndexKey, TKey, TKey> leftIndex,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TIndexKey, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TIndexKey : notnull
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>(rightCache, leftIndex, foreignKeySelector, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Inner join one-to-one via left index + foreign key selector (level 2 → 3), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, TIndexKey, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2>> builder,
// 			CacheKeyValueIndex<TIndexKey, TKey, TKey> leftIndex,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TIndexKey, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TIndexKey : notnull
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>(rightCache, leftIndex, foreignKeySelector, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
// }
// public static class CacheQueryBuilderCombinedJoinLevel3Extensions {
//
// 	/// <summary>Join one-to-one via reverse index (level 3 → 4).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-one via forward FK selector (level 3 → 4).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-many via reverse list index (level 3 → 4).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, QueryResults<TRightValue>>>
// 		JoinMany<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueListIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new ManyResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, QueryResults<TRightValue>>>(
// 			new Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), true);
// 	}
//
// 	/// <summary>Inner join one-to-one via reverse index (level 3 → 4), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Inner join one-to-one via forward FK selector (level 3 → 4), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-one via left index + foreign key selector (level 3 → 4).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, TIndexKey, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3>> builder,
// 			CacheKeyValueIndex<TIndexKey, TKey, TKey> leftIndex,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TIndexKey, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TIndexKey : notnull
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>(rightCache, leftIndex, foreignKeySelector, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Inner join one-to-one via left index + foreign key selector (level 3 → 4), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, TIndexKey, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3>> builder,
// 			CacheKeyValueIndex<TIndexKey, TKey, TKey> leftIndex,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TIndexKey, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TIndexKey : notnull
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>(rightCache, leftIndex, foreignKeySelector, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
// }
// public static class CacheQueryBuilderCombinedJoinLevel4Extensions {
//
// 	/// <summary>Join one-to-one via reverse index (level 4 → 5).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, T4, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3, T4>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-one via forward FK selector (level 4 → 5).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, T4, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3, T4>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-many via reverse list index (level 4 → 5).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, QueryResults<TRightValue>>>
// 		JoinMany<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, T4, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3, T4>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueListIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new ManyResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, QueryResults<TRightValue>>>(
// 			new Resolvers<TResolverChain, ManyResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), true);
// 	}
//
// 	/// <summary>Inner join one-to-one via reverse index (level 4 → 5), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, T4, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3, T4>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			CacheKeyValueIndex<TRightKey, TRightValue, TKey> rightIndex,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, rightIndex, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Inner join one-to-one via forward FK selector (level 4 → 5), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, T4, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3, T4>> builder,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TValue, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TRightKey, TRightValue>(rightCache, foreignKeySelector, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Join one-to-one via left index + foreign key selector (level 4 → 5).</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>
// 		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, T4, TIndexKey, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3, T4>> builder,
// 			CacheKeyValueIndex<TIndexKey, TKey, TKey> leftIndex,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TIndexKey, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IBaseJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TIndexKey : notnull
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>(rightCache, leftIndex, foreignKeySelector, rightQueryFilter);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
//
// 	/// <summary>Inner join one-to-one via left index + foreign key selector (level 4 → 5), filtering out nulls.</summary>
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static CacheQueryBuilderCombined
// 		<TDiscriminator, TExecutor, TKey, TValue, Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>
// 		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, T1, T2, T3, T4, TIndexKey, TRightKey, TRightValue>(
// 			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, JoinResult<TValue, T1, T2, T3, T4>> builder,
// 			CacheKeyValueIndex<TIndexKey, TKey, TKey> leftIndex,
// 			InMemoryDataCache<TRightKey, TRightValue> rightCache,
// 			Func<TIndexKey, TRightKey> foreignKeySelector,
// 			Func<JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>, JoinedCacheQueryBuilder<TKey, TRightKey, TRightValue>>? rightQueryFilter = null)
// 		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
// 		where TDiscriminator : struct, IInnerJoinable
// 		where TResolverChain : struct, IResolvers
// 		where TKey : notnull, IEquatable<TKey>
// 		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
// 		where TIndexKey : notnull
// 		where TRightKey : notnull, IEquatable<TRightKey>
// 		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue> {
// 		var resolver = new JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>(rightCache, leftIndex, foreignKeySelector, rightQueryFilter, inner: true);
// 		return Unsafe.AsRef(in builder).AddResolver<Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>, JoinResult<TValue, T1, T2, T3, T4, TRightValue?>>(
// 			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TIndexKey, TRightKey, TRightValue>>(builder._resolverChain, resolver), false);
// 	}
// }

/// <summary>
/// Extension method that promotes an unpaired <see cref="CacheQueryBuilderCombined{TDiscriminator,TLeftQuery,TLeftKey,TLeftValue,TResolverChain,TResult}"/>
/// to a paired one by seeding the candidate set from a symmetric index.
/// After promotion all subsequent <c>UseIndex</c> calls are intersect-only.
/// </summary>
public static class CacheQueryBuilderCombinedUseIndexAsPairsExtensions {

	/// <summary>
	/// Seeds paired candidates from <paramref name="symIndex"/> for the given
	/// <paramref name="lookupValues"/> and returns a new builder whose executor is
	/// <see cref="PairedCacheQueryBuilderCoreCombined{TLeft,TKey,TValue}"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<
		TDiscriminator,
		PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>,
		TKey, TValue, TResolverChain, TResult>
		UseIndexAsPairs<TDiscriminator, TLeft, TResolverChain, TResult, TKey, TValue>(
			this in CacheQueryBuilderCombined<
				TDiscriminator,
				CacheQueryBuilderCoreCombined<TKey, TValue>,
				TKey, TValue, TResolverChain, TResult> builder,
			CacheSymmetricKeyValueListIndex<TKey, TValue, TLeft> symIndex,
			ReadOnlySpan<TLeft> lookupValues)
		where TDiscriminator : struct, IIndexNarrower
		where TResolverChain : struct, IResolvers
		where TLeft : notnull, IEquatable<TLeft>
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {

		// Seed the paired candidate set from the symmetric index.
		var pairs = new ValueSet<JoinedKeyPair<TLeft, TKey>, DefaultKeyComparer<JoinedKeyPair<TLeft, TKey>>>();
		symIndex.IntersectValues(lookupValues, ref pairs, add: true);

		// Construct the paired core with the seeded candidates.
		var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>(
			builder._leftQuery._dataCache, pairs);

		// Return a new combined builder with TExecutor switched to the paired core.
		return new CacheQueryBuilderCombined<
			TDiscriminator,
			PairedCacheQueryBuilderCoreCombined<TLeft, TKey, TValue>,
			TKey, TValue, TResolverChain, TResult>(
			builder._discriminator,
			pairedCore,
			builder._resolverChain,
			0);
	}
}

#endregion
