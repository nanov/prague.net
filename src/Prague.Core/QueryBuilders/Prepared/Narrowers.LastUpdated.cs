namespace Prague.Core;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using QueryBuilders;

// The eager last-updated surface is 2 index kinds x 3 time types x {after, after..until}. Rather than
// twelve near-identical structs, each narrower is generic over the time type and dispatches on
// typeof(TTime): the JIT folds the comparisons per closed instantiation, so every Apply compiles to
// the one call the eager extension of that time type makes. TTime is only ever DateTime,
// DateTimeOffset or long — the public extensions are the sole constructors and fix it.

/// <summary><c>updatedAfter</c> against a global last-update index, bound at build time.</summary>
public readonly struct GlobalLastUpdatedAfter<TKey, TValue, TTime, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTime : struct {
	private readonly IDataCacheGlobalLastUpdateIndex<TKey> _index;
	private readonly TTime _after;

	public GlobalLastUpdatedAfter(IDataCacheGlobalLastUpdateIndex<TKey> index, TTime after) {
		_index = index;
		_after = after;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (typeof(TTime) == typeof(long)) core.UseIndexInternal(_index, Unsafe.BitCast<TTime, long>(_after));
		else if (typeof(TTime) == typeof(DateTime)) core.UseIndexInternal(_index, Unsafe.BitCast<TTime, DateTime>(_after));
		else if (typeof(TTime) == typeof(DateTimeOffset)) core.UseIndexInternal(_index, Unsafe.BitCast<TTime, DateTimeOffset>(_after));
		else LastUpdatedTime.ThrowUnsupported<TTime>();
	}

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.LastUpdatedAfter, _index, _after));
}

/// <summary><c>updatedAfter</c> .. <c>updatedUntilInclusive</c> against a global last-update index, bound at build time.</summary>
public readonly struct GlobalLastUpdatedBetween<TKey, TValue, TTime, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTime : struct {
	private readonly IDataCacheGlobalLastUpdateIndex<TKey> _index;
	private readonly TTime _after;
	private readonly TTime _untilInclusive;

	public GlobalLastUpdatedBetween(IDataCacheGlobalLastUpdateIndex<TKey> index, TTime after, TTime untilInclusive) {
		_index = index;
		_after = after;
		_untilInclusive = untilInclusive;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (typeof(TTime) == typeof(long))
			core.UseIndexInternal(_index, Unsafe.BitCast<TTime, long>(_after), Unsafe.BitCast<TTime, long>(_untilInclusive));
		else if (typeof(TTime) == typeof(DateTime))
			core.UseIndexInternal(_index, Unsafe.BitCast<TTime, DateTime>(_after), Unsafe.BitCast<TTime, DateTime>(_untilInclusive));
		else if (typeof(TTime) == typeof(DateTimeOffset))
			core.UseIndexInternal(_index, Unsafe.BitCast<TTime, DateTimeOffset>(_after), Unsafe.BitCast<TTime, DateTimeOffset>(_untilInclusive));
		else LastUpdatedTime.ThrowUnsupported<TTime>();
	}

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.LastUpdatedBetween, _index, (_after, _untilInclusive)));
}

/// <summary><c>updatedAfter</c> (unix ms) against a global last-update index, selected from the execution arguments.</summary>
public readonly struct GlobalLastUpdatedAfterArg<TKey, TValue, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly IDataCacheGlobalLastUpdateIndex<TKey> _index;
	private readonly Func<TArgs, long> _after;

	public GlobalLastUpdatedAfterArg(IDataCacheGlobalLastUpdateIndex<TKey> index, Func<TArgs, long> after) {
		_index = index;
		_after = after;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _after(args));

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.LastUpdatedAfter, _index, selector: _after, isParameterized: true));
}

/// <summary><c>updatedAfter</c> .. <c>updatedUntilInclusive</c> (unix ms) against a global last-update index, both selected from the execution arguments.</summary>
public readonly struct GlobalLastUpdatedBetweenArg<TKey, TValue, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly IDataCacheGlobalLastUpdateIndex<TKey> _index;
	private readonly Func<TArgs, long> _after;
	private readonly Func<TArgs, long> _untilInclusive;

	public GlobalLastUpdatedBetweenArg(IDataCacheGlobalLastUpdateIndex<TKey> index, Func<TArgs, long> after, Func<TArgs, long> untilInclusive) {
		_index = index;
		_after = after;
		_untilInclusive = untilInclusive;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _after(args), _untilInclusive(args));

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.LastUpdatedBetween, _index, selector: _after, isParameterized: true));
}

