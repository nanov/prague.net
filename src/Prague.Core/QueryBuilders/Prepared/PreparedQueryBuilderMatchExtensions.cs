namespace Prague.Core;

using TypeSystem;

/// <summary>
///   The arm recorder a <c>Match</c> lambda receives: the empty branch seed the arms are recorded
///   against (discriminated by the enclosing placement, so <c>UseIndex</c> / <c>Where</c> / <c>Or</c> /
///   <c>If</c> / nested <c>Match</c> bind inside an arm exactly as inside an <c>If</c> branch) and the
///   arms recorded so far as a type chain. <c>Case</c> / <c>Default</c> are extensions so their
///   <c>TArms : IOpenMatchArms</c> constraint can refuse an arm after <c>Default</c>; <c>Match</c>
///   in turn takes only a <c>TArms : IClosedMatchArms</c>, so the chain must reach a <c>Default</c>.
/// </summary>
public readonly struct MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag, TArms>
	where TBranchDiscriminator : struct, IIndexNarrower
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
	where TTag : notnull
	where TArms : struct, IMatchArms<TKey, TValue, TArgs, TTag>
	where TArgs : struct {
	internal readonly CacheQueryBuilderCombined<TBranchDiscriminator,
		PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue> _seed;
	internal readonly TArms _arms;

	internal MatchArmsBuilder(
		in CacheQueryBuilderCombined<TBranchDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue> seed,
		in TArms arms) {
		_seed = seed;
		_arms = arms;
	}
}

/// <summary>
///   <c>Match</c> on the prepared builder: tag-dispatched narrowing, the prepared twin of an eager
///   query built with a C# <c>switch</c> over type-preserving <c>UseIndex</c> / <c>Where</c>
///   reassignments. The selector runs per execution (one delegate call); the arm lambdas run once,
///   here, against a fresh branch seed, and the chains they return are frozen into one
///   <see cref="MatchNarrower{TKey,TValue,TArgs,TTag,TArms}" /> link. The first declared <c>Case</c>
///   whose tag equals the selected one replays; else the <c>Default</c>. A tag declared twice
///   resolves to its first arm.
///   <para>
///   <b>Every <c>Match</c> ends in a <c>Default</c>.</b> The arm lambda must return a chain closed by
///   one (<see cref="IClosedMatchArms{TKey,TValue,TArgs,TTag}" />), so an unmatched tag cannot fall
///   through to a silent no-op — an open chain, or a chain with no arm at all, does not compile.
///   Write <c>Default()</c> when the unmatched tag should narrow nothing; that is the same execution
///   as the old fall-through, said out loud.
///   </para>
///   <para>
///   <b>Two forms, one arm chain.</b> <c>Match(selector, arms)</c> dispatches on a tag; <c>Match(arms)</c>
///   — the guard form — takes no selector and gives each <c>Case</c> a <c>Func&lt;TArgs, bool&gt;</c>
///   instead, replaying the first arm whose guard holds (declaration order, later guards never called).
///   Both record into the same <see cref="IOpenMatchArms{TKey,TValue,TArgs,TTag}" /> chain and close on
///   the same <c>Default</c>; the guard form simply puts <see cref="NoTag" /> in the tag slot. <c>If</c>
///   and <c>IfElse</c> are the guard form with one <c>Case</c> and nothing else.
///   </para>
///   <para>
///   The arm rule is the <c>If</c> rule: <c>UseIndex</c> (every family), constant and parameterized
///   <c>Where</c>, nested <c>Or</c> / <c>If</c> / <c>Match</c>; joins, sorting and <c>Build()</c> never
///   bind inside an arm. Two receiver families as for <c>If</c>: on a top-level builder or inside a
///   conditional branch / arm the arms are <see cref="PreparedConditionalBranch{TCache}" /> builders;
///   inside an <c>Or</c> branch they are narrow-only (<see cref="PreparedNarrowOnly{TCache}" />), so an
///   arm cannot filter where the enclosing <c>Or</c> branch cannot.
///   </para>
/// </summary>
public static class PreparedQueryBuilderMatchExtensions {
	// ── Arms ─────────────────────────────────────────────────────────────────────

