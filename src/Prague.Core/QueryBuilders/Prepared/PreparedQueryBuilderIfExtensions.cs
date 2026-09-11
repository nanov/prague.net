namespace Prague.Core;

using TypeSystem;

/// <summary>
///   <c>If</c> / <c>IfElse</c> on the prepared builder: conditional narrowing decided per execution
///   from the arguments, the prepared twin of an eager query built with a C# <c>if</c> around a
///   type-preserving <c>UseIndex</c> / <c>Where</c> reassignment. The branch lambda receives a fresh
///   prepared branch builder over the same cache, runs once here, and the chain it returns is frozen
///   into the plan; the returned builder keeps the enclosing discriminator and resolver chain because
///   a conditional never changes the query's shape.
///   <para>
///   <b>Both verbs are sugar over the guard form of <c>Match</c></b> (<c>Narrowers.Match.Guard.cs</c>):
///   <c>If(c, b)</c> is <c>Match(m =&gt; m.Case(c, b).Default())</c> and <c>IfElse(c, t, e)</c> is
///   <c>Match(m =&gt; m.Case(c, t).Default(e))</c> — one <c>Case</c> whose guard is the condition, and the
///   <c>Default</c> the arm chain must end in. There is no <c>If</c> narrower, no <c>If</c> selector and
///   no "bind nothing" arm: a skipped <c>If</c> takes its empty <c>Default</c>, which has nothing to
///   replay and no leaf to bind, so it costs what falling through used to. Writing <c>Match</c> with two
///   or more <c>Case</c>s is the <c>else if</c> chain these two cannot spell.
///   </para>
///   <para>
///   Two branch families, selected by the receiver's discriminator. On a top-level builder or inside
///   another conditional the branch is discriminated by <see cref="PreparedConditionalBranch{TCache}" />,
///   so <c>UseIndex</c>, <c>Where</c>, <c>Or</c> and a nested <c>If</c> bind. Inside an <c>Or</c> branch
///   (<see cref="PreparedNarrowOnly{TCache}" />) the conditional branch is itself narrow-only: an
///   <c>Or</c> branch may never filter, conditionally or not.
///   </para>
///   <para>
///   The branch discriminator carries the enclosing query's <c>TCache</c> and carrier value, so the
///   generated <c>WithXxx</c> extensions bind inside a conditional branch exactly as at top level. C#
///   does not infer a type argument from a constraint, so each placement (top level, inside an
///   <c>If</c> branch, inside an <c>Or</c> branch) spells its receiver discriminator out; all forward to
///   one core per verb.
///   </para>
/// </summary>
public static class PreparedQueryBuilderIfExtensions {
	// ── Top-level receiver ───────────────────────────────────────────────────────

