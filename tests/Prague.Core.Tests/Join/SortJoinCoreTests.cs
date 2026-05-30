namespace Prague.Core.Tests.Join;

using System.Collections.Generic;
using System.Linq;
using Prague.Core;
using NUnit.Framework;

// ── Domain ──────────────────────────────────────────────────────────────────
// Sort + Join matrix coverage at the Core layer (raw POCOs, no codegen, direct
// JoinOne / JoinMany). In Core, join extensions only require IBaseJoinable on
// the discriminator — and SortedQuery<T> implements it — so every combination
// here compiles today.

internal sealed class SnAuthor : ICacheEquatable<SnAuthor>, ICacheClonable<SnAuthor> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public bool CacheEquals(SnAuthor? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public SnAuthor Clone() => new() { Id = Id, Name = Name };
}

internal sealed class SnProfile : ICacheEquatable<SnProfile>, ICacheClonable<SnProfile> {
	public int Id { get; init; }
	public string Bio { get; init; } = "";
	public bool CacheEquals(SnProfile? other) => other is not null && other.Id == Id && other.Bio == Bio;
	public int CacheGetHashCode() => HashCode.Combine(Id, Bio);
	public SnProfile Clone() => new() { Id = Id, Bio = Bio };
}

internal sealed class SnBook : ICacheEquatable<SnBook>, ICacheClonable<SnBook> {
	public int Id { get; init; }
	public int AuthorId { get; init; }
	public string Title { get; init; } = "";
	public bool CacheEquals(SnBook? other) => other is not null && other.Id == Id && other.AuthorId == AuthorId && other.Title == Title;
	public int CacheGetHashCode() => HashCode.Combine(Id, AuthorId, Title);
	public SnBook Clone() => new() { Id = Id, AuthorId = AuthorId, Title = Title };
}

internal sealed class SnAward : ICacheEquatable<SnAward>, ICacheClonable<SnAward> {
	public int Id { get; init; }
	public int AuthorId { get; init; }
	public string AwardName { get; init; } = "";
	public bool CacheEquals(SnAward? other) => other is not null && other.Id == Id && other.AuthorId == AuthorId && other.AwardName == AwardName;
	public int CacheGetHashCode() => HashCode.Combine(Id, AuthorId, AwardName);
	public SnAward Clone() => new() { Id = Id, AuthorId = AuthorId, AwardName = AwardName };
}

// Right-side entity with its own PK + a separate AuthorId carrying a Unique index.
// Models the AuthorProfile shape from the Generated tests so we can repro the
// Sort + JoinOne(rightCache, rightUniqueIndex) failure at the Core layer.
internal sealed class SnInfo : ICacheEquatable<SnInfo>, ICacheClonable<SnInfo> {
	public int Id { get; init; }
	public int AuthorId { get; init; }
	public string Bio { get; init; } = "";
	public bool CacheEquals(SnInfo? other) => other is not null && other.Id == Id && other.AuthorId == AuthorId && other.Bio == Bio;
	public int CacheGetHashCode() => HashCode.Combine(Id, AuthorId, Bio);
	public SnInfo Clone() => new() { Id = Id, AuthorId = AuthorId, Bio = Bio };
}

// Comparer by Id descending — easy to verify ordering.
internal sealed class AuthorByIdDesc : IComparer<SnAuthor> {
	public int Compare(SnAuthor? x, SnAuthor? y) => (y?.Id ?? 0).CompareTo(x?.Id ?? 0);
}

internal sealed class JoinedByLeftIdDesc<T> : IComparer<JoinResult<SnAuthor, T>> {
	public int Compare(JoinResult<SnAuthor, T> x, JoinResult<SnAuthor, T> y) => y.Left.Id.CompareTo(x.Left.Id);
}

internal sealed class JoinedByLeftIdDesc2<T1, T2> : IComparer<JoinResult<SnAuthor, T1, T2>> {
	public int Compare(JoinResult<SnAuthor, T1, T2> x, JoinResult<SnAuthor, T1, T2> y) => y.Left.Id.CompareTo(x.Left.Id);
}

[TestFixture]
public class SortJoinCoreTests {
	private InMemoryDataCache<int, SnAuthor> _authors = null!;
	private InMemoryDataCache<int, SnProfile> _profiles = null!;
	private InMemoryDataCache<int, SnBook> _books = null!;
	private InMemoryDataCache<int, SnAward> _awards = null!;
	private InMemoryDataCache<int, SnInfo> _infos = null!;

	private CacheKeyValueListIndex<int, SnBook, int> _bookAuthorIdx = null!;
	private CacheKeyValueListIndex<int, SnAward, int> _awardAuthorIdx = null!;
	private CacheKeyValueIndex<int, SnInfo, int> _infoAuthorIdUniqueIdx = null!;

