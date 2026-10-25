namespace Prague.Core.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// CountJoin / InnerCountJoin: per left, the number of rights the matching JoinMany delivers, as an int.
// Every shape is pinned against its JoinMany twin (.Right.Count per left, same left set) on Execute() and
// ExecutePooled(): right-list (identity, selector, filter, filter + arg), left-symmetric (identity, filter),
// collection reverse and forward. Fixtures are the JoinMany core tests' own (MnAuthor/MnBook,
// MlsAuthor/MlsBook, MnTag/MnTaggedBook).
[TestFixture]
public class CountJoinCoreTests {
	private InMemoryDataCache<int, MnAuthor> _authors = null!;
	private InMemoryDataCache<int, MnBook> _books = null!;
	private CacheKeyValueListIndex<int, MnBook, int> _bookByAuthor = null!;

	private InMemoryDataCache<int, MlsAuthor> _symAuthors = null!;
	private InMemoryDataCache<int, MlsBook> _symBooks = null!;
	private CacheSymmetricKeyValueListIndex<int, MlsAuthor, string> _symAuthorCountry = null!;
	private CacheKeyValueListIndex<int, MlsBook, string> _symBookCountry = null!;

	private InMemoryDataCache<int, MnTag> _tags = null!;
	private InMemoryDataCache<int, MnTaggedBook> _taggedBooks = null!;
	private CacheCollectionSymmetricKeyValueListIndex<int, MnTaggedBook, int> _bookTags = null!;

	[SetUp]
	public void SetUp() {
		_authors = new InMemoryDataCache<int, MnAuthor>();
		_books = new InMemoryDataCache<int, MnBook>();
		_bookByAuthor = _books.CacheKeyValueListIndex<int>(static (_, v) => v.AuthorId);
		// Authors 1..6; author i has i % 4 books (author 4 none), author 5's books are all "X"-titled.
		for (var a = 1; a <= 6; a++) {
			_authors.AddOrUpdate(a, new MnAuthor { Id = a, Name = "A" + a });
			for (var k = 0; k < a % 4; k++)
				_books.AddOrUpdate(a * 10 + k, new MnBook { Id = a * 10 + k, AuthorId = a, Title = (k % 2 == 0 ? "X" : "Y") + k });
		}

		_symAuthors = new InMemoryDataCache<int, MlsAuthor>();
		_symBooks = new InMemoryDataCache<int, MlsBook>();
		_symAuthorCountry = _symAuthors.CacheSymmetricKeyValueListIndex<string>(static (_, v) => v.Country);
		_symBookCountry = _symBooks.CacheKeyValueListIndex<string>(static (_, v) => v.Country);
		_symAuthors.AddOrUpdate(1, new MlsAuthor { Id = 1, Country = "UK", Name = "Tolkien" });
		_symAuthors.AddOrUpdate(2, new MlsAuthor { Id = 2, Country = "UK", Name = "Lewis" });
		_symAuthors.AddOrUpdate(3, new MlsAuthor { Id = 3, Country = "US", Name = "Asimov" });
		_symAuthors.AddOrUpdate(4, new MlsAuthor { Id = 4, Country = "FR", Name = "OrphanFrench" });
		_symBooks.AddOrUpdate(101, new MlsBook { Id = 101, Country = "UK", Title = "Hobbit" });
		_symBooks.AddOrUpdate(102, new MlsBook { Id = 102, Country = "UK", Title = "Narnia" });
		_symBooks.AddOrUpdate(201, new MlsBook { Id = 201, Country = "US", Title = "Foundation" });

		_tags = new InMemoryDataCache<int, MnTag>();
		_taggedBooks = new InMemoryDataCache<int, MnTaggedBook>();
		_bookTags = _taggedBooks.CacheCollectionSymmetricKeyValueListIndex<int>(static (_, b) => b.TagIds);
		_tags.AddOrUpdate(10, new MnTag { Id = 10, Name = "fantasy" });
		_tags.AddOrUpdate(20, new MnTag { Id = 20, Name = "classic" });
		_tags.AddOrUpdate(99, new MnTag { Id = 99, Name = "unused" });
		_taggedBooks.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Hobbit", TagIds = [10, 20] });
		_taggedBooks.AddOrUpdate(2, new MnTaggedBook { Id = 2, Title = "LOTR", TagIds = [10] });
		_taggedBooks.AddOrUpdate(3, new MnTaggedBook { Id = 3, Title = "Dune", TagIds = [20, 77] });
		_taggedBooks.AddOrUpdate(4, new MnTaggedBook { Id = 4, Title = "Untagged", TagIds = [] });
	}

