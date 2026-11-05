namespace Prague.Generated.Tests.Query;

using System.Globalization;
using Fixtures.Entities;
using NUnit.Framework;
using Prague.Core;

/// <summary>
///   Generated <c>[DataCacheSort]</c> comparers compare <c>string</c> properties ordinally. A culture-sensitive
///   comparison would order the same rows differently per machine culture and could call distinct strings equal,
///   breaking the total order that stable paging relies on.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SortComparerOrdinalTests {
	private CultureInfo _previous = null!;

	[SetUp]
	public void SetUp() => _previous = CultureInfo.CurrentCulture;

	[TearDown]
	public void TearDown() => CultureInfo.CurrentCulture = _previous;

	[TestCase("en-US")]
	[TestCase("tr-TR")]
	[TestCase("")]
	public void StringColumns_SortOrdinally_UnderAnyCulture(string culture) {
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
		var names = new[] { "b", "B", "a", "A", "i", "I", "ı", "İ", "a­", "Z" };
		var tags = names.Select((n, i) => new SortedTag { Id = i, Name = n }).ToList();

		tags.Sort(SortedTagCache.ByNameComparer);

		var expected = names.Order(StringComparer.Ordinal).ToArray();
		Assert.That(tags.Select(t => t.Name), Is.EqualTo(expected));
	}

	[Test]
	public void DistinctStrings_NeverCompareEqual() {
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
		var plain = new SortedTag { Id = 1, Name = "a" };
		var softHyphen = new SortedTag { Id = 1, Name = "a­" };   // culture comparison ignores U+00AD

		Assert.That(SortedTagCache.ByNameComparer.Compare(plain, softHyphen), Is.Not.Zero);
	}

	[Test]
	public void NullableStringColumn_NullsFirstAscending_LastDescending() {
		var tags = new List<SortedTag> {
			new() { Id = 1, Name = "x", Code = "b" },
			new() { Id = 2, Name = "x", Code = null },
			new() { Id = 3, Name = "x", Code = "a" },
		};

		tags.Sort(SortedTagCache.ByCodeDescComparer);

		Assert.That(tags.Select(t => t.Id), Is.EqualTo(new[] { 1, 3, 2 }));   // "b", "a", then null
	}

	[Test]
	public void SortedQuery_UsesTheOrdinalComparer() {
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
		var cache = new SortedTagCache();
		cache.AddOrUpdate(new SortedTag { Id = 1, Name = "apple" });
		cache.AddOrUpdate(new SortedTag { Id = 2, Name = "Banana" });
		cache.AddOrUpdate(new SortedTag { Id = 3, Name = "cherry" });

		using var rows = cache.Query().WithGroup(0).SortBounded(SortedTagCache.ByNameComparer).ExecutePooled(0, 2);

		Assert.That(rows.Select(t => t.Name), Is.EqualTo(new[] { "Banana", "apple" }));   // 'B' (0x42) < 'a' (0x61)
	}
}
