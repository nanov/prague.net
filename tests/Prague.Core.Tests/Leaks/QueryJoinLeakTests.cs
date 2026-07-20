namespace Prague.Core.Tests.Leaks;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using NUnit.Framework;

// ── Domain model (hand-rolled, no codegen — same pattern as Join core tests) ──

internal sealed class LeakLeft : ICacheEquatable<LeakLeft>, ICacheClonable<LeakLeft> {
	public int Id { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(LeakLeft? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public LeakLeft Clone() => new() { Id = Id, Name = Name };
}

internal sealed class LeakRight : ICacheEquatable<LeakRight>, ICacheClonable<LeakRight> {
	public int Id { get; init; }
	public int LeftId { get; init; }
	public string Status { get; init; } = "";

	public bool CacheEquals(LeakRight? other) => other is not null && other.Id == Id && other.LeftId == LeftId && other.Status == Status;
	public int CacheGetHashCode() => HashCode.Combine(Id, LeftId, Status);
	public LeakRight Clone() => new() { Id = Id, LeftId = LeftId, Status = Status };
}

// Every scenario must come back to zero new outstanding pool arrays and zero
// double-returns, on happy paths, empty-result paths, Count paths, and paths where a
// user-supplied predicate or join filter throws mid-pipeline. Caches are built once
// per fixture (InMemoryDataCache has no Dispose — cache-internal index structures are
// process-lifetime) so only per-query rentals show up in the delta.
[TestFixture]
[NonParallelizable]
public class QueryJoinLeakTests {
	private const int Size = 500;

	private InMemoryDataCache<int, LeakLeft> _left = null!;
	private InMemoryDataCache<int, LeakRight> _right = null!;
	private InMemoryDataCache<int, LeakRight> _rightHalf = null!;
	private InMemoryDataCache<int, LeakRight> _rightEmpty = null!;
	private InMemoryDataCache<int, LeakRight> _rightMany = null!;
	private CacheKeyValueListIndex<int, LeakRight, int> _manyIndex = null!;
	private CacheUniqueIndex<int, LeakRight, int> _rightHalfUniqueIndex = null!;

	[OneTimeSetUp]
	public void BuildCaches() {
		_left = new InMemoryDataCache<int, LeakLeft>();
		for (var i = 0; i < Size; i++)
			_left.AddOrUpdate(i, new LeakLeft { Id = i, Name = $"L{i}" });

		_right = new InMemoryDataCache<int, LeakRight>();
		for (var i = 0; i < Size; i++)
			_right.AddOrUpdate(i, new LeakRight { Id = i, LeftId = i, Status = $"R{i}" });

		_rightHalf = new InMemoryDataCache<int, LeakRight>();
		_rightHalfUniqueIndex = _rightHalf.AddKeyValueIndex<int>(static (_, v) => v.LeftId);
		for (var i = 0; i < Size / 2; i++)
			_rightHalf.AddOrUpdate(i, new LeakRight { Id = i, LeftId = i, Status = $"R{i}" });

		_rightEmpty = new InMemoryDataCache<int, LeakRight>();

		_rightMany = new InMemoryDataCache<int, LeakRight>();
		_manyIndex = _rightMany.CacheKeyValueListIndex<int>(static (_, v) => v.LeftId);
		for (var i = 0; i < 400; i++)
			_rightMany.AddOrUpdate(i, new LeakRight { Id = i, LeftId = i % 50, Status = $"R{i}" });
	}

	// ── Simple (un-joined) pooled queries ────────────────────────────────────

	[Test]
	public void SimplePooled_HappyPath_DisposeReturnsBuffer() =>
		LeakAssert.Balanced(() => {
			using var results = _left.Query().Where(static l => l.Id < 100).ExecutePooled();
			Assert.That(results.Count, Is.EqualTo(100));
		});

	[Test]
	public void SimplePooled_AllRowsFiltered_EmptyResult_DoesNotLeakBuffer() =>
		LeakAssert.Balanced(() => {
			using var results = _left.Query().Where(static _ => false).ExecutePooled();
			Assert.That(results.Count, Is.Zero);
		});

	[Test]
	public void SimplePooled_ThrowingPredicate_DoesNotLeakBuffer() =>
		LeakAssert.Balanced(() => {
			try {
				_left.Query().Where(static l => l.Id < 10 ? true : throw new InvalidOperationException("hostile predicate")).ExecutePooled();
				Assert.Fail("predicate must throw");
			} catch (InvalidOperationException) {
			}
		});

	[Test]
	public void SimplePooled_DoubleDispose_NoDoubleReturn() =>
		LeakAssert.Balanced(() => {
			var results = _left.Query().ExecutePooled();
			results.Dispose();
			results.Dispose();
		});

	// ── Joined pooled queries ────────────────────────────────────────────────

	[Test]
	public void JoinOnePooled_HappyPath_Balanced() =>
		LeakAssert.Balanced(() => {
			using var results = _left.Query().JoinOne(_right).ExecutePooled();
			Assert.That(results.Count, Is.EqualTo(Size));
		});

	[Test]
	public void InnerJoinOnePooled_NoMatches_EmptyResult_DoesNotLeakValuesArray() =>
		LeakAssert.Balanced(() => {
			using var results = _left.Query().InnerJoinOne(_rightEmpty).ExecutePooled();
			Assert.That(results.Count, Is.Zero);
		});

	// Indexed-inner: only this family goes through PrepareIndexedInner's dictionary init,
	// which is what rents the pooled values array on the Count() path. Count() reports the
	// LEFT candidate count — inner-join narrowing happens at Execute, not Count — so this
	// test pins pool balance, not join semantics.
	[Test]
	public void InnerJoinOne_Indexed_Count_DoesNotLeakValuesArray() =>
		LeakAssert.Balanced(() => {
			var count = _left.Query().InnerJoinOne(_rightHalf, _rightHalfUniqueIndex).Count();
			Assert.That(count, Is.EqualTo(Size));
		});

	[Test]
	public void InnerJoinOne_Indexed_Pooled_HappyPath_Balanced() =>
		LeakAssert.Balanced(() => {
			using var results = _left.Query().InnerJoinOne(_rightHalf, _rightHalfUniqueIndex).ExecutePooled();
			Assert.That(results.Count, Is.EqualTo(Size / 2));
		});

	[Test]
	public void JoinOnePooled_ThrowingFilter_DoesNotLeak() =>
		LeakAssert.Balanced(() => {
			try {
				_left.Query().JoinOne(_right, static _ => throw new InvalidOperationException("hostile filter")).ExecutePooled();
				Assert.Fail("filter must throw");
			} catch (InvalidOperationException) {
			}
		});

	[Test]
	public void JoinedPooled_ThrowingWhere_DoesNotLeak() =>
		LeakAssert.Balanced(() => {
			try {
				_left.Query()
					.Where(static l => l.Id < 10 ? true : throw new InvalidOperationException("hostile predicate"))
					.JoinOne(_right)
					.ExecutePooled();
				Assert.Fail("predicate must throw");
			} catch (InvalidOperationException) {
			}
		});

	// ── JoinMany (right list index) pooled queries ───────────────────────────

	[Test]
	public void JoinManyPooled_HappyPath_Balanced() =>
		LeakAssert.Balanced(() => {
			using var results = _left.Query().Where(static l => l.Id < 50).JoinMany(_rightMany, _manyIndex).ExecutePooled();
			Assert.That(results.Count, Is.EqualTo(50));
		});

	[Test]
	public void JoinManyPooled_ThrowingFilter_DoesNotLeakSharedBuffer() =>
		LeakAssert.Balanced(() => {
			try {
				_left.Query().Where(static l => l.Id < 50).JoinMany(_rightMany, _manyIndex, static _ => throw new InvalidOperationException("hostile filter")).ExecutePooled();
				Assert.Fail("filter must throw");
			} catch (InvalidOperationException) {
			}
		});

	[Test]
	public void JoinManyPooled_DoubleDispose_NoDoubleReturnOfChildBuffers() =>
		LeakAssert.Balanced(() => {
			var results = _left.Query().Where(static l => l.Id < 50).JoinMany(_rightMany, _manyIndex).ExecutePooled();
			results.Dispose();
			results.Dispose();
		});
}