	private static Dictionary<int, int> Counts<TLeft>(QueryResults<JoinResult<TLeft, int>> results, Func<TLeft, int> id) {
		var map = new Dictionary<int, int>();
		foreach (var r in results)
			map.Add(id(r.Left), r.Right);
		return map;
	}

	private static Dictionary<int, int> ManyCounts<TLeft, TRight>(QueryResults<JoinResult<TLeft, QueryResults<TRight>>> results, Func<TLeft, int> id) {
		var map = new Dictionary<int, int>();
		foreach (var r in results)
			map.Add(id(r.Left), r.Right.Count);
		return map;
	}

	private static void AssertSame(Dictionary<int, int> count, Dictionary<int, int> many, string label) {
		Assert.That(count.Keys.OrderBy(static k => k), Is.EqualTo(many.Keys.OrderBy(static k => k)), label + " lefts");
		foreach (var (k, v) in many)
			Assert.That(count[k], Is.EqualTo(v), label + " left " + k);
	}

	// ── Right-list ──────────────────────────────────────────────────────────────

	[Test]
	public void RightList_Outer_CountsEveryLeft_IncludingZero() {
		var counts = Counts(_authors.Query().CountJoin(_books, _bookByAuthor).Execute(), static a => a.Id);

		Assert.That(counts, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 0, [5] = 1, [6] = 2 }));
	}

	[Test]
	public void RightList_Inner_DropsZeroCounts() {
		var counts = Counts(_authors.Query().InnerCountJoin(_books, _bookByAuthor).Execute(), static a => a.Id);

		Assert.That(counts.Keys.OrderBy(static k => k), Is.EqualTo(new[] { 1, 2, 3, 5, 6 }));
		Assert.That(counts[3], Is.EqualTo(3));
	}

	[Test]
	public void RightList_Filtered_CountsOnlySurvivors_InnerDropsFilteredToZero() {
		var outer = Counts(_authors.Query().CountJoin(_books, _bookByAuthor, static q => q.Where(static b => b.Title.StartsWith('Y'))).Execute(), static a => a.Id);
		var inner = Counts(_authors.Query().InnerCountJoin(_books, _bookByAuthor, static q => q.Where(static b => b.Title.StartsWith('Y'))).Execute(), static a => a.Id);

		Assert.That(outer, Is.EqualTo(new Dictionary<int, int> { [1] = 0, [2] = 1, [3] = 1, [4] = 0, [5] = 0, [6] = 1 }));
		Assert.That(inner, Is.EqualTo(new Dictionary<int, int> { [2] = 1, [3] = 1, [6] = 1 }));
	}

	[Test]
	public void RightList_FilterWithArg_MatchesJoinMany() {
		foreach (var prefix in new[] { "X", "Y", "Z" }) {
			AssertSame(
				Counts(_authors.Query().CountJoin(_books, _bookByAuthor, static (q, p) => q.Where(b => b.Title.StartsWith(p)), prefix).Execute(), static a => a.Id),
				ManyCounts(_authors.Query().JoinMany(_books, _bookByAuthor, static (q, p) => q.Where(b => b.Title.StartsWith(p)), prefix).Execute(), static a => a.Id),
				"outer " + prefix);
			AssertSame(
				Counts(_authors.Query().InnerCountJoin(_books, _bookByAuthor, static (q, p) => q.Where(b => b.Title.StartsWith(p)), prefix).Execute(), static a => a.Id),
				ManyCounts(_authors.Query().InnerJoinMany(_books, _bookByAuthor, static (q, p) => q.Where(b => b.Title.StartsWith(p)), prefix).Execute(), static a => a.Id),
				"inner " + prefix);
		}
	}

	[Test]
	public void RightList_NonInjectiveSelector_SharedRightCountedPerLeft() {
		// Authors 2k and 2k + 1 both read author 2k's bucket: every right is shared by two lefts.
		var outer = Counts(_authors.Query().CountJoin(static id => id - id % 2, _books, _bookByAuthor).Execute(), static a => a.Id);
		var filtered = Counts(_authors.Query().CountJoin(static id => id - id % 2, _books, _bookByAuthor, static q => q.Where(static b => b.Title.StartsWith('X'))).Execute(), static a => a.Id);

		AssertSame(outer, ManyCounts(_authors.Query().JoinMany(static id => id - id % 2, _books, _bookByAuthor).Execute(), static a => a.Id), "selector");
		AssertSame(filtered, ManyCounts(_authors.Query().JoinMany(static id => id - id % 2, _books, _bookByAuthor, static q => q.Where(static b => b.Title.StartsWith('X'))).Execute(), static a => a.Id), "selector filtered");
		Assert.That(outer[2], Is.EqualTo(2));
		Assert.That(outer[3], Is.EqualTo(2));
		Assert.That(filtered[6], Is.EqualTo(1));
	}

	[Test]
	public void RightList_Where_Then_InnerCount_NarrowsCandidates() {
		var counts = Counts(_authors.Query().Where(static a => a.Id >= 3).InnerCountJoin(_books, _bookByAuthor).Execute(), static a => a.Id);

		Assert.That(counts, Is.EqualTo(new Dictionary<int, int> { [3] = 3, [5] = 1, [6] = 2 }));
	}

	[Test]
	public void RightList_Pooled_MatchesEager_AndDisposes() {
		using var pooled = _authors.Query().CountJoin(_books, _bookByAuthor).ExecutePooled();
		using var pooledInner = _authors.Query().InnerCountJoin(_books, _bookByAuthor, static q => q.Where(static b => b.Title.StartsWith('X'))).ExecutePooled();

		AssertSame(Counts(pooled, static a => a.Id), ManyCounts(_authors.Query().JoinMany(_books, _bookByAuthor).Execute(), static a => a.Id), "pooled outer");
		AssertSame(Counts(pooledInner, static a => a.Id), ManyCounts(_authors.Query().InnerJoinMany(_books, _bookByAuthor, static q => q.Where(static b => b.Title.StartsWith('X'))).Execute(), static a => a.Id), "pooled inner");
	}

	[Test]
	public void RightList_ChainedAfterJoinMany_CountsTheSameBuckets() {
		var results = _authors.Query().InnerJoinMany(_books, _bookByAuthor).InnerCountJoin(_books, _bookByAuthor, static q => q.Where(static b => b.Title.StartsWith('Y'))).Execute();

		var byId = results.ToDictionary(static r => r.Left.Id);
		Assert.That(byId.Keys.OrderBy(static k => k), Is.EqualTo(new[] { 2, 3, 6 }));
		foreach (var r in results)
			Assert.That(r.Right2, Is.EqualTo(r.Right.Count(static b => b.Title.StartsWith('Y'))));
	}

	// ── Left-symmetric ──────────────────────────────────────────────────────────

	[Test]
	public void LeftSym_OuterInnerFiltered_MatchJoinMany() {
		var outer = Counts(_symAuthors.Query().CountJoin(_symAuthorCountry, _symBooks, _symBookCountry).Execute(), static a => a.Id);

		Assert.That(outer, Is.EqualTo(new Dictionary<int, int> { [1] = 2, [2] = 2, [3] = 1, [4] = 0 }));
		AssertSame(
			Counts(_symAuthors.Query().InnerCountJoin(_symAuthorCountry, _symBooks, _symBookCountry).Execute(), static a => a.Id),
			ManyCounts(_symAuthors.Query().InnerJoinMany(_symAuthorCountry, _symBooks, _symBookCountry).Execute(), static a => a.Id),
			"inner");
		AssertSame(
			Counts(_symAuthors.Query().CountJoin(_symAuthorCountry, _symBooks, _symBookCountry, static q => q.Where(static b => b.Title != "Narnia")).Execute(), static a => a.Id),
			ManyCounts(_symAuthors.Query().JoinMany(_symAuthorCountry, _symBooks, _symBookCountry, static q => q.Where(static b => b.Title != "Narnia")).Execute(), static a => a.Id),
			"filtered");
		AssertSame(
			Counts(_symAuthors.Query().InnerCountJoin(_symAuthorCountry, _symBooks, _symBookCountry, static q => q.Where(static b => b.Title == "Foundation")).Execute(), static a => a.Id),
			ManyCounts(_symAuthors.Query().InnerJoinMany(_symAuthorCountry, _symBooks, _symBookCountry, static q => q.Where(static b => b.Title == "Foundation")).Execute(), static a => a.Id),
			"inner filtered");
	}

	// ── Collection ─────────────────────────────────────────────────────────────

	[Test]
	public void CollectionReverse_OuterInnerFiltered_MatchJoinMany() {
		var outer = Counts(_tags.Query().CountJoinCollection(_taggedBooks, _bookTags).Execute(), static t => t.Id);

		Assert.That(outer, Is.EqualTo(new Dictionary<int, int> { [10] = 2, [20] = 2, [99] = 0 }));
		AssertSame(
			Counts(_tags.Query().InnerCountJoinCollection(_taggedBooks, _bookTags).Execute(), static t => t.Id),
			ManyCounts(_tags.Query().InnerJoinManyCollection(_taggedBooks, _bookTags).Execute(), static t => t.Id),
			"inner");
		AssertSame(
			Counts(_tags.Query().CountJoinCollection(_taggedBooks, _bookTags, static q => q.Where(static b => b.Title != "Hobbit")).Execute(), static t => t.Id),
			ManyCounts(_tags.Query().JoinManyCollection(_taggedBooks, _bookTags, static q => q.Where(static b => b.Title != "Hobbit")).Execute(), static t => t.Id),
			"filtered");
	}

	[Test]
	public void CollectionForward_OuterInnerFiltered_MatchJoinMany() {
		var outer = Counts(_taggedBooks.Query().CountJoinCollectionForward(_tags, _bookTags).Execute(), static b => b.Id);

		// Book 3 references tag 77, which is not in the tag cache: JoinMany drops it, so does the count.
		AssertSame(outer, ManyCounts(_taggedBooks.Query().JoinManyCollectionForward(_tags, _bookTags).Execute(), static b => b.Id), "outer");
		AssertSame(
			Counts(_taggedBooks.Query().InnerCountJoinCollectionForward(_tags, _bookTags).Execute(), static b => b.Id),
			ManyCounts(_taggedBooks.Query().InnerJoinManyCollectionForward(_tags, _bookTags).Execute(), static b => b.Id),
			"inner");
		AssertSame(
			Counts(_taggedBooks.Query().InnerCountJoinCollectionForward(_tags, _bookTags, static q => q.Where(static t => t.Name == "classic")).Execute(), static b => b.Id),
			ManyCounts(_taggedBooks.Query().InnerJoinManyCollectionForward(_tags, _bookTags, static q => q.Where(static t => t.Name == "classic")).Execute(), static b => b.Id),
			"inner filtered");
	}
}
