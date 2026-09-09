namespace Prague.Core;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Collections;

/// <summary>
///   The left query of a prepared builder: a recorder, not an executor. It carries the narrower chain
///   and the argument type, and satisfies <see cref="ICandidatesExecutor{TKey,TValue}" /> only so the
///   existing join extensions (constrained on that interface alone) bind unchanged. Execution never
///   goes through these members — <c>Build()</c> replays the chain into a real
///   <see cref="CacheQueryBuilderCoreCombined{TKey,TValue}" /> — so they throw if ever reached.
///   It does not implement <see cref="ICandidatesFilterer{TKey,TValue}" />, which is what keeps the
///   eager <c>UseIndex</c> / <c>Where</c> overloads from binding on a prepared builder.
/// </summary>
public readonly struct PreparedNarrowers<TKey, TValue, TArgs, TChain> : ICandidatesExecutor<TKey, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TChain : struct, INarrowerChain<TKey, TValue, TArgs> {
	internal readonly InMemoryDataCache<TKey, TValue> _cache;
	internal readonly TChain _chain;

	public PreparedNarrowers(InMemoryDataCache<TKey, TValue> cache, in TChain chain) {
		_cache = cache;
		_chain = chain;
	}

	[DoesNotReturn]
	private static void ThrowNotExecutable()
		=> throw new InvalidOperationException("A prepared query is executed through Build(); the recorder itself never runs.");

	void ICandidatesExecutor<TKey, TValue>.ExecuteBase<TContainer>(ref TContainer c) => ThrowNotExecutable();

	int ICandidatesExecutor<TKey, TValue>.CountBase() {
		ThrowNotExecutable();
		return 0;
	}

	ref ValueSet<TKey, DefaultKeyComparer<TKey>> ICandidatesExecutor<TKey, TValue>.Candidates {
		get {
			ThrowNotExecutable();
			return ref Unsafe.NullRef<ValueSet<TKey, DefaultKeyComparer<TKey>>>();
		}
	}
}
