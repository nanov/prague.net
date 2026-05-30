namespace Prague.Core.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// MnAuthor (left, PK only) and MnBook (right, has a list-valued FK AuthorId
// pointing back to the left's PK). All hand-rolled — no [DataCache], no codegen.
// The right-side list index is added manually via AddKeyValueListIndex.

internal sealed class MnAuthor : ICacheEquatable<MnAuthor>, ICacheClonable<MnAuthor> {
	public int Id { get; init; }
	public string Name { get; init; } = "";

	public bool CacheEquals(MnAuthor? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public MnAuthor Clone() => new() { Id = Id, Name = Name };
}

internal sealed class MnBook : ICacheEquatable<MnBook>, ICacheClonable<MnBook> {
	public int Id { get; init; }
	public int AuthorId { get; init; }
	public string Title { get; init; } = "";

	public bool CacheEquals(MnBook? other) => other is not null && other.Id == Id && other.AuthorId == AuthorId && other.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, AuthorId, Title);
	public MnBook Clone() => new() { Id = Id, AuthorId = AuthorId, Title = Title };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class JoinManyRightListIndexCoreTests {
	private InMemoryDataCache<int, MnAuthor> _authorCache = null!;
	private InMemoryDataCache<int, MnBook> _bookCache = null!;
	// List-valued right-side index keyed by MnBook.AuthorId.
	private CacheKeyValueListIndex<int, MnBook, int> _authorIdIndex = null!;

	[SetUp]
	public void SetUp() {
		_authorCache = new InMemoryDataCache<int, MnAuthor>();
		_bookCache = new InMemoryDataCache<int, MnBook>();
		_authorIdIndex = _bookCache.CacheKeyValueListIndex<int>((_, v) => v.AuthorId);
	}

	// ── Test 1: full match — every author has books ──────────────────────────

	[Test]
	public void JoinMany_AuthorToBooks_FullMatch_FansOut() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Tolkien" });
		_authorCache.AddOrUpdate(2, new MnAuthor { Id = 2, Name = "Asimov" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(102, new MnBook { Id = 102, AuthorId = 1, Title = "LOTR" });
		_bookCache.AddOrUpdate(201, new MnBook { Id = 201, AuthorId = 2, Title = "Foundation" });

		var results = _authorCache.Query()
			.JoinMany(_bookCache, _authorIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		var tolkienTitles = byId[1].Right.Select(b => b.Title).OrderBy(t => t).ToArray();
		Assert.That(tolkienTitles, Is.EqualTo(new[] { "Hobbit", "LOTR" }));

		Assert.That(byId[2].Right.Count, Is.EqualTo(1));
		Assert.That(byId[2].Right.First().Title, Is.EqualTo("Foundation"));
	}

	// ── Test 2: orphan author — empty QueryResults ───────────────────────────

	[Test]
	public void JoinMany_AuthorWithNoBooks_GetsEmptyQueryResults() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Tolkien" });
		_authorCache.AddOrUpdate(2, new MnAuthor { Id = 2, Name = "OrphanAuthor" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		// No books for author 2.

		var results = _authorCache.Query()
			.JoinMany(_bookCache, _authorIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byId[1].Right.Count, Is.EqualTo(1));
		Assert.That(byId[2].Right.Count, Is.EqualTo(0), "Orphan author should have empty QueryResults");
	}

	// ── Test 3: filter narrows the right side ────────────────────────────────

	[Test]
	public void JoinMany_WithFilter_NarrowsBooksPerAuthor() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Tolkien" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(102, new MnBook { Id = 102, AuthorId = 1, Title = "LOTR" });
		_bookCache.AddOrUpdate(103, new MnBook { Id = 103, AuthorId = 1, Title = "Silmarillion" });

		var results = _authorCache.Query()
			.JoinMany(_bookCache, _authorIdIndex,
				q => q.Where(b => b.Title.StartsWith("H") || b.Title.StartsWith("L")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		var tolkien = results.Single();
		var titles = tolkien.Right.Select(b => b.Title).OrderBy(t => t).ToArray();
		Assert.That(titles, Is.EqualTo(new[] { "Hobbit", "LOTR" }));
	}

	// ── Test 4: filter + arg (zero-alloc static lambda) ──────────────────────

	[Test]
	public void JoinMany_WithFilterAndArg_StaticLambda() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Tolkien" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(102, new MnBook { Id = 102, AuthorId = 1, Title = "Foundation" });

		const string prefix = "H";
		var results = _authorCache.Query()
			.JoinMany(_bookCache, _authorIdIndex,
				static (q, p) => q.Where(b => b.Title.StartsWith(p)),
				prefix)
			.Execute();

		var tolkien = results.Single();
		Assert.That(tolkien.Right.Count, Is.EqualTo(1));
		Assert.That(tolkien.Right.First().Title, Is.EqualTo("Hobbit"));
	}

	// ── Test 5: key selector (cross-key-type) ────────────────────────────────
	// Author PK is long, Book.AuthorId is int. Selector long → int bridges them.

	// ── Test 6: InnerJoinMany drops orphans ───────────────────────────────

	[Test]
	public void InnerJoinMany_DropsAuthorsWithNoBooks() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Tolkien" });
		_authorCache.AddOrUpdate(2, new MnAuthor { Id = 2, Name = "OrphanAuthor" });
		_authorCache.AddOrUpdate(3, new MnAuthor { Id = 3, Name = "Asimov" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(301, new MnBook { Id = 301, AuthorId = 3, Title = "Foundation" });
		// No books for author 2.

		var results = _authorCache.Query()
			.InnerJoinMany(_bookCache, _authorIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(i => i).ToArray();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
		Assert.That(results.All(r => r.Right.Count > 0), Is.True);
	}

	// ── Test 7: InnerJoinMany with filter that drops all rights for some left

	[Test]
	public void InnerJoinMany_WithFilter_DropsAuthorsWithNoMatchingBooks() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Tolkien" });
		_authorCache.AddOrUpdate(2, new MnAuthor { Id = 2, Name = "Asimov" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(102, new MnBook { Id = 102, AuthorId = 1, Title = "LOTR" });
		_bookCache.AddOrUpdate(201, new MnBook { Id = 201, AuthorId = 2, Title = "Foundation" });
		_bookCache.AddOrUpdate(202, new MnBook { Id = 202, AuthorId = 2, Title = "I, Robot" });

		// Filter keeps only titles starting with "H" — Tolkien has Hobbit, Asimov has nothing
		var results = _authorCache.Query()
			.InnerJoinMany(_bookCache, _authorIdIndex,
				q => q.Where(b => b.Title.StartsWith("H")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.Single().Left.Name, Is.EqualTo("Tolkien"));
		Assert.That(results.Single().Right.Count, Is.EqualTo(1));
		Assert.That(results.Single().Right.First().Title, Is.EqualTo("Hobbit"));
	}

	// ── Test 8: InnerJoinMany with filter+arg ─────────────────────────────

	[Test]
	public void InnerJoinMany_WithFilterAndArg_StaticLambda() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Tolkien" });
		_authorCache.AddOrUpdate(2, new MnAuthor { Id = 2, Name = "Asimov" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(201, new MnBook { Id = 201, AuthorId = 2, Title = "Foundation" });

		const string prefix = "Hob";
		var results = _authorCache.Query()
			.InnerJoinMany(_bookCache, _authorIdIndex,
				static (q, p) => q.Where(b => b.Title.StartsWith(p)),
				prefix)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.Single().Left.Id, Is.EqualTo(1));
	}

	[Test]
	public void JoinMany_WithKeySelector_BridgesKeyTypes() {
		var longAuthorCache = new InMemoryDataCache<long, MnAuthor>();
		longAuthorCache.AddOrUpdate(1L, new MnAuthor { Id = 1, Name = "Tolkien" });
		longAuthorCache.AddOrUpdate(2L, new MnAuthor { Id = 2, Name = "Asimov" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(201, new MnBook { Id = 201, AuthorId = 2, Title = "Foundation" });

		var results = longAuthorCache.Query()
			.JoinMany(static (long k) => (int)k, _bookCache, _authorIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Single().Title, Is.EqualTo("Hobbit"));
		Assert.That(byId[2].Right.Single().Title, Is.EqualTo("Foundation"));
	}

	// ── Edge cases ────────────────────────────────────────────────────────────

	[Test]
	public void JoinMany_EmptyLeftCache_ReturnsEmpty() {
		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });

		var results = _authorCache.Query()
			.JoinMany(_bookCache, _authorIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_EmptyRightCache_OuterAllEmptyResults() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Tolkien" });
		_authorCache.AddOrUpdate(2, new MnAuthor { Id = 2, Name = "Asimov" });

		var results = _authorCache.Query()
			.JoinMany(_bookCache, _authorIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(2));
		Assert.That(results.All(r => r.Right.Count == 0), Is.True);
	}

	[Test]
	public void InnerJoinMany_EmptyRightCache_ReturnsEmpty() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Tolkien" });
		_authorCache.AddOrUpdate(2, new MnAuthor { Id = 2, Name = "Asimov" });

		var results = _authorCache.Query()
			.InnerJoinMany(_bookCache, _authorIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_LargeFanOut_AllRightsAttached() {
		_authorCache.AddOrUpdate(1, new MnAuthor { Id = 1, Name = "Asimov" });
		for (var i = 0; i < 100; i++)
			_bookCache.AddOrUpdate(1000 + i, new MnBook { Id = 1000 + i, AuthorId = 1, Title = $"Book {i}" });

		var results = _authorCache.Query()
			.JoinMany(_bookCache, _authorIdIndex)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.Single().Right.Count, Is.EqualTo(100));
	}

	// ── Selector + filter combinations (MS2 / MS3 / MSA2 / MSA3) ─────────────
	// The identity-selector × filter tests cover M1/M2/M3 — these cover the
	// remaining 6 surface overloads (selector × filter shapes).

	[Test]
	public void JoinMany_KeySelector_AndFilter_NarrowsBoth() {
		var longAuthors = new InMemoryDataCache<long, MnAuthor>();
		longAuthors.AddOrUpdate(1L, new MnAuthor { Id = 1, Name = "Tolkien" });
		longAuthors.AddOrUpdate(2L, new MnAuthor { Id = 2, Name = "Asimov" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(102, new MnBook { Id = 102, AuthorId = 1, Title = "LOTR" });
		_bookCache.AddOrUpdate(201, new MnBook { Id = 201, AuthorId = 2, Title = "Foundation" });

		var results = longAuthors.Query()
			.JoinMany(static (long k) => (int)k, _bookCache, _authorIdIndex,
				q => q.Where(b => b.Title.StartsWith("H")))
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Single().Title, Is.EqualTo("Hobbit"));
		Assert.That(byId[2].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void JoinMany_KeySelector_AndFilterWithArg_StaticLambda() {
		var longAuthors = new InMemoryDataCache<long, MnAuthor>();
		longAuthors.AddOrUpdate(1L, new MnAuthor { Id = 1, Name = "Tolkien" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(102, new MnBook { Id = 102, AuthorId = 1, Title = "LOTR" });

		const string prefix = "Hob";
		var results = longAuthors.Query()
			.JoinMany(static (long k) => (int)k, _bookCache, _authorIdIndex,
				static (q, p) => q.Where(b => b.Title.StartsWith(p)),
				prefix)
			.Execute();

		Assert.That(results.Single().Right.Single().Title, Is.EqualTo("Hobbit"));
	}

	[Test]
	public void JoinMany_KeySelectorWithArg_NoFilter() {
		var longAuthors = new InMemoryDataCache<long, MnAuthor>();
		longAuthors.AddOrUpdate(100L, new MnAuthor { Id = 1, Name = "Tolkien" });
		longAuthors.AddOrUpdate(200L, new MnAuthor { Id = 2, Name = "Asimov" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(201, new MnBook { Id = 201, AuthorId = 2, Title = "Foundation" });

		// Selector uses `divisor` arg to extract: longKey / divisor = bookAuthorId
		const long divisor = 100L;
		var results = longAuthors.Query()
			.JoinMany(static (long k, long d) => (int)(k / d), divisor, _bookCache, _authorIdIndex)
			.Execute();

		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Single().Title, Is.EqualTo("Hobbit"));
		Assert.That(byId[2].Right.Single().Title, Is.EqualTo("Foundation"));
	}

	[Test]
	public void JoinMany_KeySelectorWithArg_AndFilter() {
		var longAuthors = new InMemoryDataCache<long, MnAuthor>();
		longAuthors.AddOrUpdate(100L, new MnAuthor { Id = 1, Name = "Tolkien" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(102, new MnBook { Id = 102, AuthorId = 1, Title = "LOTR" });

		const long divisor = 100L;
		var results = longAuthors.Query()
			.JoinMany(static (long k, long d) => (int)(k / d), divisor, _bookCache, _authorIdIndex,
				q => q.Where(b => b.Title.StartsWith("H")))
			.Execute();

		Assert.That(results.Single().Right.Single().Title, Is.EqualTo("Hobbit"));
	}

	[Test]
	public void JoinMany_KeySelectorWithArg_AndFilterWithArg() {
		var longAuthors = new InMemoryDataCache<long, MnAuthor>();
		longAuthors.AddOrUpdate(100L, new MnAuthor { Id = 1, Name = "Tolkien" });

		_bookCache.AddOrUpdate(101, new MnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_bookCache.AddOrUpdate(102, new MnBook { Id = 102, AuthorId = 1, Title = "LOTR" });

		const long divisor = 100L;
		const string prefix = "L";
		var results = longAuthors.Query()
			.JoinMany(static (long k, long d) => (int)(k / d), divisor, _bookCache, _authorIdIndex,
				static (q, p) => q.Where(b => b.Title.StartsWith(p)),
				prefix)
			.Execute();

		Assert.That(results.Single().Right.Single().Title, Is.EqualTo("LOTR"));
	}
}