	/// <summary>Records the arm for <paramref name="tag" />. Arms are tried in declaration order; the first equal tag wins.</summary>
	public static MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag, MatchArms<TArms, TArm, TKey, TValue, TArgs, TTag>>
		Case<TBranchDiscriminator, TKey, TValue, TArgs, TTag, TArms, TArm>(
			this in MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag, TArms> arms,
			TTag tag,
			Func<
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, TArm>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> arm)
		where TBranchDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TTag : notnull
		where TArms : struct, IOpenMatchArms<TKey, TValue, TArgs, TTag>
		where TArm : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct {
		ArgumentNullException.ThrowIfNull(arm);
		var chain = arm(arms._seed)._leftQuery._chain;
		return new(in arms._seed, new MatchArms<TArms, TArm, TKey, TValue, TArgs, TTag>(in arms._arms, tag, in chain));
	}

	/// <summary>
	///   Records the arm for <paramref name="guard" /> — the guard form, where an arm names a predicate
	///   over the execution arguments rather than a tag. Guards are evaluated in declaration order and
	///   the first that holds wins; the later ones are never called, exactly as a C# <c>if / else if</c>
	///   chain short-circuits.
	/// </summary>
	public static MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, NoTag, GuardArms<TArms, TArm, TKey, TValue, TArgs>>
		Case<TBranchDiscriminator, TKey, TValue, TArgs, TArms, TArm>(
			this in MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, NoTag, TArms> arms,
			Func<TArgs, bool> guard,
			Func<
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, TArm>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> arm)
		where TBranchDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TArms : struct, IOpenMatchArms<TKey, TValue, TArgs, NoTag>
		where TArm : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct {
		ArgumentNullException.ThrowIfNull(guard);
		ArgumentNullException.ThrowIfNull(arm);
		var chain = arm(arms._seed)._leftQuery._chain;
		return new(in arms._seed, new GuardArms<TArms, TArm, TKey, TValue, TArgs>(in arms._arms, guard, in chain));
	}

	/// <summary>
	///   Records the arm replayed when no <c>Case</c> matched, and closes the chain — no <c>Case</c>
	///   binds after it, and only a closed chain is a <c>Match</c>, so every <c>Match</c> ends here.
	/// </summary>
	public static MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag, DefaultArm<TArms, TArm, TKey, TValue, TArgs, TTag>>
		Default<TBranchDiscriminator, TKey, TValue, TArgs, TTag, TArms, TArm>(
			this in MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag, TArms> arms,
			Func<
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>,
				CacheQueryBuilderCombined<TBranchDiscriminator,
					PreparedNarrowers<TKey, TValue, TArgs, TArm>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue>> arm)
		where TBranchDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TTag : notnull
		where TArms : struct, IOpenMatchArms<TKey, TValue, TArgs, TTag>
		where TArm : struct, INarrowerChain<TKey, TValue, TArgs>
		where TArgs : struct {
		ArgumentNullException.ThrowIfNull(arm);
		var chain = arm(arms._seed)._leftQuery._chain;
		return new(in arms._seed, new DefaultArm<TArms, TArm, TKey, TValue, TArgs, TTag>(in arms._arms, in chain));
	}

	/// <summary>
	///   Closes the chain with an empty default: the explicit "an unmatched tag narrows nothing"
	///   arm, <c>Default(static b =&gt; b)</c> without the lambda. The step is then a no-op for every
	///   tag no <c>Case</c> names — which is the old implicit behaviour, now written down.
	/// </summary>
	public static MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag,
			DefaultArm<TArms, EmptyNarrowers<TKey, TValue, TArgs>, TKey, TValue, TArgs, TTag>>
		Default<TBranchDiscriminator, TKey, TValue, TArgs, TTag, TArms>(
			this in MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag, TArms> arms)
		where TBranchDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TTag : notnull
		where TArms : struct, IOpenMatchArms<TKey, TValue, TArgs, TTag>
		where TArgs : struct {
		var chain = arms._seed._leftQuery._chain;
		return new(in arms._seed, new DefaultArm<TArms, EmptyNarrowers<TKey, TValue, TArgs>, TKey, TValue, TArgs, TTag>(in arms._arms, in chain));
	}

	// ── Top-level receiver ───────────────────────────────────────────────────────

