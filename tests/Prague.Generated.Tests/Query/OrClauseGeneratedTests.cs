namespace Prague.Generated.Tests.Query;

using Prague.Core;
using Prague.Tests.Models;
using NUnit.Framework;

/// <summary>
///   End-to-end tests for Or with codegen-emitted WithXxx extensions.
///   Verifies that the IIndexNarrower constraint migration (cfefcb0) makes
///   WithXxx callable inside Or branch lambdas whose discriminator is
///   NarrowOnlyQuery&lt;TCache&gt;.
/// </summary>
[TestFixture]
public class OrClauseGeneratedTests {
	private QueryParserTestModelCache _cache = null!;

	[SetUp]
	public void SetUp() {
		_cache = new QueryParserTestModelCache();

		// Id=1  Category=Electronics  UserId=100  Status=Active    Priority=High
		// Id=2  Category=Electronics  UserId=101  Status=Active    Priority=Medium
		// Id=3  Category=Books        UserId=100  Status=Inactive  Priority=Low
		// Id=4  Category=Electronics  UserId=102  Status=Pending   Priority=Critical
		// Id=5  Category=Books        UserId=103  Status=Active    Priority=High
		_cache.AddOrUpdate(new QueryParserTestModel {
			Id = 1, Category = "Electronics", UserId = 100,
			Status = TestStatus.Active, Priority = TestPriority.High,
			Name = "Item1", Description = "", Timestamp = 1000, Score = 10
		});
		_cache.AddOrUpdate(new QueryParserTestModel {
			Id = 2, Category = "Electronics", UserId = 101,
			Status = TestStatus.Active, Priority = TestPriority.Medium,
			Name = "Item2", Description = "", Timestamp = 2000, Score = 20
		});
		_cache.AddOrUpdate(new QueryParserTestModel {
			Id = 3, Category = "Books", UserId = 100,
			Status = TestStatus.Inactive, Priority = TestPriority.Low,
			Name = "Item3", Description = "", Timestamp = 3000, Score = 30
		});
		_cache.AddOrUpdate(new QueryParserTestModel {
			Id = 4, Category = "Electronics", UserId = 102,
			Status = TestStatus.Pending, Priority = TestPriority.Critical,
			Name = "Item4", Description = "", Timestamp = 4000, Score = 40
		});
		_cache.AddOrUpdate(new QueryParserTestModel {
			Id = 5, Category = "Books", UserId = 103,
			Status = TestStatus.Active, Priority = TestPriority.High,
			Name = "Item5", Description = "", Timestamp = 5000, Score = 50
		});
	}

	/// <summary>
	/// Outer WithCategory narrows to Electronics={1,2,4}.
	/// Or branches use WithUserId (codegen extension) inside NarrowOnlyQuery context.
	/// UserId=100 → {1,3}; UserId=101 → {2}.
	/// Expected: {1,2,4} ∩ ({1,3} ∪ {2}) = {1,2,4} ∩ {1,2,3} = {1,2}.
	/// </summary>
	[Test]
	public void Or_CodegenWithXxx_IntersectsOuterWithUnionOfBranches() {
		using var result = _cache.Query()
			.WithCategory("Electronics")
			.Or(
				b => b.WithUserId(100),
				b => b.WithUserId(101))
			.Execute();

		Assert.That(result.Select(x => x.Id).OrderBy(x => x).ToList(),
			Is.EqualTo(new[] { 1, 2 }));
	}

	/// <summary>
	/// No outer narrowing — Or alone returns union of both category branches.
	/// WithCategory("Electronics") → {1,2,4}; WithCategory("Books") → {3,5}.
	/// Expected union: {1,2,3,4,5}.
	/// </summary>
	[Test]
	public void Or_TwoCategoryBranches_ReturnsFullUnion() {
		using var result = _cache.Query()
			.Or(
				b => b.WithCategory("Electronics"),
				b => b.WithCategory("Books"))
			.Execute();

		Assert.That(result.Select(x => x.Id).OrderBy(x => x).ToList(),
			Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
	}

	/// <summary>
	/// Static-lambda TArg overload: zero-alloc closure alternative.
	/// Same intersection as the first test but dispatched via Or&lt;TArg&gt;.
	/// Expected: {1,2,4} ∩ ({1,3} ∪ {2}) = {1,2}.
	/// </summary>
	[Test]
	public void Or_TArg_StaticLambda_CodegenWithXxx_ProducesCorrectIntersection() {
		using var result = _cache.Query()
			.WithCategory("Electronics")
			.Or(
				static (b, arg) => b.WithUserId(arg.U1),
				static (b, arg) => b.WithUserId(arg.U2),
				(U1: 100, U2: 101))
			.Execute();

		Assert.That(result.Select(x => x.Id).OrderBy(x => x).ToList(),
			Is.EqualTo(new[] { 1, 2 }));
	}

	/// <summary>
	/// Multi-index branch: each branch narrows on two codegen extensions.
	/// Branch1: Category=Electronics AND UserId=100 → {1}.
	/// Branch2: Category=Books AND UserId=100 → {3}.
	/// Expected union: {1,3}.
	/// </summary>
	[Test]
	public void Or_MultipleWithXxxPerBranch_ReturnsUnion() {
		using var result = _cache.Query()
			.Or(
				b => b.WithCategory("Electronics").WithUserId(100),
				b => b.WithCategory("Books").WithUserId(100))
			.Execute();

		Assert.That(result.Select(x => x.Id).OrderBy(x => x).ToList(),
			Is.EqualTo(new[] { 1, 3 }));
	}

	/// <summary>
	/// Chained Or: first Or narrows to Status=Active={1,2,5},
	/// second Or further intersects with UserId=100={1,3} ∪ UserId=101={2} = {1,2,3}.
	/// Expected: {1,2,5} ∩ {1,2,3} = {1,2}.
	/// </summary>
	[Test]
	public void Or_Chained_BothOrClausesApplyIntersection() {
		using var result = _cache.Query()
			.Or(
				b => b.WithStatus(TestStatus.Active),
				b => b.WithStatus(TestStatus.Active))  // same value — effectively just Active={1,2,5}
			.Or(
				b => b.WithUserId(100),
				b => b.WithUserId(101))
			.Execute();

		// Status=Active = {1,2,5}; UserId=100∪101 = {1,2,3}; intersection = {1,2}
		Assert.That(result.Select(x => x.Id).OrderBy(x => x).ToList(),
			Is.EqualTo(new[] { 1, 2 }));
	}

	/// <summary>
	/// Or branches narrow by DIFFERENT properties (cross-property union):
	/// outer narrows by Priority=High → {1, 5}, then
	/// Or(WithCategory("Electronics"), WithStatus(Inactive)) unions
	/// {1, 2, 4} ∪ {3} = {1, 2, 3, 4}.
	/// Intersection: {1, 5} ∩ {1, 2, 3, 4} = {1}.
	/// </summary>
	[Test]
	public void Or_CrossPropertyBranches_IntersectsOuterWithUnion() {
		using var result = _cache.Query()
			.WithPriority(TestPriority.High)
			.Or(
				b => b.WithCategory("Electronics"),
				b => b.WithStatus(TestStatus.Inactive))
			.Execute();

		Assert.That(result.Select(x => x.Id).OrderBy(x => x).ToList(),
			Is.EqualTo(new[] { 1 }));
	}
}