	[SetUp]
	public void SetUp() {
		_authors = new InMemoryDataCache<int, SnAuthor>();
		_profiles = new InMemoryDataCache<int, SnProfile>();
		_books = new InMemoryDataCache<int, SnBook>();
		_awards = new InMemoryDataCache<int, SnAward>();
		_infos = new InMemoryDataCache<int, SnInfo>();

		_bookAuthorIdx = _books.CacheKeyValueListIndex<int>((_, v) => v.AuthorId);
		_awardAuthorIdx = _awards.CacheKeyValueListIndex<int>((_, v) => v.AuthorId);
		_infoAuthorIdUniqueIdx = _infos.AddKeyValueIndex<int>((_, v) => v.AuthorId);

		_authors.AddOrUpdate(1, new SnAuthor { Id = 1, Name = "Tolkien" });
		_authors.AddOrUpdate(2, new SnAuthor { Id = 2, Name = "Asimov" });
		_authors.AddOrUpdate(3, new SnAuthor { Id = 3, Name = "Lewis" });

		_profiles.AddOrUpdate(1, new SnProfile { Id = 1, Bio = "UK fantasy" });
		_profiles.AddOrUpdate(2, new SnProfile { Id = 2, Bio = "US sci-fi" });
		// no profile for 3

		_books.AddOrUpdate(101, new SnBook { Id = 101, AuthorId = 1, Title = "Hobbit" });
		_books.AddOrUpdate(102, new SnBook { Id = 102, AuthorId = 1, Title = "LOTR" });
		_books.AddOrUpdate(201, new SnBook { Id = 201, AuthorId = 2, Title = "Foundation" });

		_awards.AddOrUpdate(901, new SnAward { Id = 901, AuthorId = 1, AwardName = "Hugo" });
		_awards.AddOrUpdate(902, new SnAward { Id = 902, AuthorId = 2, AwardName = "Nebula" });

		// SnInfo PK is its own Id; AuthorId is a unique FK back to SnAuthor.
		// Author 3 has no info → null Right after JoinOne(_infos, _infoAuthorIdUniqueIdx).
		_infos.AddOrUpdate(701, new SnInfo { Id = 701, AuthorId = 1, Bio = "UK fantasy" });
		_infos.AddOrUpdate(702, new SnInfo { Id = 702, AuthorId = 2, Bio = "US sci-fi" });
	}

	// ── Sort only (level 0) ───────────────────────────────────────────────────