	/// <summary>Replays <paramref name="branch" /> only when <paramref name="condition" /> holds for the execution arguments; otherwise the empty <c>Default</c> arm, which narrows nothing.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain,
				GuardMatchNarrower<TKey, TValue, TArgs,
					DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TSub, TKey, TValue, TArgs>, EmptyNarrowers<TKey, TValue, TArgs>, TKey, TValue, TArgs, NoTag>>,
				TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		If<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TSub>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TSub>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> branch)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TSub : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct
		=> IfCore(in builder, PreparedBranchSeeds.Conditional<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), condition, branch);

	/// <summary>Replays <paramref name="then" /> when <paramref name="condition" /> holds for the execution arguments, <paramref name="otherwise" /> when it does not.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain,
				GuardMatchNarrower<TKey, TValue, TArgs,
					DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TThen, TKey, TValue, TArgs>, TElse, TKey, TValue, TArgs, NoTag>>,
				TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		IfElse<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TThen, TElse>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TThen>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> then,
			Func<
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TElse>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> otherwise)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TThen : struct, INarrowerChain<TKey, TValue, TArgs>
		where TElse : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct
		=> IfElseCore(in builder, PreparedBranchSeeds.Conditional<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), condition, then, otherwise);

	// ── Nested-conditional receiver ──────────────────────────────────────────────

	/// <summary>Nested conditional narrowing inside an <c>If</c> / <c>IfElse</c> branch or a <c>Match</c> arm.</summary>
	public static CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain,
				GuardMatchNarrower<TKey, TValue, TArgs,
					DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TSub, TKey, TValue, TArgs>, EmptyNarrowers<TKey, TValue, TArgs>, TKey, TValue, TArgs, NoTag>>,
				TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		If<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TSub>(
			this in CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TSub>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> branch)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TSub : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct
		=> IfCore(in builder, PreparedBranchSeeds.Conditional<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), condition, branch);

	/// <summary>Nested two-way conditional narrowing inside an <c>If</c> / <c>IfElse</c> branch or a <c>Match</c> arm.</summary>
	public static CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain,
				GuardMatchNarrower<TKey, TValue, TArgs,
					DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TThen, TKey, TValue, TArgs>, TElse, TKey, TValue, TArgs, NoTag>>,
				TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		IfElse<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TThen, TElse>(
			this in CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TThen>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> then,
			Func<
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TElse>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> otherwise)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TThen : struct, INarrowerChain<TKey, TValue, TArgs>
		where TElse : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct
		=> IfElseCore(in builder, PreparedBranchSeeds.Conditional<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), condition, then, otherwise);

	// ── Or-branch receiver (narrow-only) ─────────────────────────────────────────

	/// <summary>Conditional narrowing inside an <c>Or</c> branch: the branch is narrow-only, as the enclosing <c>Or</c> branch is.</summary>
	public static CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain,
				GuardMatchNarrower<TKey, TValue, TArgs,
					DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TSub, TKey, TValue, TArgs>, EmptyNarrowers<TKey, TValue, TArgs>, TKey, TValue, TArgs, NoTag>>,
				TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		If<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TSub>(
			this in CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TSub>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> branch)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TSub : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct
		=> IfCore(in builder, PreparedBranchSeeds.NarrowOnly<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), condition, branch);

	/// <summary>Two-way conditional narrowing inside an <c>Or</c> branch: both branches are narrow-only, as the enclosing <c>Or</c> branch is.</summary>
	public static CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain,
				GuardMatchNarrower<TKey, TValue, TArgs,
					DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TThen, TKey, TValue, TArgs>, TElse, TKey, TValue, TArgs, NoTag>>,
				TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		IfElse<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TThen, TElse>(
			this in CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TThen>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> then,
			Func<
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
					PreparedNarrowers<TKey, TValue, TArgs, TElse>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> otherwise)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TThen : struct, INarrowerChain<TKey, TValue, TArgs>
		where TElse : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct
		=> IfElseCore(in builder, PreparedBranchSeeds.NarrowOnly<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), condition, then, otherwise);

	// ── Cores ────────────────────────────────────────────────────────────────────
	//
	// The branch lambdas run once, at build time, against the empty seed recorder over the enclosing
	// query's cache; the resolver chain and result type only type the seed (a branch cannot join or
	// execute). What they record is then closed into the same one-Case guard arm chain the arm-recorder
	// lambda would have built — Case(condition, branch) then Default() / Default(otherwise) — and linked
	// as one GuardMatchNarrower. Built here rather than through MatchArmsBuilder because the arms are
	// known: two `new`s over values the caller already handed us, no recorder lambda to allocate.

	private static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain,
				GuardMatchNarrower<TKey, TValue, TArgs,
					DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TSub, TKey, TValue, TArgs>, EmptyNarrowers<TKey, TValue, TArgs>, TKey, TValue, TArgs, NoTag>>,
				TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		IfCore<TDiscriminator, TBranchDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TSub>(
			in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			in CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue> seed,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, TSub>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> branch)
		where TDiscriminator : struct, IIndexNarrower
		where TBranchDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TSub : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct {
		ArgumentNullException.ThrowIfNull(condition);
		ArgumentNullException.ThrowIfNull(branch);
		var sub = branch(seed)._leftQuery._chain;
		var guarded = new GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TSub, TKey, TValue, TArgs>(default, condition, in sub);
		var arms = new DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TSub, TKey, TValue, TArgs>,
			EmptyNarrowers<TKey, TValue, TArgs>, TKey, TValue, TArgs, NoTag>(in guarded, default);
		return PreparedQueryBuilderExtensions.Link(in builder,
			new GuardMatchNarrower<TKey, TValue, TArgs,
				DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TSub, TKey, TValue, TArgs>,
					EmptyNarrowers<TKey, TValue, TArgs>, TKey, TValue, TArgs, NoTag>>(in arms));
	}

	private static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain,
				GuardMatchNarrower<TKey, TValue, TArgs,
					DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TThen, TKey, TValue, TArgs>, TElse, TKey, TValue, TArgs, NoTag>>,
				TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		IfElseCore<TDiscriminator, TBranchDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TThen, TElse>(
			in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			in CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue> seed,
			Func<TArgs, bool> condition,
			Func<
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, TThen>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> then,
			Func<
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, TElse>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> otherwise)
		where TDiscriminator : struct, IIndexNarrower
		where TBranchDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TThen : struct, INarrowerChain<TKey, TValue, TArgs>
		where TElse : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct {
		ArgumentNullException.ThrowIfNull(condition);
		ArgumentNullException.ThrowIfNull(then);
		ArgumentNullException.ThrowIfNull(otherwise);
		var thenChain = then(seed)._leftQuery._chain;
		var elseChain = otherwise(seed)._leftQuery._chain;
		var guarded = new GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TThen, TKey, TValue, TArgs>(default, condition, in thenChain);
		var arms = new DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TThen, TKey, TValue, TArgs>,
			TElse, TKey, TValue, TArgs, NoTag>(in guarded, in elseChain);
		return PreparedQueryBuilderExtensions.Link(in builder,
			new GuardMatchNarrower<TKey, TValue, TArgs,
				DefaultArm<GuardArms<EmptyArms<TKey, TValue, TArgs, NoTag>, TThen, TKey, TValue, TArgs>, TElse, TKey, TValue, TArgs, NoTag>>(in arms));
	}
}
