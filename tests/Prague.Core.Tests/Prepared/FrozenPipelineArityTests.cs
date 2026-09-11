namespace Prague.Core.Tests.Prepared;

using Prague.Core;
using static PreparedQueryJoinDifferentialTests;

// The joined pipeline's fill walk indexes one advisory hint per chain position, and the generated
// FillFused walks positions 1..JoinResultLimits.MaxJoinResults. JoinChainShape.MaxPositions sizes that
// array, and it read 8 against a 15-position walk: a chain of more than seven joins read past the array
// once per execution. Pinned here: the bound tracks the generated arity, and a chain that reports a
// position past it is refused by Accepts (→ the replay) instead of indexing out of range.
[TestFixture]
public class FrozenPipelineArityTests {
	private const int MaxPositions = 16;

	[Test]
	public void MaxPositions_TracksTheGeneratedJoinArity() {
		Assert.That(JoinChainShape<PqOrder>.MaxPositions, Is.EqualTo(JoinResultLimits.MaxJoinResults + 1),
			"the hint array must cover every position the generated FillFused walks");
		Assert.That(JoinChainShape<PqOrder>.MaxPositions, Is.EqualTo(MaxPositions), "MaxJoinResults changed: re-run T4 and re-check the walk");
	}

	// A chain of fusable joins filling every position the hint array has is admitted; one join deeper is
	// refused. The guard is what keeps raising MaxJoinResults without re-running T4 a lost plan, not a throw.
	[TestCase(1, true)]
	[TestCase(MaxPositions - 2, true)]
	[TestCase(MaxPositions - 1, true)]
	[TestCase(MaxPositions, false)]
	[TestCase(MaxPositions + 4, false)]
	public void Accepts_RefusesAChainDeeperThanTheHintArray(int joins, bool expected) {
		var accepted = PipelineJoinedExecutor<int, PqOrder, int, FixedDepthChain, JoinResult<PqOrder, PqCustomer?>>
			.Accepts(new FixedDepthChain(joins), fuseRegroupingInner: true, out var shape);
		Assert.That(accepted, Is.EqualTo(expected), $"{joins} joins");
		Assert.That(shape.MaxPosition, Is.EqualTo(joins), "the shape reports the deepest position it saw");
	}

	/// <summary>A chain of <see cref="Joins" /> outer fusable joins at positions 1..n, position 0 the left slot.</summary>
	private readonly struct FixedDepthChain(int joins) : IResolvers {
		private readonly int _joins = joins;

		public int Execute<TExecutor>(ref TExecutor executor) where TExecutor : struct, IResolverExecutor, allows ref struct {
			var resolver = new FusableStub();
			for (var position = 0; position <= _joins; position++)
				executor.Process(position, ref resolver);
			return _joins;
		}

		public static int Clone<TFullResult>(ref TFullResult fullResult) where TFullResult : struct, IJoinResult => 0;
	}

	/// <summary>A resolver that looks fusable to the shape walk and is never executed.</summary>
	private struct FusableStub : IJoinResolver {
		public static bool IsSorter => false;
		public static bool SupportsFusedLookup => true;
		public bool Inner => false;
		public bool CanFuse => true;

		public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult { }

		void IJoinResolver.UnsafeExecuteWithAccessor<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer)
			=> throw new NotSupportedException();

		void IJoinResolver.UnsafeExecuteIndexedInner<TAccessor, TExecutor>(ref TAccessor accessor, ref TExecutor leftQuery, bool cloneOnAdd, bool isFirst, ref QueryResultsDisposer disposer)
			=> throw new NotSupportedException();

		void IJoinResolver.PrepareIndexedInner<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool, ref QueryResultsDisposer disposer)
			=> throw new NotSupportedException();
	}
}
