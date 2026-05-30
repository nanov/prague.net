// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Core.Tests;

using Prague.Core;

#region Test Entities

public class Note : ICacheEquatable<Note>, ICacheClonable<Note>
{
	public int Id { get; set; }
	public int AuthorId { get; set; }

	public bool CacheEquals(Note? other) => other != null && Id == other.Id;
	public int CacheGetHashCode() => Id;
	public Note Clone() => new Note { Id = Id, AuthorId = AuthorId};
}

/// <summary>Simple Author entity for testing.</summary>
public class Author : ICacheEquatable<Author>, ICacheClonable<Author>
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? CountryId { get; set; }

    public bool CacheEquals(Author? other) => other != null && Id == other.Id && Name == other.Name && CountryId == other.CountryId;
    public int CacheGetHashCode() => Id;
    public Author Clone() => new Author { Id = Id, Name = Name, CountryId = CountryId };
}

/// <summary>Book entity with foreign key to Author.</summary>
public class Book : ICacheEquatable<Book>, ICacheClonable<Book>
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public string Title { get; set; } = "";
    public int AuthorId { get; set; }

    public bool CacheEquals(Book? other) => other != null && Id == other.Id && Title == other.Title && AuthorId == other.AuthorId;
    public int CacheGetHashCode() => Id;
    public Book Clone() => new Book { Id = Id, Title = Title, AuthorId = AuthorId };
}

public class CountryCategory : ICacheEquatable<CountryCategory>, ICacheClonable<CountryCategory> {
	public (int CategoryId, int CountryId) Id { get; set; }
	public string Title { get; set; }

	public bool CacheEquals(CountryCategory? other) => other != null && Id == other.Id && Title == other.Title;
	public int CacheGetHashCode() => Id.GetHashCode();
	public CountryCategory Clone() => new CountryCategory { Id = Id, Title = Title};
}

/// <summary>Country entity.</summary>
public class Country : ICacheEquatable<Country>, ICacheClonable<Country>
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public bool CacheEquals(Country? other) => other != null && Id == other.Id && Name == other.Name;
    public int CacheGetHashCode() => Id;
    public Country Clone() => new Country { Id = Id, Name = Name };
}

#endregion

[TestFixture]
public class JoinQueryBuilderLevel1Tests
{
    private InMemoryDataCache<int, Author> _authorCache = null!;
    private InMemoryDataCache<int, Book> _bookCache = null!;
    private InMemoryDataCache<int, Country> _countryCache = null!;
    private InMemoryDataCache<int, Note> _noteCache = null!;
    private InMemoryDataCache<(int, int), CountryCategory> _countryCategoryCache = null!;

    // Indexes
    private CacheUniqueIndex<int, Book, int> _bookByAuthorIndex = null!;
    private CacheKeyValueListIndex<int, Book, int> _booksByAuthorIndex = null!;
    private CacheSymmetricKeyValueListIndex<int, Author, int> _authorByCountryIndex;
    private CacheSymmetricKeyValueListIndex<int, Note, int> _noteByAuthorIndex;
    private CacheSymmetricUniqueIndex<int, Book, int> _bookByCategoryIndex;

    [SetUp]
    public void Setup()
    {
        // Create caches
        _authorCache = new InMemoryDataCache<int, Author>();
        _bookCache = new InMemoryDataCache<int, Book>();
        _countryCache = new InMemoryDataCache<int, Country>();
        _noteCache = new InMemoryDataCache<int, Note>();
        _countryCategoryCache = new InMemoryDataCache<(int, int), CountryCategory>();

        _authorByCountryIndex = _authorCache.CacheSymmetricKeyValueListIndex((k, a) => a.CountryId ?? 0);
        _noteByAuthorIndex = _noteCache.CacheSymmetricKeyValueListIndex((k, n) => n.AuthorId);
        // Create indexes
        _bookByAuthorIndex = _bookCache.AddKeyValueIndex((k, b) => b.AuthorId);
        _booksByAuthorIndex = _bookCache.CacheKeyValueListIndex((k, b) => b.AuthorId);
        _bookByCategoryIndex = _bookCache.AddSymmetricKeyValueIndex((k, b) => b.CategoryId);

        // Seed data
        SeedData();
    }

