namespace Prague.Core;

// Why this exists instead of Func<TValue, TArgs, bool>. A prepared query's TArgs is unconstrained and
// routinely a tuple or a small record struct; a BCL Func takes it by value, so every invocation
// copies the whole struct. The internal plumbing (INarrower.Apply, IFrozenExecutor, PipelineCore,
// ArgPredicate) has always passed TArgs by `in` — this delegate carries that contract across the one
// boundary where the copy is paid per ROW rather than per execution: the arg Where predicate.
//
// The once-per-execution callbacks — the WithXxx / UseIndex selectors, If conditions, Match tags,
// last-updated instants, the range builder — stay plain Func on purpose. Their copy costs 1–2 ns
// once per execution, which no shape measured, and an `in` parameter is not free for callers: a
// plain lambda does not convert to one (CS1676), so every call site would have to be respelled.

/// <summary>
///   A row predicate parameterized by the execution arguments — the <c>Where((v, in a) =&gt; …)</c>
///   shape. Called once per candidate row, which is why <typeparamref name="TArgs" /> is passed by
///   <c>in</c>: a <see cref="Func{T1,T2,TResult}" /> here copies the whole struct per row.
/// </summary>
/// <remarks>
///   <para>
///   Callers must spell the modifier — <c>static (v, in a) =&gt; …</c> — because a plain lambda
///   parameter does not convert to an <c>in</c> delegate parameter (CS1676). The type is still
///   inferred. A stored delegate must be typed <c>ArgFilter&lt;TValue, TArgs&gt;</c>, not <c>Func</c>.
///   </para>
///   <para>
///   Prefer a <c>readonly struct</c> / <c>readonly record struct</c> for <typeparamref name="TArgs" />:
///   <c>in</c> removes the copy at the call, but reading a property of a <em>non-readonly</em> struct
///   through an <c>in</c> reference copies it again inside the lambda — exactly what this avoids.
///   Field access on a <see cref="ValueTuple" /> is free.
///   </para>
///   <para>
///   Named <c>ArgFilter</c> rather than <c>ArgPredicate</c>: the latter is taken by the internal
///   pooled binding box, <c>ArgPredicate&lt;TValue, TArgs&gt;</c>.
///   </para>
/// </remarks>
public delegate bool ArgFilter<in TValue, TArgs>(TValue value, in TArgs args)
	where TArgs : struct;