/// <summary><c>updatedAfter</c> against a raw <see cref="LastUpdatedIndex{TKey}" />, bound at build time.</summary>
public readonly struct LastUpdatedAfter<TKey, TValue, TTime, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTime : struct {
	private readonly LastUpdatedIndex<TKey> _index;
	private readonly TTime _after;

	public LastUpdatedAfter(LastUpdatedIndex<TKey> index, TTime after) {
		_index = index;
		_after = after;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (typeof(TTime) == typeof(long)) core.UseIndexInternal(_index, Unsafe.BitCast<TTime, long>(_after));
		else if (typeof(TTime) == typeof(DateTime)) core.UseIndexInternal(_index, Unsafe.BitCast<TTime, DateTime>(_after));
		else if (typeof(TTime) == typeof(DateTimeOffset)) core.UseIndexInternal(_index, Unsafe.BitCast<TTime, DateTimeOffset>(_after));
		else LastUpdatedTime.ThrowUnsupported<TTime>();
	}

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.LastUpdatedAfter, _index, _after));
}

/// <summary><c>updatedAfter</c> .. <c>updatedUntilInclusive</c> against a raw <see cref="LastUpdatedIndex{TKey}" />, bound at build time.</summary>
public readonly struct LastUpdatedBetween<TKey, TValue, TTime, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTime : struct {
	private readonly LastUpdatedIndex<TKey> _index;
	private readonly TTime _after;
	private readonly TTime _untilInclusive;

	public LastUpdatedBetween(LastUpdatedIndex<TKey> index, TTime after, TTime untilInclusive) {
		_index = index;
		_after = after;
		_untilInclusive = untilInclusive;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore> {
		if (typeof(TTime) == typeof(long))
			core.UseIndexInternal(_index, Unsafe.BitCast<TTime, long>(_after), Unsafe.BitCast<TTime, long>(_untilInclusive));
		else if (typeof(TTime) == typeof(DateTime))
			core.UseIndexInternal(_index, Unsafe.BitCast<TTime, DateTime>(_after), Unsafe.BitCast<TTime, DateTime>(_untilInclusive));
		else if (typeof(TTime) == typeof(DateTimeOffset))
			core.UseIndexInternal(_index, Unsafe.BitCast<TTime, DateTimeOffset>(_after), Unsafe.BitCast<TTime, DateTimeOffset>(_untilInclusive));
		else LastUpdatedTime.ThrowUnsupported<TTime>();
	}

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.LastUpdatedBetween, _index, (_after, _untilInclusive)));
}

/// <summary><c>updatedAfter</c> (unix ms) against a raw <see cref="LastUpdatedIndex{TKey}" />, selected from the execution arguments.</summary>
public readonly struct LastUpdatedAfterArg<TKey, TValue, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly LastUpdatedIndex<TKey> _index;
	private readonly Func<TArgs, long> _after;

	public LastUpdatedAfterArg(LastUpdatedIndex<TKey> index, Func<TArgs, long> after) {
		_index = index;
		_after = after;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _after(args));

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.LastUpdatedAfter, _index, selector: _after, isParameterized: true));
}

/// <summary><c>updatedAfter</c> .. <c>updatedUntilInclusive</c> (unix ms) against a raw <see cref="LastUpdatedIndex{TKey}" />, both selected from the execution arguments.</summary>
public readonly struct LastUpdatedBetweenArg<TKey, TValue, TArgs> : INarrower<TKey, TValue, TArgs>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	private readonly LastUpdatedIndex<TKey> _index;
	private readonly Func<TArgs, long> _after;
	private readonly Func<TArgs, long> _untilInclusive;

	public LastUpdatedBetweenArg(LastUpdatedIndex<TKey> index, Func<TArgs, long> after, Func<TArgs, long> untilInclusive) {
		_index = index;
		_after = after;
		_untilInclusive = untilInclusive;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Apply<TCore>(ref TCore core, in TArgs args) where TCore : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TCore>
		=> core.UseIndexInternal(_index, _after(args), _untilInclusive(args));

	public void Describe(List<NarrowerDescriptor> plan) => plan.Add(NarrowerDescriptor.ForIndex(NarrowerKind.LastUpdatedBetween, _index, selector: _after, isParameterized: true));
}

internal static class LastUpdatedTime {
	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void ThrowUnsupported<TTime>()
		=> throw new NotSupportedException("Last-updated narrowers accept DateTime, DateTimeOffset or long (unix ms); got " + typeof(TTime).Name + ".");
}