    private void SeedData()
    {
        // Countries
        _countryCache.AddOrUpdate(1, new Country { Id = 1, Name = "USA" });
        _countryCache.AddOrUpdate(2, new Country { Id = 2, Name = "UK" });
        _countryCache.AddOrUpdate(3, new Country { Id = 3, Name = "France" });

        // Authors (some with country, some without)
        _authorCache.AddOrUpdate(1, new Author { Id = 1, Name = "Author One", CountryId = 1 });
        _authorCache.AddOrUpdate(2, new Author { Id = 2, Name = "Author Two", CountryId = 2 });
        _authorCache.AddOrUpdate(3, new Author { Id = 3, Name = "Author Three", CountryId = null }); // No country
        _authorCache.AddOrUpdate(4, new Author { Id = 4, Name = "Author Four", CountryId = 99 }); // Non-existent country

        _noteCache.AddOrUpdate(1, new Note() { Id = 1, AuthorId = 1 });
        _noteCache.AddOrUpdate(2, new Note() { Id = 2, AuthorId = 2 });
        _noteCache.AddOrUpdate(3, new Note() { Id = 3, AuthorId = 2 });

        // Books
        _bookCache.AddOrUpdate(1, new Book { Id = 1, Title = "Book A", AuthorId = 1, CategoryId = 1 });
        _bookCache.AddOrUpdate(2, new Book { Id = 2, Title = "Book B", AuthorId = 1, CategoryId = 2 });
        _bookCache.AddOrUpdate(3, new Book { Id = 3, Title = "Book C", AuthorId = 2, CategoryId = 3 });
        _bookCache.AddOrUpdate(4, new Book { Id = 4, Title = "Book D", AuthorId = 3, CategoryId = 4 });


        _countryCategoryCache.AddOrUpdate((1, 1), new CountryCategory { Id = (1, 1), Title = "Category 1" });
        _countryCategoryCache.AddOrUpdate((1, 2), new CountryCategory { Id = (1, 2), Title = "Category 2" });
        _countryCategoryCache.AddOrUpdate((1, 3), new CountryCategory { Id = (1, 3), Title = "Category 3" });
        // Note: Author 4 has no books
    }

    #region Forward JoinOne Tests (Author -> Country via CountryId)

    [Ignore("One to many not working")]
    [Test]
    public void JoinOne_Forward_ReturnsAllAuthorsWithCountrySlot()
    {
        // Arrange & Act
        var builder = _authorCache.Query()
            .JoinOne(_countryCache, a => a.CountryId ?? 0);
        var results = builder.Execute();

        // Assert
        Assert.That(results.Count, Is.EqualTo(4)); // All 4 authors

        var list = results.ToList();

        // Author 1 -> Country 1 (USA)
        var author1 = list.First(r => r.Left.Id == 1);
        Assert.That(author1.Right, Is.Not.Null);
        Assert.That(author1.Right!.Name, Is.EqualTo("USA"));

        // Author 2 -> Country 2 (UK)
        var author2 = list.First(r => r.Left.Id == 2);
        Assert.That(author2.Right, Is.Not.Null);
        Assert.That(author2.Right!.Name, Is.EqualTo("UK"));

        // Author 3 -> No country (CountryId = null, becomes 0)
        var author3 = list.First(r => r.Left.Id == 3);
        Assert.That(author3.Right, Is.Null);

        // Author 4 -> Non-existent country (CountryId = 99)
        var author4 = list.First(r => r.Left.Id == 4);
        Assert.That(author4.Right, Is.Null);
    }


    [Test]
    public void InnerJoinOne_Indexed()
    {
	    // Arrange & Act
	    var builder = _bookCache.Query()
		    .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
		    .InnerJoinOne(
			    _bookByCategoryIndex.Reverse,
			    _countryCategoryCache,
			    (c) => (c, 1));
	    var results = builder.Execute();

	    // Assert
	    Assert.That(results.Count, Is.EqualTo(1)); // Only authors 1 and 2 have valid countries

	    var list = results.ToList();
	    Assert.That(list.All(r => r.Right != null), Is.True);
    }

