namespace Prague.Core.Tests.Join;

using Prague.Core;
using Prague.Core.Tests.Infrastructure;
using NUnit.Framework;

// The filtered outer left-symmetric JoinOne fills the result rows by pair slot: one pair per distinct right
// seeded from the rows, every row remembering its pair's slot, the delivery parking each surviving right in
// its slot. Before, OuterFanOutContainer walked every left of each right's index bucket to find the rows —
// the bucket's size, not the page's. These pin the result on a narrowed left (the bucket holds lefts outside
// the result), every filter kind (value predicate, index narrowing, Or), a selector folding several lookup
// keys onto one right, a bounded page, cloned execution and pool balance.
[TestFixture]
public class JoinOneLeftSymFilteredOuterCoreTests {
	private InMemoryDataCache<int, SrLeft> _left = null!;
	private CacheKeyValueListIndex<int, SrLeft, int> _byGroup = null!;
	private CacheSymmetricKeyValueListIndex<int, SrLeft, int> _byCode = null!;
	private InMemoryDataCache<int, ShrCountry> _rights = null!;
	private CacheKeyValueListIndex<int, ShrCountry, string> _byName = null!;
	private CacheUniqueIndex<int, ShrCountry, int> _byNumeric = null!;

	// Lefts 1..20; group 1 = 1..10, group 2 = 11..20; code = 10 + id % 4 (10, 11, 12, 13) — each code's bucket
	// spans both groups. Rights: 10 "A", 11 "B", 12 "A", 13 absent; right 1 "One" (numeric 100) is what
	// k / 10 folds every code onto.
	[SetUp]
	public void SetUp() {
		_left = new InMemoryDataCache<int, SrLeft>();
		_byGroup = _left.CacheKeyValueListIndex<int>(static (_, v) => v.Group);
		_byCode = _left.CacheSymmetricKeyValueListIndex<int>(static (_, v) => v.Code);
		for (var i = 1; i <= 20; i++)
			_left.AddOrUpdate(i, new SrLeft { Id = i, Group = i <= 10 ? 1 : 2, Code = 10 + i % 4 });

		_rights = new InMemoryDataCache<int, ShrCountry>();
		_byName = _rights.CacheKeyValueListIndex<string>(static (_, v) => v.Name);
		_byNumeric = _rights.AddKeyValueIndex<int>(static (_, v) => v.Numeric);
		_rights.AddOrUpdate(1, new ShrCountry { Id = 1, Numeric = 100, Name = "One" });
		_rights.AddOrUpdate(10, new ShrCountry { Id = 10, Numeric = 1000, Name = "A" });
		_rights.AddOrUpdate(11, new ShrCountry { Id = 11, Numeric = 1100, Name = "B" });
		_rights.AddOrUpdate(12, new ShrCountry { Id = 12, Numeric = 1200, Name = "A" });
	}

	private static string Describe(IEnumerable<JoinResult<SrLeft, ShrCountry?>> rows)
		=> string.Join(" ", rows.OrderBy(static r => r.Left.Id).Select(static r => $"{r.Left.Id}={r.Right?.Name ?? "-"}"));

	// Group 1 (ids 1..10) with the right each code reaches through `keep`, "-" otherwise.
	private static string ExpectedGroup1(Func<int, string?> byCode) {
		var parts = new List<string>();
		for (var id = 1; id <= 10; id++)
			parts.Add($"{id}={byCode(10 + id % 4) ?? "-"}");
		return string.Join(" ", parts);
	}

	private readonly struct SrLeftById : IComparer<SrLeft> {
		public int Compare(SrLeft? x, SrLeft? y) => (x?.Id ?? 0).CompareTo(y?.Id ?? 0);
	}

	[Test]
	public void NarrowedLeft_ValuePredicate() {
		var rows = _left.Query().UseIndex(_byGroup, 1).JoinOne(_byCode, _rights, static q => q.Where(static r => r.Name == "A")).Execute();
		Assert.That(Describe(rows), Is.EqualTo(ExpectedGroup1(static c => c is 10 or 12 ? "A" : null)));
	}