	/// <summary>Replays the first arm whose tag equals <paramref name="selector" />'s result for the execution arguments, else the <c>Default</c> the arm chain must end in.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, MatchNarrower<TKey, TValue, TArgs, TTag, TArms>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Match<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TTag, TArms>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, TTag> selector,
			Func<
				MatchArmsBuilder<PreparedConditionalBranch<TCache>, TKey, TValue, TArgs, TTag, EmptyArms<TKey, TValue, TArgs, TTag>>,
				MatchArmsBuilder<PreparedConditionalBranch<TCache>, TKey, TValue, TArgs, TTag, TArms>> arms)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TTag : notnull
		where TArms : struct, IClosedMatchArms<TKey, TValue, TArgs, TTag>
		where TArgs : struct
		=> MatchCore(in builder, PreparedBranchSeeds.Conditional<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), selector, arms);

	// ── Nested receiver (inside an If branch or a Match arm) ─────────────────────

	/// <summary>Tag-dispatched narrowing inside an <c>If</c> / <c>IfElse</c> branch or a <c>Match</c> arm. The arm chain must end in a <c>Default</c>.</summary>
	public static CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, MatchNarrower<TKey, TValue, TArgs, TTag, TArms>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Match<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TTag, TArms>(
			this in CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, TTag> selector,
			Func<
				MatchArmsBuilder<PreparedConditionalBranch<TCache>, TKey, TValue, TArgs, TTag, EmptyArms<TKey, TValue, TArgs, TTag>>,
				MatchArmsBuilder<PreparedConditionalBranch<TCache>, TKey, TValue, TArgs, TTag, TArms>> arms)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TTag : notnull
		where TArms : struct, IClosedMatchArms<TKey, TValue, TArgs, TTag>
		where TArgs : struct
		=> MatchCore(in builder, PreparedBranchSeeds.Conditional<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), selector, arms);

	// ── Or-branch receiver (narrow-only arms) ────────────────────────────────────

	/// <summary>Tag-dispatched narrowing inside an <c>Or</c> branch: every arm is narrow-only, as the enclosing branch is, and the chain must end in a <c>Default</c>.</summary>
	public static CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, MatchNarrower<TKey, TValue, TArgs, TTag, TArms>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Match<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TTag, TArms>(
			this in CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<TArgs, TTag> selector,
			Func<
				MatchArmsBuilder<PreparedNarrowOnly<TCache>, TKey, TValue, TArgs, TTag, EmptyArms<TKey, TValue, TArgs, TTag>>,
				MatchArmsBuilder<PreparedNarrowOnly<TCache>, TKey, TValue, TArgs, TTag, TArms>> arms)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TTag : notnull
		where TArms : struct, IClosedMatchArms<TKey, TValue, TArgs, TTag>
		where TArgs : struct
		=> MatchCore(in builder, PreparedBranchSeeds.NarrowOnly<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), selector, arms);

	// ── Guard form: the same three receivers, no selector ────────────────────────

	/// <summary>
	///   Guard-form <c>Match</c>: no selector, one predicate per <c>Case</c>, and the first arm whose
	///   guard holds for the execution arguments replays — else the <c>Default</c> the arm chain must end
	///   in. The prepared twin of a C# <c>if / else if / else</c> chain over type-preserving
	///   reassignments; <c>If</c> and <c>IfElse</c> are this with one <c>Case</c>.
	/// </summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GuardMatchNarrower<TKey, TValue, TArgs, TArms>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Match<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TArms>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<
				MatchArmsBuilder<PreparedConditionalBranch<TCache>, TKey, TValue, TArgs, NoTag, EmptyArms<TKey, TValue, TArgs, NoTag>>,
				MatchArmsBuilder<PreparedConditionalBranch<TCache>, TKey, TValue, TArgs, NoTag, TArms>> arms)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TArms : struct, IClosedMatchArms<TKey, TValue, TArgs, NoTag>
		where TArgs : struct
		=> GuardMatchCore(in builder, PreparedBranchSeeds.Conditional<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), arms);

	/// <summary>Guard-dispatched narrowing inside an <c>If</c> / <c>IfElse</c> branch or a <c>Match</c> arm. The arm chain must end in a <c>Default</c>.</summary>
	public static CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GuardMatchNarrower<TKey, TValue, TArgs, TArms>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Match<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TArms>(
			this in CacheQueryBuilderCombined<PreparedConditionalBranch<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<
				MatchArmsBuilder<PreparedConditionalBranch<TCache>, TKey, TValue, TArgs, NoTag, EmptyArms<TKey, TValue, TArgs, NoTag>>,
				MatchArmsBuilder<PreparedConditionalBranch<TCache>, TKey, TValue, TArgs, NoTag, TArms>> arms)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TArms : struct, IClosedMatchArms<TKey, TValue, TArgs, NoTag>
		where TArgs : struct
		=> GuardMatchCore(in builder, PreparedBranchSeeds.Conditional<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), arms);

	/// <summary>Guard-dispatched narrowing inside an <c>Or</c> branch: every arm is narrow-only, as the enclosing branch is, and the chain must end in a <c>Default</c>.</summary>
	public static CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GuardMatchNarrower<TKey, TValue, TArgs, TArms>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Match<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TArms>(
			this in CacheQueryBuilderCombined<PreparedNarrowOnly<TCache>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Func<
				MatchArmsBuilder<PreparedNarrowOnly<TCache>, TKey, TValue, TArgs, NoTag, EmptyArms<TKey, TValue, TArgs, NoTag>>,
				MatchArmsBuilder<PreparedNarrowOnly<TCache>, TKey, TValue, TArgs, NoTag, TArms>> arms)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TArms : struct, IClosedMatchArms<TKey, TValue, TArgs, NoTag>
		where TArgs : struct
		=> GuardMatchCore(in builder, PreparedBranchSeeds.NarrowOnly<TCache, TKey, TValue, TArgs>(builder._discriminator.Cache, builder._leftQuery._cache), arms);

	// ── Core ─────────────────────────────────────────────────────────────────────
	//
	// The arms lambda runs once, at build time, against an arm recorder over the empty seed; every
	// Case / Default runs its arm lambda against that seed and appends the frozen chain to the arm
	// chain. TArms : IClosedMatchArms is what makes the Default mandatory — DefaultArm is its only
	// implementation, so a lambda that stops at a Case (or records no arm at all) does not bind here.

	private static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, MatchNarrower<TKey, TValue, TArgs, TTag, TArms>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		MatchCore<TDiscriminator, TBranchDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TTag, TArms>(
			in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			in CacheQueryBuilderCombined<TBranchDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue> seed,
			Func<TArgs, TTag> selector,
			Func<
				MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag, EmptyArms<TKey, TValue, TArgs, TTag>>,
				MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag, TArms>> arms)
		where TDiscriminator : struct, IIndexNarrower
		where TBranchDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TTag : notnull
		where TArms : struct, IClosedMatchArms<TKey, TValue, TArgs, TTag>
		where TArgs : struct {
		ArgumentNullException.ThrowIfNull(selector);
		ArgumentNullException.ThrowIfNull(arms);
		var recorded = arms(new MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, TTag, EmptyArms<TKey, TValue, TArgs, TTag>>(in seed, default))._arms;
		return PreparedQueryBuilderExtensions.Link(in builder, new MatchNarrower<TKey, TValue, TArgs, TTag, TArms>(selector, in recorded));
	}

	// The guard core: the same recording, with NoTag in the tag slot — a guard arm reads the arguments
	// itself, so the chain carries no selected value and there is no selector to call per execution.

	private static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GuardMatchNarrower<TKey, TValue, TArgs, TArms>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		GuardMatchCore<TDiscriminator, TBranchDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TArms>(
			in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			in CacheQueryBuilderCombined<TBranchDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue> seed,
			Func<
				MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, NoTag, EmptyArms<TKey, TValue, TArgs, NoTag>>,
				MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, NoTag, TArms>> arms)
		where TDiscriminator : struct, IIndexNarrower
		where TBranchDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TArms : struct, IClosedMatchArms<TKey, TValue, TArgs, NoTag>
		where TArgs : struct {
		ArgumentNullException.ThrowIfNull(arms);
		var recorded = arms(new MatchArmsBuilder<TBranchDiscriminator, TKey, TValue, TArgs, NoTag, EmptyArms<TKey, TValue, TArgs, NoTag>>(in seed, default))._arms;
		return PreparedQueryBuilderExtensions.Link(in builder, new GuardMatchNarrower<TKey, TValue, TArgs, TArms>(in recorded));
	}
}
