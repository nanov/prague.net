namespace Prague.Core;

using System.Runtime.CompilerServices;

/// <summary>
///   One reusable binding of a parameterized predicate to one execution's arguments. The
///   <see cref="Predicate{T}" /> the eager core's filter slot takes is created once, here, and closes
///   over the box rather than over <c>args</c>; rebinding the box for a later execution is two field
///   writes and no allocation. Boxes are owned by <see cref="ArgPredicatePool{TValue,TArgs}" /> and
///   are only ever touched by the thread that rented them.
/// </summary>
internal sealed class ArgPredicate<TValue, TArgs> {
	private Func<TValue, TArgs, bool>? _func;
	private TArgs _args;
	private FusedFilter<TValue, TArgs>? _fused;
	private FusedOrdering<TValue, TArgs>? _ordering;

	internal readonly Predicate<TValue> Predicate;

	// The fused-filter bindings (BuildFrozen stage 2): the box carries the execution's arguments plus
	// the order snapshot the whole execution evaluates under; the sampled twin records statistics.
	internal readonly Predicate<TValue> FusedPredicate;
	internal readonly Predicate<TValue> FusedSampledPredicate;

	internal ArgPredicate() {
		_args = default!;
		Predicate = Invoke;
		FusedPredicate = InvokeFused;
		FusedSampledPredicate = InvokeFusedSampled;
	}

	private bool Invoke(TValue value) => _func!(value, _args);

	private bool InvokeFused(TValue value) => FusedFilter<TValue, TArgs>.Passes(value, in _args, _ordering!);

	private bool InvokeFusedSampled(TValue value) => _fused!.PassesSampled(value, in _args, _ordering!);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void Bind(Func<TValue, TArgs, bool> func, in TArgs args) {
		_func = func;
		_args = args;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void BindFused(FusedFilter<TValue, TArgs> fused, FusedOrdering<TValue, TArgs> ordering, in TArgs args) {
		_fused = fused;
		_ordering = ordering;
		_args = args;
	}

	// Clearing on pop is GC hygiene: TArgs may hold references (a string, a nested command) that
	// must not stay rooted by a thread-static box past the execution that used them.
	internal void Clear() {
		_func = null;
		_fused = null;
		_ordering = null;
		_args = default!;
	}

	internal Func<TValue, TArgs, bool>? FuncForTests => _func;

	internal TArgs ArgsForTests => _args;
}

/// <summary>
///   Per-thread stack of <see cref="ArgPredicate{TValue,TArgs}" /> boxes, one pool per closed
///   (value, args) pair. A prepared execution takes a <see cref="Mark" /> before replay, every
///   parameterized filter in the chain <see cref="Rent" />s the next box (pushing it), and the
///   execution <see cref="Reset" />s to its mark once the eager core is done with the predicates —
///   after <c>Execute</c> / <c>Count</c>, not after replay, because the core applies its filter during
///   execution (and during <c>GetCandidates</c> for inner joins). Stack discipline is what makes the
///   pool re-entrancy-safe: a predicate whose body executes another prepared query on the same
///   thread pushes and pops strictly above the outer frame, so the outer boxes are never rebound
///   underneath the outer execution. Thread-static storage is what makes it thread-safe: one
///   command executed concurrently from many threads binds many boxes, one per thread.
/// </summary>
internal static class ArgPredicatePool<TValue, TArgs> {
	private const int InitialCapacity = 4;

	[ThreadStatic] private static ArgPredicate<TValue, TArgs>[]? _boxes;
	[ThreadStatic] private static int _depth;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int Mark() => _depth;

	/// <summary>Binds the next free box on this thread and returns its predicate, valid until the enclosing <see cref="Reset" />.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static Predicate<TValue> Rent(Func<TValue, TArgs, bool> func, in TArgs args) {
		var depth = _depth;
		var boxes = _boxes;
		if (boxes is null || (uint)depth >= (uint)boxes.Length)
			boxes = Grow(boxes);
		var box = boxes[depth];
		box.Bind(func, in args);
		_depth = depth + 1;
		return box.Predicate;
	}

	/// <summary>
	///   Binds the next free box to a fused filter, its current ordering and this execution's arguments —
	///   one box for every parameterized filter in the plan instead of one per filter — and returns
	///   the sampled or unsampled predicate.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static Predicate<TValue> RentFused(FusedFilter<TValue, TArgs> fused, FusedOrdering<TValue, TArgs> ordering, bool sampled, in TArgs args) {
		var depth = _depth;
		var boxes = _boxes;
		if (boxes is null || (uint)depth >= (uint)boxes.Length)
			boxes = Grow(boxes);
		var box = boxes[depth];
		box.BindFused(fused, ordering, in args);
		_depth = depth + 1;
		return sampled ? box.FusedSampledPredicate : box.FusedPredicate;
	}

	/// <summary>Pops every box rented since <paramref name="mark" /> and clears what it was bound to.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void Reset(int mark) {
		var depth = _depth;
		if (depth == mark)
			return;
		Pop(mark, depth);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Pop(int mark, int depth) {
		var boxes = _boxes!;
		for (var i = mark; i < depth; i++)
			boxes[i].Clear();
		_depth = mark;
	}

	// Boxes are created eagerly on growth so the rent path never null-checks a slot; the array only
	// ever grows (a thread's deepest nesting), and the boxes it holds are reused for the thread's life.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ArgPredicate<TValue, TArgs>[] Grow(ArgPredicate<TValue, TArgs>[]? boxes) {
		var grown = new ArgPredicate<TValue, TArgs>[boxes is null ? InitialCapacity : boxes.Length * 2];
		var i = 0;
		if (boxes is not null) {
			boxes.CopyTo(grown, 0);
			i = boxes.Length;
		}

		for (; i < grown.Length; i++)
			grown[i] = new();
		_boxes = grown;
		return grown;
	}

	internal static int DepthForTests => _depth;

	internal static ArgPredicate<TValue, TArgs>? BoxForTests(int index) {
		var boxes = _boxes;
		return boxes is not null && index < boxes.Length ? boxes[index] : null;
	}
}
