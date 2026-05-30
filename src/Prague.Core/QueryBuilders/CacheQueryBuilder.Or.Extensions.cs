namespace Prague.Core;

using System.Runtime.CompilerServices;
using TypeSystem;
using QueryBuilders;

// ── Or-branch strategy interface and structs ─────────────────────────────────

/// <summary>
/// Zero-virtual-dispatch branch strategy for <see cref="CacheQueryBuilderCombinedOrExtensions"/>.
/// The JIT devirtualizes <see cref="Apply"/> per closed generic — no virtual dispatch overhead.
/// </summary>
public interface IOrBranch<TBuilder> {
	TBuilder Apply(TBuilder b);
}

/// <summary>
/// Branch strategy that wraps a <see cref="Func{TBuilder,TBuilder}"/> and applies it at call time.
/// </summary>
public readonly struct OrBranch<TBuilder> : IOrBranch<TBuilder> {
	private readonly Func<TBuilder, TBuilder> _func;

	public OrBranch(Func<TBuilder, TBuilder> func) => _func = func;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TBuilder Apply(TBuilder b) => _func(b);
}

/// <summary>
/// Branch strategy that holds a <typeparamref name="TArg"/> user-state value alongside the branch
/// delegate. Pass a <c>static</c> lambda to guarantee zero closure allocation:
/// <code>.Or(static (b, s) => b.UseIndex(s.Idx, s.Val), ..., state)</code>
/// </summary>
public readonly struct OrBranchWithArg<TBuilder, TArg> : IOrBranch<TBuilder> {
	private readonly Func<TBuilder, TArg, TBuilder> _func;
	private readonly TArg _arg;

	public OrBranchWithArg(Func<TBuilder, TArg, TBuilder> func, TArg arg) {
		_func = func;
		_arg = arg;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TBuilder Apply(TBuilder b) => _func(b, _arg);
}

public static class CacheQueryBuilderCombinedOrExtensions {

	/// <summary>
	/// Applies an Or-clause to the combined builder when the discriminator is
	/// <see cref="ExecutableQuery{TCache}"/> (the common <c>cache.Query()…</c> entry point).
	/// <para>
	/// Semantics: <c>outer.UseIndex(A).Or(b1, b2)</c> produces
	/// <c>outer_candidates ∩ (b1_candidates ∪ b2_candidates)</c>.
	/// When no outer narrowing has happened yet the Or result becomes the sole
	/// candidate set (union only, no intersection).
	/// </para>
	/// <para>
	/// Branch builders share the same executor as the outer builder but start with
	/// empty candidates.  Inside each lambda only <c>UseIndex</c> / <c>WithXxx</c> /
	/// nested <c>Or</c> are reachable — <c>Execute*</c>, <c>Where</c>, <c>Sort</c>,
	/// and <c>Join</c> are gated behind markers that
	/// <see cref="NarrowOnlyQuery{TCache}"/> deliberately omits.
	/// </para>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<ExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>
		Or<TCache, TExecutor, TKey, TValue, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<ExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult> source,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b1,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b2)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TExecutor>
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {

		ref var outer = ref Unsafe.AsRef(in source);
		var narrowOnly = new NarrowOnlyQuery<TCache>(outer._discriminator.Cache);
		var br1 = new OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>(b1);
		var br2 = new OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>(b2);
		ref var lq = ref outer._leftQuery;
		lq.OrWith<
			TCache,
			TResolverChain,
			TResult,
			OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>>(
			narrowOnly,
			outer._resolverChain,
			br1,
			br2
			);
		// var narrowOnly = new NarrowOnlyQuery<TCache>(outer._discriminator.Cache);
		// var br1 = new OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>(b1);
		// var br2 = new OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>(b2);
		// ApplyOr<TCache, TExecutor, TKey, TValue, TResolverChain, TResult,
		// 	OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>,
		// 	OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>>(
		// 	ref outer._leftQuery, narrowOnly, outer._resolverChain, outer._manyCount, in br1, in br2);
		return source;
	}

	/// <summary>
	/// Applies an Or-clause inside a branch lambda where the discriminator is
	/// <see cref="NarrowOnlyQuery{TCache}"/> (nested Or support).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>
		Or<TCache, TExecutor, TKey, TValue, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult> source,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b1,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b2)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TExecutor>
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {

		ref var outer = ref Unsafe.AsRef(in source);
		var br1 = new OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>(b1);
		var br2 = new OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>(b2);
		ref var lq = ref outer._leftQuery;
		lq.OrWith<
			TCache,
			TResolverChain,
			TResult,
			OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>>(
			outer._discriminator,
			outer._resolverChain,
			br1,
			br2
		);
		// ApplyOr<TCache, TExecutor, TKey, TValue, TResolverChain, TResult,
		// 	OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>,
		// 	OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>>(
		// 	ref outer._leftQuery, outer._discriminator, outer._resolverChain, outer._manyCount, in br1, in br2);
		return source;
	}

	/// <summary>
	/// Applies an Or-clause inside a <c>JoinOneNew</c> filter callback where the
	/// discriminator is <see cref="NonExecutableQuery{TCache}"/>.
	/// Branch builders still use <see cref="NarrowOnlyQuery{TCache}"/> — only
	/// <c>UseIndex</c> / <c>WithXxx</c> / nested <c>Or</c> are reachable inside each lambda.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<NonExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>
		Or<TCache, TExecutor, TKey, TValue, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<NonExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult> source,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b1,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b2)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TExecutor>
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {

		ref var outer = ref Unsafe.AsRef(in source);
		var narrowOnly = new NarrowOnlyQuery<TCache>(outer._discriminator.Cache);
		var br1 = new OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>(b1);
		var br2 = new OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>(b2);
		ref var lq = ref outer._leftQuery;
		lq.OrWith<
			TCache,
			TResolverChain,
			TResult,
			OrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>>>(
			narrowOnly, outer._resolverChain, br1, br2);
		return source;
	}

	/// <summary>
	/// Applies an Or-clause to the combined builder when the discriminator is
	/// <see cref="ExecutableQuery{TCache}"/>, passing <paramref name="arg"/> to both
	/// branch lambdas for zero-allocation static-lambda usage.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<ExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>
		Or<TCache, TExecutor, TKey, TValue, TResolverChain, TResult, TArg>(
			this in CacheQueryBuilderCombined<ExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult> source,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				TArg,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b1,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				TArg,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b2,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TExecutor>
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {

		ref var outer = ref Unsafe.AsRef(in source);
		var narrowOnly = new NarrowOnlyQuery<TCache>(outer._discriminator.Cache);
		var br1 = new OrBranchWithArg<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg>(b1, arg);
		var br2 = new OrBranchWithArg<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg>(b2, arg);
		ref var lq = ref outer._leftQuery;
		lq.OrWith<
			TCache,
			TResolverChain,
			TResult,
			OrBranchWithArg<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg>>(
			narrowOnly, outer._resolverChain, br1, br2);
		return source;
	}

	/// <summary>
	/// Applies an Or-clause inside a branch lambda where the discriminator is
	/// <see cref="NarrowOnlyQuery{TCache}"/> (nested Or support), passing <paramref name="arg"/>
	/// to both branch lambdas for zero-allocation static-lambda usage.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>
		Or<TCache, TExecutor, TKey, TValue, TResolverChain, TResult, TArg>(
			this in CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult> source,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				TArg,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b1,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				TArg,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b2,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TExecutor>
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {

		ref var outer = ref Unsafe.AsRef(in source);
		var br1 = new OrBranchWithArg<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg>(b1, arg);
		var br2 = new OrBranchWithArg<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg>(b2, arg);
		ref var lq = ref outer._leftQuery;
		lq.OrWith<
			TCache,
			TResolverChain,
			TResult,
			OrBranchWithArg<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg>>(
			outer._discriminator, outer._resolverChain, br1, br2);
		return source;
	}

	/// <summary>
	/// Applies an Or-clause inside a <c>JoinOneNew</c> filter callback where the discriminator
	/// is <see cref="NonExecutableQuery{TCache}"/>, passing <paramref name="arg"/> to both
	/// branch lambdas for zero-allocation static-lambda usage.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<NonExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>
		Or<TCache, TExecutor, TKey, TValue, TResolverChain, TResult, TArg>(
			this in CacheQueryBuilderCombined<NonExecutableQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult> source,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				TArg,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b1,
			Func<
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>,
				TArg,
				CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>> b2,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>, ICandidatesFilterer<TKey, TValue>, IOrCapable<TKey, TValue, TExecutor>
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {

		ref var outer = ref Unsafe.AsRef(in source);
		var narrowOnly = new NarrowOnlyQuery<TCache>(outer._discriminator.Cache);
		var br1 = new OrBranchWithArg<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg>(b1, arg);
		var br2 = new OrBranchWithArg<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg>(b2, arg);
		ref var lq = ref outer._leftQuery;
		lq.OrWith<
			TCache,
			TResolverChain,
			TResult,
			OrBranchWithArg<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TExecutor, TKey, TValue, TResolverChain, TResult>, TArg>>(
			narrowOnly, outer._resolverChain, br1, br2);
		return source;
	}
}