	[Test]
	public void NarrowedLeft_IndexFilter() {
		var rows = _left.Query().UseIndex(_byGroup, 1).JoinOne(_byCode, _rights, q => q.UseIndex(_byName, "A")).Execute();
		Assert.That(Describe(rows), Is.EqualTo(ExpectedGroup1(static c => c is 10 or 12 ? "A" : null)));
	}

	[Test]
	public void NarrowedLeft_OrFilter() {
		var rows = _left.Query().UseIndex(_byGroup, 1)
			.JoinOne(_byCode, _rights, q => q.Or(c => c.UseIndex(_byName, "A"), c => c.UseIndex(_byName, "B")))
			.Execute();
		Assert.That(Describe(rows), Is.EqualTo(ExpectedGroup1(static c => c switch { 10 or 12 => "A", 11 => "B", _ => null })));
	}

	[Test]
	public void NarrowedLeft_FilterRejectsEverything_AllNull() {
		var rows = _left.Query().UseIndex(_byGroup, 1).JoinOne(_byCode, _rights, static q => q.Where(static _ => false)).Execute();
		Assert.That(rows.Count, Is.EqualTo(10));
		Assert.That(rows.All(static r => r.Right is null), Is.True);
	}

	[Test]
	public void ShapeA_Selector_FoldsEveryCodeOntoOneRight() {
		var rows = _left.Query().UseIndex(_byGroup, 1).JoinOne(_byCode, static (int k) => k / 10, _rights, static q => q.Where(static _ => true)).Execute();
		Assert.That(Describe(rows), Is.EqualTo(ExpectedGroup1(static _ => "One")));
	}

	[Test]
	public void ShapeB_Selector_FoldsEveryCodeOntoOneRight() {
		var rows = _left.Query().UseIndex(_byGroup, 1).JoinOne(_byCode, static (int k) => k / 10 * 100, _rights, _byNumeric, static q => q.Where(static _ => true)).Execute();
		Assert.That(Describe(rows), Is.EqualTo(ExpectedGroup1(static _ => "One")));
	}

	[Test]
	public void SortBoundedPage_IndexFilter() {
		using var page = _left.Query().SortBounded(new SrLeftById()).JoinOne(_byCode, _rights, q => q.UseIndex(_byName, "A")).ExecutePooled(3, 4);
		// Ids 4..7: codes 10, 11, 12, 13.
		Assert.That(Describe(page), Is.EqualTo("4=A 5=- 6=A 7=-"));
		Assert.That(page.TotalCount, Is.EqualTo(20));
	}

	[Test]
	public void Cloned_EveryRowGetsItsOwnCopy() {
		var rows = _left.Query().UseIndex(_byGroup, 1).JoinOne(_byCode, static (int k) => k / 10, _rights, static q => q.Where(static _ => true)).ExecuteCloned();
		Assert.That(_rights.TryGet(1, out var stored), Is.True);
		var rights = rows.Select(static r => r.Right!).ToList();
		Assert.That(rights, Has.Count.EqualTo(10));
		Assert.That(rights.All(r => !ReferenceEquals(r, stored) && r.CacheEquals(stored)), Is.True, "a clone of the stored right");
		Assert.That(rights.Distinct(ReferenceEqualityComparer.Instance).Count(), Is.EqualTo(10), "one clone per row");
	}

	[Test]
	public void FilteredOuter_ThrowingFilterAndPredicate_Balanced() =>
		LeakAssert.Balanced(() => {
			using (var rows = _left.Query().UseIndex(_byGroup, 1).JoinOne(_byCode, _rights, q => q.UseIndex(_byName, "A")).ExecutePooled())
				Assert.That(rows.Count, Is.EqualTo(10));
			Assert.Throws<InvalidOperationException>(() => _left.Query().UseIndex(_byGroup, 1)
				.JoinOne(_byCode, _rights, static q => throw new InvalidOperationException("hostile filter"))
				.ExecutePooled());
			Assert.Throws<InvalidOperationException>(() => _left.Query().UseIndex(_byGroup, 1)
				.JoinOne(_byCode, _rights, static q => q.Where(static r => r.Name == "B" ? throw new InvalidOperationException("hostile predicate") : true))
				.ExecutePooled());
		});
}