	[Test]
	public void Sort_NoJoins_Execute_OrderedByIdDesc() {
		var results = _authors.Query().Sort(new AuthorByIdDesc()).Execute();
		Assert.That(results.Select(a => a.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void Sort_NoJoins_ExecutePooled_OrderedByIdDesc() {
		using var results = _authors.Query().Sort(new AuthorByIdDesc()).ExecutePooled();
		Assert.That(results.Select(a => a.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void Sort_NoJoins_ExecuteCloned_OrderedAndIsolated() {
		var results = _authors.Query().Sort(new AuthorByIdDesc()).ExecuteCloned();
		Assert.That(results.Select(a => a.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void Sort_NoJoins_ExecutePooledCloned_OrderedAndIsolated() {
		using var results = _authors.Query().Sort(new AuthorByIdDesc()).ExecutePooledCloned();
		Assert.That(results.Select(a => a.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void Sort_NoJoins_SkipTake_AppliesAfterOrdering() {
		using var results = _authors.Query().Sort(new AuthorByIdDesc()).ExecutePooled(skip: 1, take: 1);
		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.First().Id, Is.EqualTo(2));
	}

	// ── Sort then joins (TDiscriminator becomes SortedQuery<...>) ────────────

	[Test]
	public void Sort_Then_JoinOne_Profile() {
		var results = _authors.Query()
			.Sort(new AuthorByIdDesc())
			.JoinOne(_profiles)
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		Assert.That(results.First(r => r.Left.Id == 1).Right!.Bio, Is.EqualTo("UK fantasy"));
		Assert.That(results.First(r => r.Left.Id == 3).Right, Is.Null);
	}

	[Test]
	public void Sort_Then_JoinMany_Books() {
		var results = _authors.Query()
			.Sort(new AuthorByIdDesc())
			.JoinMany(_books, _bookAuthorIdx)
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		Assert.That(results.First(r => r.Left.Id == 1).Right.Count, Is.EqualTo(2));
		Assert.That(results.First(r => r.Left.Id == 3).Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void Sort_Then_JoinMany_Books_Then_JoinMany_Awards() {
		var results = _authors.Query()
			.Sort(new AuthorByIdDesc())
			.JoinMany(_books, _bookAuthorIdx)
			.JoinMany(_awards, _awardAuthorIdx)
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));    // books
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));   // awards
		Assert.That(byId[3].Right.Count, Is.EqualTo(0));
		Assert.That(byId[3].Right2.Count, Is.EqualTo(0));
	}

	// ── Joins then Sort (Sort at level 1+) ───────────────────────────────────

	[Test]
	public void JoinMany_Books_Then_Sort_AtLevel1() {
		var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdx)
			.Sort(new JoinedByLeftIdDesc<QueryResults<SnBook>>())
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		Assert.That(results.First(r => r.Left.Id == 1).Right.Count, Is.EqualTo(2));
	}

	[Test]
	public void JoinMany_Books_Then_JoinMany_Awards_Then_Sort_AtLevel2() {
		var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdx)
			.JoinMany(_awards, _awardAuthorIdx)
			.Sort(new JoinedByLeftIdDesc2<QueryResults<SnBook>, QueryResults<SnAward>>())
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[2].Right.Count, Is.EqualTo(1));
		Assert.That(byId[2].Right2.Count, Is.EqualTo(1));
	}

	[Test]
	public void JoinMany_Books_Then_Sort_AtLevel1_ExecutePooled() {
		using var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdx)
			.Sort(new JoinedByLeftIdDesc<QueryResults<SnBook>>())
			.ExecutePooled();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void JoinMany_Books_Then_Sort_AtLevel1_ExecuteCloned() {
		var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdx)
			.Sort(new JoinedByLeftIdDesc<QueryResults<SnBook>>())
			.ExecuteCloned();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void JoinMany_Books_Then_Sort_AtLevel1_ExecutePooledCloned() {
		using var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdx)
			.Sort(new JoinedByLeftIdDesc<QueryResults<SnBook>>())
			.ExecutePooledCloned();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}

	[Test]
	public void JoinMany_Books_Then_Sort_Skip_Take_AtLevel1() {
		using var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdx)
			.Sort(new JoinedByLeftIdDesc<QueryResults<SnBook>>())
			.ExecutePooled(skip: 1, take: 1);

		Assert.That(results.Count, Is.EqualTo(1));
		Assert.That(results.First().Left.Id, Is.EqualTo(2));
	}

	// ── Sort interleaved between joins ───────────────────────────────────────

	[Test]
	public void JoinMany_Books_Then_Sort_AtLevel1_Then_JoinMany_Awards() {
		var results = _authors.Query()
			.JoinMany(_books, _bookAuthorIdx)
			.Sort(new JoinedByLeftIdDesc<QueryResults<SnBook>>())
			.JoinMany(_awards, _awardAuthorIdx)
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right.Count, Is.EqualTo(2));
		Assert.That(byId[1].Right2.Count, Is.EqualTo(1));
	}

	// ── Reproducer: Sort + JoinOne(rightCache, rightUniqueIndex) ─────────────
	// Same shape as the Generated failure Sort_Then_JoinWithAuthorProfile_Execute.

	[Test]
	public void Sort_Then_JoinOne_RightUniqueIndex_Info() {
		var results = _authors.Query()
			.Sort(new AuthorByIdDesc())
			.JoinOne(_infos, _infoAuthorIdUniqueIdx)
			.Execute();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
		Assert.That(results.First(r => r.Left.Id == 1).Right!.Bio, Is.EqualTo("UK fantasy"));
		Assert.That(results.First(r => r.Left.Id == 2).Right!.Bio, Is.EqualTo("US sci-fi"));
		Assert.That(results.First(r => r.Left.Id == 3).Right, Is.Null);
	}

	[Test]
	public void JoinOne_RightUniqueIndex_Info_NoSort_Baseline() {
		// Sanity baseline — must pass before / after the fix.
		var results = _authors.Query()
			.JoinOne(_infos, _infoAuthorIdUniqueIdx)
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results.First(r => r.Left.Id == 1).Right!.Bio, Is.EqualTo("UK fantasy"));
		Assert.That(results.First(r => r.Left.Id == 3).Right, Is.Null);
	}

	[Test]
	public void Sort_Then_JoinMany_Books_Then_JoinMany_Awards_ExecutePooled() {
		using var results = _authors.Query()
			.Sort(new AuthorByIdDesc())
			.JoinMany(_books, _bookAuthorIdx)
			.JoinMany(_awards, _awardAuthorIdx)
			.ExecutePooled();

		Assert.That(results.Select(r => r.Left.Id).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
	}
}