    [Ignore("Inner Join rework")]
    [Test]
    public void InnerJoinOne_Forward_Predicate()
    {
	    // Arrange & Act
	    var builder = _authorCache.Query()
		    .InnerJoinOne(
			    _countryCache, a => a.CountryId ?? 0);
	    var results = builder.Execute();

	    // Assert
	    Assert.That(results.Count, Is.EqualTo(2)); // Only authors 1 and 2 have valid countries

	    var list = results.ToList();
	    Assert.That(list.All(r => r.Right != null), Is.True);
	    Assert.That(list.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2 }));
    }

    [Ignore("Inner Join rework")]
    [Test]
    public void InnerJoinOne_Forward_ReturnsOnlyAuthorsWithMatchingCountry()
    {
        // Arrange & Act
        var builder = _authorCache.Query()
            .InnerJoinOne(_countryCache, a => a.CountryId ?? 0);
        var results = builder.Execute();

        // Assert
        Assert.That(results.Count, Is.EqualTo(2)); // Only authors 1 and 2 have valid countries

        var list = results.ToList();
        Assert.That(list.All(r => r.Right != null), Is.True);
        Assert.That(list.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2 }));
    }

    #endregion

    #region Reverse JoinOne Tests (Author -> Book via index)

    [Test]
    public void JoinOne_Reverse_ReturnsAuthorsWithFirstBook()
    {
        // Arrange & Act
        var builder = _authorCache.Query()
            .JoinOne(_bookCache, _bookByAuthorIndex);
        var results = builder.Execute();

        // Assert
        Assert.That(results.Count, Is.EqualTo(4)); // All 4 authors

        var list = results.ToList();

        // Author 1 has books
        var author1 = list.First(r => r.Left.Id == 1);
        Assert.That(author1.Right, Is.Not.Null);

        // Author 4 has no books
        var author4 = list.First(r => r.Left.Id == 4);
        Assert.That(author4.Right, Is.Null);
    }

    [Test]
    public void InnerJoinOne_Reverse_ReturnsOnlyAuthorsWithBooks()
    {
        // Arrange & Act
        var builder = _authorCache.Query()
            .InnerJoinOne(_bookCache, _bookByAuthorIndex);
        var results = builder.Execute();

        // Assert
        Assert.That(results.Count, Is.EqualTo(3)); // Authors 1, 2, 3 have books; Author 4 doesn't

        var list = results.ToList();
        Assert.That(list.All(r => r.Right != null), Is.True);
        Assert.That(list.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    #endregion

    #region JoinMany Tests (Author -> Books via list index)

    [Test]
    public void JoinMany_Sort_AndJoin() {
	    // Arrange & Act
	    var builder = _authorCache.Query()
		    .UseIndex(_authorByCountryIndex, 2)
		    .JoinMany(_bookCache, _booksByAuthorIndex);
	    var builder2 = builder.Sort(
		    Comparer<JoinResult<Author, QueryResults<Book>>>.Create((a, b) => a.Left.Id.CompareTo(b.Left.Id)));
	    var builder3 = builder2.JoinMany(_noteCache, _noteByAuthorIndex);
	    var results = builder3.ExecutePooled(skip: 0, take: 1);
    }

    [Test]
    public void JoinMany_ReturnsAuthorsWithAllBooks()
    {
        // Arrange & Act
        var builder = _authorCache.Query()
            .JoinMany(_bookCache, _booksByAuthorIndex);
        var results = builder.Execute();

        // Assert
        Assert.That(results.Count, Is.EqualTo(4)); // All 4 authors

        var list = results.ToList();

        // Author 1 has 2 books
        var author1 = list.First(r => r.Left.Id == 1);
        Assert.That(author1.Right.Count, Is.EqualTo(2));

        // Author 2 has 1 book
        var author2 = list.First(r => r.Left.Id == 2);
        Assert.That(author2.Right.Count, Is.EqualTo(1));

        // Author 3 has 1 book
        var author3 = list.First(r => r.Left.Id == 3);
        Assert.That(author3.Right.Count, Is.EqualTo(1));

        // Author 4 has no books
        var author4 = list.First(r => r.Left.Id == 4);
        Assert.That(author4.Right.Count, Is.EqualTo(0));
    }

    /*
    [Test]
    public void InnerJoinMany_ReturnsOnlyAuthorsWithBooks()
    {
        // Arrange & Act
        var results = _authorCache.Query()
            .InnerJoinMany(_bookCache, _booksByAuthorIndex)
            .Execute();

        // Assert
        Assert.That(results.Count, Is.EqualTo(3)); // Authors 1, 2, 3 have books

        var list = results.ToList();
        Assert.That(list.Select(r => r.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2, 3 }));
    }
    */

    #endregion

    #region Query Filter Tests

    [Test]
    public void JoinOne_WithFilter_AppliesFilterToRightSide()
    {
        // Arrange - Add more countries for filtering
        _countryCache.AddOrUpdate(4, new Country { Id = 4, Name = "Spain" });
        _authorCache.AddOrUpdate(5, new Author { Id = 5, Name = "Author Five", CountryId = 4 });

        // Act - Join with filter that only includes countries with Id <= 2
        var builder = _authorCache.Query()
            .JoinOne(_countryCache, _authorByCountryIndex,
                q => q.Where(c => c.Id <= 2));
        var results = builder.Execute();

        // Assert
        var list = results.ToList();

        // Author 1 (Country 1) should have match
        Assert.That(list.First(r => r.Left.Id == 1).Right, Is.Not.Null);

        // Author 2 (Country 2) should have match
        Assert.That(list.First(r => r.Left.Id == 2).Right, Is.Not.Null);

        // Author 5 (Country 4) should NOT have match due to filter
        Assert.That(list.First(r => r.Left.Id == 5).Right, Is.Null);
    }

    #endregion

    #region Chaining Tests (Level 2)

    [Test]
    public void JoinOne_CanChainToLevel2()
    {
        // Chain from level 1 to level 2
        var builder = _authorCache.Query()
            .JoinOne(_countryCache, _authorByCountryIndex);
        var level2Builder = builder.JoinOne(_bookCache, _bookByAuthorIndex);

        // Just verify the builder was created successfully (LeftQuery was removed)
        Assert.Pass();
    }

    // [Ignore("One to Many rework")]
    [Test]
    public void JoinOne_Level2_Execute_ReturnsCorrectResults()
    {
        // Author -> Country (forward) -> Book (reverse by author)
        var builder = _authorCache.Query()
            .JoinOne(_countryCache, _authorByCountryIndex);
        var builder2 = builder.JoinOne(_bookCache, _bookByAuthorIndex);
        var results = builder2.Execute();

        // Should have all 4 authors
        Assert.That(results.Count, Is.EqualTo(4));

        var list = results.ToList();

        // Check author 1 (USA, has books)
        var author1Result = list.First(r => r.Left.Id == 1);
        Assert.That(author1Result.Right?.Name, Is.EqualTo("USA"));
        Assert.That(author1Result.Right2?.Title, Is.Not.Null); // Has a book

        // Check author 2 (UK, has book)
        var author2Result = list.First(r => r.Left.Id == 2);
        Assert.That(author2Result.Right?.Name, Is.EqualTo("UK"));
        Assert.That(author2Result.Right2?.Title, Is.EqualTo("Book C"));

        // Check author 4 (non-existent country, no books)
        var author4Result = list.First(r => r.Left.Id == 4);
        Assert.That(author4Result.Right, Is.Null); // Country 99 doesn't exist
        Assert.That(author4Result.Right2, Is.Null); // No books
    }

    [Test]
    public void JoinOne_Level3_Execute_ReturnsCorrectResults()
    {
        // Author -> Country (forward) -> Book (reverse) -> Country again (forward from author)
        var builder = _authorCache.Query()
            .JoinOne(_countryCache, _authorByCountryIndex);  // Right = Country
        var builder2 = builder.JoinOne(_bookCache, _bookByAuthorIndex);        // Right2 = Book
        var results = builder2.Execute();

        Assert.That(results.Count, Is.EqualTo(4));

        var list = results.ToList();

        // Author 1 should have same country in Right and Right3
        var author1Result = list.First(r => r.Left.Id == 1);
        Assert.That(author1Result.Right?.Name, Is.EqualTo("USA"));
        Assert.That(author1Result.Right2?.Title, Is.Not.Null);
    }

    [Test]
    [Ignore("TODO: fix joins")]
    public void JoinOne_Level4_Execute_ReturnsCorrectResults()
    {
        // 4 levels of joins
        var builder = _authorCache.Query()
            .JoinOne(_countryCache, _authorByCountryIndex);  // Right = Country
        var builder2 = builder.JoinOne(_bookCache, _bookByAuthorIndex);        // Right2 = Book
        var builder3 = builder2.JoinOne(_countryCache, a => a.CountryId ?? 0);  // Right3 = Country
        var builder4 = builder3.JoinOne(_bookCache, _bookByAuthorIndex);        // Right4 = Book (same as Right2)
        var results = builder4.Execute();

        Assert.That(results.Count, Is.EqualTo(4));

        var list = results.ToList();

        // Author 1 should have books in Right2 and Right4
        var author1Result = list.First(r => r.Left.Id == 1);
        Assert.That(author1Result.Right?.Name, Is.EqualTo("USA"));
        Assert.That(author1Result.Right2?.Title, Is.Not.Null);
        Assert.That(author1Result.Right3?.Name, Is.EqualTo("USA"));
        Assert.That(author1Result.Right4?.Title, Is.Not.Null);

        // Right2 and Right4 should be same book (same reverse index lookup)
        Assert.That(author1Result.Right2?.Id, Is.EqualTo(author1Result.Right4?.Id));
    }

    [Test]
    [Ignore("TODO: fix joins")]
    public void JoinOne_Level5_Execute_ReturnsCorrectResults()
    {
        // 5 levels - maximum
        var builder = _authorCache.Query()
            .JoinOne(_countryCache, _authorByCountryIndex);  // Right
        var builder2 = builder.JoinOne(_bookCache, _bookByAuthorIndex);        // Right2
        var builder3 = builder2.JoinOne(_countryCache, a => a.CountryId ?? 0);  // Right3
        var builder4 = builder3.JoinOne(_bookCache, _bookByAuthorIndex);        // Right4
        var builder5 = builder4.JoinOne(_countryCache, a => a.CountryId ?? 0);  // Right5
        var results = builder5.Execute();

        Assert.That(results.Count, Is.EqualTo(4));

        var list = results.ToList();

        // Author 2 (UK) - check all 5 slots
        var author2Result = list.First(r => r.Left.Id == 2);
        Assert.That(author2Result.Right?.Name, Is.EqualTo("UK"));
        Assert.That(author2Result.Right2?.Title, Is.EqualTo("Book C"));
        Assert.That(author2Result.Right3?.Name, Is.EqualTo("UK"));
        Assert.That(author2Result.Right4?.Title, Is.EqualTo("Book C"));
        Assert.That(author2Result.Right5?.Name, Is.EqualTo("UK"));
    }

    [Test]
    public void JoinMany_Level2_Execute_ReturnsCorrectResults()
    {
        // Author -> Country -> Books (Many)
        var builder = _authorCache.Query()
            .JoinOne(_countryCache, _authorByCountryIndex);
        var builder2 = builder.JoinMany(_bookCache, _booksByAuthorIndex);
        var results = builder2.Execute();

        Assert.That(results.Count, Is.EqualTo(4));

        var list = results.ToList();

        // Author 1 has 2 books
        var author1Result = list.First(r => r.Left.Id == 1);
        Assert.That(author1Result.Right?.Name, Is.EqualTo("USA"));
        Assert.That(author1Result.Right2.Count, Is.EqualTo(2));

        // Author 4 has no books
        var author4Result = list.First(r => r.Left.Id == 4);
        Assert.That(author4Result.Right, Is.Null);
        Assert.That(author4Result.Right2.Count, Is.EqualTo(0));
    }

    [Test]
    public void JoinMany_Level2_ExecutePooled_DisposesBufferCorrectly()
    {
        // Test that pooled execution with Many join disposes correctly
        QueryResults<JoinResult<Author, Country?, QueryResults<Book>>> results;

        var builder = _authorCache.Query()
            .JoinOne(_countryCache, a => a.CountryId ?? 0);
        var builder2 = builder.JoinMany(_bookCache, _booksByAuthorIndex);
        using (results = builder2.ExecutePooled())
        {
            Assert.That(results.Count, Is.EqualTo(4));

            // Author 1 has 2 books
            var author1Result = results.First(r => r.Left.Id == 1);
            Assert.That(author1Result.Right2.Count, Is.EqualTo(2));
        }

        // After dispose, the buffer should be returned to pool
        // We can't easily verify this without inspecting ArrayPool internals,
        // but at least we verify no exceptions are thrown
    }

    [Test]
    public void MixedJoins_Level3_OneOneMany_Execute()
    {
        // Author -> Country -> Book (One) -> Books (Many for same author)
        var builder = _authorCache.Query()
            .JoinOne(_countryCache,_authorByCountryIndex);   // Right = Country
        var builder2 = builder.JoinOne(_bookCache, _bookByAuthorIndex);         // Right2 = Book (one)
        var builder3 = builder2.JoinMany(_bookCache, _booksByAuthorIndex);       // Right3 = Books (all)
        var results = builder3.Execute();

        Assert.That(results.Count, Is.EqualTo(4));

        var list = results.ToList();

        // Author 1: has country, one book, and 2 books total
        var author1Result = list.First(r => r.Left.Id == 1);
        Assert.That(author1Result.Right?.Name, Is.EqualTo("USA"));
        Assert.That(author1Result.Right2, Is.Not.Null);
        Assert.That(author1Result.Right3.Count, Is.EqualTo(2));
    }

    #endregion
}

[TestFixture]
public class IndexedInnerJoinTests {
    private InMemoryDataCache<int, Author> _authorCache = null!;
    private InMemoryDataCache<int, Book> _bookCache = null!;
    private InMemoryDataCache<int, Country> _countryCache = null!;
    private InMemoryDataCache<int, Note> _noteCache = null!;
    private InMemoryDataCache<(int, int), CountryCategory> _countryCategoryCache = null!;

    private CacheUniqueIndex<int, Book, int> _bookByAuthorIndex = null!;
    private CacheKeyValueListIndex<int, Book, int> _booksByAuthorIndex = null!;
    private CacheKeyValueListIndex<int, Note, int> _noteByAuthorIndex = null!;
    private CacheSymmetricUniqueIndex<int, Book, int> _bookByCategoryIndex = null!;
    private CacheSymmetricKeyValueListIndex<int, Author, int> _authorByCountryIndex = null;

    [SetUp]
    public void Setup() {
        _authorCache = new InMemoryDataCache<int, Author>();
        _bookCache = new InMemoryDataCache<int, Book>();
        _countryCache = new InMemoryDataCache<int, Country>();
        _noteCache = new InMemoryDataCache<int, Note>();
        _countryCategoryCache = new InMemoryDataCache<(int, int), CountryCategory>();

        _bookByAuthorIndex = _bookCache.AddKeyValueIndex((k, b) => b.AuthorId);
        _authorByCountryIndex = _authorCache.CacheSymmetricKeyValueListIndex((k, a) => a.CountryId ?? 0);
        _booksByAuthorIndex = _bookCache.CacheKeyValueListIndex((k, b) => b.AuthorId);
        _noteByAuthorIndex = _noteCache.CacheKeyValueListIndex((k, n) => n.AuthorId);
        _bookByCategoryIndex = _bookCache.AddSymmetricKeyValueIndex((k, b) => b.CategoryId);

        SeedData();
    }

    private void SeedData() {
        _countryCache.AddOrUpdate(1, new Country { Id = 1, Name = "USA" });
        _countryCache.AddOrUpdate(2, new Country { Id = 2, Name = "UK" });

        _authorCache.AddOrUpdate(1, new Author { Id = 1, Name = "Author One", CountryId = 1 });
        _authorCache.AddOrUpdate(2, new Author { Id = 2, Name = "Author Two", CountryId = 2 });
        _authorCache.AddOrUpdate(3, new Author { Id = 3, Name = "Author Three", CountryId = 1 });

        // Books: each has a unique categoryId
        // Category 1,2,3 have matching CountryCategory entries; category 4 does not
        _bookCache.AddOrUpdate(1, new Book { Id = 1, Title = "Book A", AuthorId = 1, CategoryId = 1 });
        _bookCache.AddOrUpdate(2, new Book { Id = 2, Title = "Book B", AuthorId = 1, CategoryId = 2 });
        _bookCache.AddOrUpdate(3, new Book { Id = 3, Title = "Book C", AuthorId = 2, CategoryId = 3 });
        _bookCache.AddOrUpdate(4, new Book { Id = 4, Title = "Book D", AuthorId = 3, CategoryId = 4 }); // no match

        _noteCache.AddOrUpdate(1, new Note { Id = 1, AuthorId = 1 });
        _noteCache.AddOrUpdate(2, new Note { Id = 2, AuthorId = 2 });
        _noteCache.AddOrUpdate(3, new Note { Id = 3, AuthorId = 3 });

        // CountryCategory: (categoryId, countryId)
        // foreignKeySelector (c) => (c, 1) matches only country=1
        _countryCategoryCache.AddOrUpdate((1, 1), new CountryCategory { Id = (1, 1), Title = "Cat 1 USA" });
        _countryCategoryCache.AddOrUpdate((2, 1), new CountryCategory { Id = (2, 1), Title = "Cat 2 USA" });
        _countryCategoryCache.AddOrUpdate((3, 1), new CountryCategory { Id = (3, 1), Title = "Cat 3 USA" });
        // Category 4 has no entry -> Book 4 will be filtered out by indexed inner join
    }

    #region Basic Indexed Inner Join

    [Test]
    public void IndexedInnerJoin_FiltersOutBooksWithNoMatch() {
        // Books 1,2,3 have matching categories; Book 4 (category 4) does not
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var results = builder.Execute();

        Assert.That(results.Count, Is.EqualTo(3));
        var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
        Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(results.All(r => r.Right != null), Is.True);
    }

    [Test]
    public void IndexedInnerJoin_AllMatch_ReturnsAll() {
        // Only query books 1,2,3 which all have matches
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var results = builder.Execute();

        Assert.That(results.Count, Is.EqualTo(3));
    }

    [Test]
    public void IndexedInnerJoin_NoneMatch_ReturnsEmpty() {
        // Only query book 4 which has no match
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var results = builder.Execute();

        Assert.That(results.Count, Is.EqualTo(0));
    }

    [Test]
    public void IndexedInnerJoin_RightValueIsCorrect() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var results = builder.Execute();

        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].Left.Title, Is.EqualTo("Book A"));
        Assert.That(results[0].Right!.Title, Is.EqualTo("Cat 1 USA"));
    }

    #endregion

    #region Indexed Inner Join + Additional Joins

    [Test]
    public void IndexedInnerJoin_ThenJoinMany_ResolvesSecondJoin() {
        // Indexed inner join filters books, then join notes by author
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var builder2 = builder.JoinMany(_noteCache, _noteByAuthorIndex);
        var results = builder2.Execute();

        Assert.That(results.Count, Is.EqualTo(3)); // Book 4 filtered
        // Each book's author has 1 note
        foreach (var r in results) {
            Assert.That(r.Right, Is.Not.Null, $"Book {r.Left.Id} should have CountryCategory");
            Assert.That(r.Right2.Count, Is.GreaterThan(0), $"Book {r.Left.Id} should have notes");
        }
    }

    [Test]
    [Ignore("TODO: fix joins")]
    public void IndexedInnerJoin_ThenJoinOne_ResolvesSecondJoin() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var builder2 = builder.JoinOne(_authorCache, b => b.AuthorId);
        var results = builder2.Execute();

        // Book 4 filtered; remaining 3 books join with their author
        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results.All(r => r.Right2 != null), Is.True);
    }

    #endregion

    #region Indexed Inner Join with Skip/Take

    [Test]
    public void IndexedInnerJoin_WithSkipTake_PaginatesCorrectly() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var results = builder.Execute(skip: 0, take: 2);

        Assert.That(results.Count, Is.EqualTo(2));
    }

    [Test]
    public void IndexedInnerJoin_WithSkipBeyondCount_ReturnsEmpty() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var results = builder.Execute(skip: 100, take: 10);

        Assert.That(results.Count, Is.EqualTo(0));
    }

    #endregion

    #region Indexed Inner Join with Execute Variants

    [Test]
    public void IndexedInnerJoin_ExecutePooled() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var results = builder.ExecutePooled();

        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results.All(r => r.Right != null), Is.True);
        results.Dispose();
    }

    [Test]
    public void IndexedInnerJoin_ExecuteCloned() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var results = builder.ExecuteCloned();

        Assert.That(results.Count, Is.EqualTo(3));

        // Verify clone — mutating result should not affect cache
        results[0].Left.Title = "Modified";
        var original = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { results[0].Left.Id })
            .Execute();
        Assert.That(original[0].Title, Is.Not.EqualTo("Modified"));
    }

    [Test]
    public void IndexedInnerJoin_ExecutePooledCloned_WithSkipTake() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var results = builder.ExecutePooledCloned(skip: 1, take: 1);

        Assert.That(results.Count, Is.EqualTo(1));
        results.Dispose();
    }

    #endregion

    #region Indexed Inner Join + Sort

    [Test]
    public void IndexedInnerJoin_WithSort_SortsCorrectly() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var sorted = builder.Sort(
            Comparer<JoinResult<Book, CountryCategory>>.Create((a, b) => b.Left.Id.CompareTo(a.Left.Id)));
        var results = sorted.Execute();

        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results[0].Left.Id, Is.EqualTo(3));
        Assert.That(results[1].Left.Id, Is.EqualTo(2));
        Assert.That(results[2].Left.Id, Is.EqualTo(1));
    }

    [Test]
    public void IndexedInnerJoin_WithSort_AndSkipTake() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var sorted = builder.Sort(
            Comparer<JoinResult<Book, CountryCategory>>.Create((a, b) => b.Left.Id.CompareTo(a.Left.Id)));
        var results = sorted.Execute(skip: 1, take: 1);

        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].Left.Id, Is.EqualTo(2)); // sorted desc: 3,2,1 -> skip 1 -> 2
    }

    [Test]
    public void IndexedInnerJoin_MidChainSort_ThenJoinMany() {
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .InnerJoinOne(
                _bookByCategoryIndex.Reverse,
                _countryCategoryCache,
                (c) => (c, 1));
        var sorted = builder.Sort(Comparer<JoinResult<Book, CountryCategory>>.Create(
            (a, b) => b.Left.Id.CompareTo(a.Left.Id)));
        var builder2 = sorted.JoinMany(_noteCache, _noteByAuthorIndex);
        var results = builder2.Execute(skip: 0, take: 2);

        // 3 books match, sorted desc by id: 3,2,1. Take 2 -> books 3,2
        Assert.That(results.Count, Is.EqualTo(2));
        Assert.That(results[0].Left.Id, Is.EqualTo(3));
        Assert.That(results[1].Left.Id, Is.EqualTo(2));
        // Notes still resolved on the remaining items
        Assert.That(results[0].Right2.Count, Is.GreaterThan(0));
        Assert.That(results[1].Right2.Count, Is.GreaterThan(0));
    }

    #endregion

    #region Indexed Inner Join at Level 2

    [Test]
    public void JoinMany_ThenIndexedInnerJoin_FiltersAtLevel2() {
        // First join notes by author (non-inner), then indexed inner join to country category
        // This tests the T4-generated level-2 indexed inner support
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .JoinMany(_noteCache, _noteByAuthorIndex);
        var builder2 = builder.InnerJoinOne(
            _bookByCategoryIndex.Reverse,
            _countryCategoryCache,
            (c) => (c, 1));
        var results = builder2.Execute();

        // Book 4 filtered at level 2 because category 4 has no CountryCategory match
        Assert.That(results.Count, Is.EqualTo(3));
        // Notes (Right) still resolved for remaining books
        foreach (var r in results) {
            Assert.That(r.Right.Count, Is.GreaterThan(0), $"Book {r.Left.Id} should have notes");
        }
        // CountryCategory (Right2) also resolved
        Assert.That(results.All(r => r.Right2 != null), Is.True);
    }

    [Test]
    [Ignore("TODO: fix joins")]
    public void JoinOne_ThenIndexedInnerJoin_FiltersAtLevel2() {
        // First join author (non-inner), then indexed inner join to country category
        var builder = _bookCache.Query()
            .UseIndex(_bookCache.KeyIndex, new[] { 1, 2, 3, 4 })
            .JoinOne(_authorCache, b => b.AuthorId);
        var builder2 = builder.InnerJoinOne(
            _bookByCategoryIndex.Reverse,
            _countryCategoryCache,
            (c) => (c, 1));
        var results = builder2.Execute();

        // Book 4 filtered at level 2; remaining 3 have both author and country category
        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results.All(r => r.Right != null), Is.True); // Author
        Assert.That(results.All(r => r.Right2 != null), Is.True); // CountryCategory
    }

    [Test]
    public void FunWithNewBuilder() {
	    var r =
		    _authorCache.Query()
			    .Sort(Comparer<Author>.Create((a, b) => b.Id.CompareTo(a.Id)))
	    .JoinOne(_countryCache, _authorByCountryIndex)
	    // .Sort(Comparer<JoinResult<Author, Book>>.Create((a, b) => a.Left.Id.CompareTo(b.Left.Id)))
	    .Execute();
	    ;

    }


    [Test]
    public void FunWithNewBuilderSort() {
	    var r =
		    _authorCache.Query()
			    .Sort(Comparer<Author>.Create((a, b) => b.Id.CompareTo(a.Id)))
			    // .Sort(Comparer<JoinResult<Author, Book>>.Create((a, b) => a.Left.Id.CompareTo(b.Left.Id)))
			    .Execute();
	    ;

    }

    #endregion
}

#endif
