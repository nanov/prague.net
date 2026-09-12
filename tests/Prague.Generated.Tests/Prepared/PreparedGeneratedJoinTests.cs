namespace Prague.Generated.Tests.Prepared;

using System.Text;
using Prague.Core;
using Prague.Generated.Tests.Join;
using NUnit.Framework;
using static PreparedParity;

// Generated FK JoinWith{T} / InnerJoinWith{T} on a prepared builder, eager vs prepared from the same
// inputs: reverse one-to-many, reverse one-to-one, forward many-to-one, filtered right side, after a
// Sort / SortBounded, with a parameterized left lane, Count parity. The emitted JoinWith overloads bind
// on ICandidatesExecutor (the recorder implements it) and on IBaseJoinable + ICacheCarrier<XxxCache>
// (the top-level prepared discriminator implements both), so no prepared twin is emitted for them.
[TestFixture]
public class PreparedGeneratedJoinTests {
	private DataCacheRegistry _registry = null!;
	private AuthorCache _authors = null!;
	private BookCache _books = null!;
	private AuthorProfileCache _profiles = null!;
	private M2OAuthorCache _m2oAuthors = null!;
	private M2OBookCache _m2oBooks = null!;

	private readonly struct AuthorByIdDesc : IComparer<Author> {
		public int Compare(Author? x, Author? y) => (y?.Id ?? 0).CompareTo(x?.Id ?? 0);
	}

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<AuthorCache>()
			.Register<BookCache>()
			.Register<AuthorProfileCache>()
			.Register<M2OAuthorCache>()
			.Register<M2OPublisherCache>()
			.Register<M2OBookCache>()
			.Register<M2OAwardCache>()
			.Build();
		_authors = _registry.GetCache<AuthorCache>();
		_books = _registry.GetCache<BookCache>();
		_profiles = _registry.GetCache<AuthorProfileCache>();
		_m2oAuthors = _registry.GetCache<M2OAuthorCache>();
		_m2oBooks = _registry.GetCache<M2OBookCache>();

		// 12 authors; author 11 has no books, authors 0..5 have profiles; 40 books over authors 0..10 plus 2 orphans.
		for (var a = 0; a < 12; a++)
			_authors.AddOrUpdate(new Author { Id = a, Name = $"A{a}", Country = a % 2 == 0 ? "UK" : "US" });
		for (var b = 0; b < 40; b++)
			_books.AddOrUpdate(new Book { Id = b, Title = $"B{b}", AuthorId = b % 11, Year = 1990 + b % 7 });
		_books.AddOrUpdate(new Book { Id = 100, Title = "orphan", AuthorId = 500, Year = 2000 });
		_books.AddOrUpdate(new Book { Id = 101, Title = "orphan", AuthorId = 501, Year = 2001 });
		for (var p = 0; p < 6; p++)
			_profiles.AddOrUpdate(new AuthorProfile { Id = p, AuthorId = p, Bio = $"bio{p}", Website = "" });

		for (var a = 1; a <= 3; a++)
			_m2oAuthors.AddOrUpdate(new M2OAuthor { Id = a, Name = $"M{a}" });
		for (var b = 0; b < 12; b++)
			_m2oBooks.AddOrUpdate(new M2OBook { Id = b, Title = $"MB{b}", AuthorId = b % 4, PublisherId = 100 }); // AuthorId 0 does not exist
	}

	private static string ManyRow(JoinResult<Author, QueryResults<Book>> r) {
		var sb = new StringBuilder().Append(r.Left.Id).Append('|');
		var ids = new int[r.Right.Count];
		for (var i = 0; i < ids.Length; i++) ids[i] = r.Right[i].Id;
		Array.Sort(ids);
		return sb.AppendJoin(',', ids).ToString();
	}

	private static string OneRow(JoinResult<Author, AuthorProfile?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id.ToString())}";
	private static string ForwardRow(JoinResult<M2OBook, M2OAuthor?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id.ToString())}";

	[Test]
	public void ReverseOneToMany_Outer_LikeEager() {
		var prepared = _authors.Prepare().JoinWithBook().Build();
		AssertSame(_authors.Query().JoinWithBook().Execute(), prepared.Execute(), ManyRow);
		Assert.That(prepared.Count(), Is.EqualTo(_authors.Query().JoinWithBook().Count()));
	}

	[Test]
	public void ReverseOneToMany_Inner_LikeEager() {
		var prepared = _authors.Prepare().InnerJoinWithBook().Build();
		AssertSame(_authors.Query().InnerJoinWithBook().Execute(), prepared.Execute(), ManyRow);
		Assert.That(prepared.Count(), Is.EqualTo(_authors.Query().InnerJoinWithBook().Count()));
	}

	[Test]
	public void ReverseOneToMany_FilteredRight_LikeEager() {
		var prepared = _authors.Prepare().JoinWithBook(static b => b.WithAuthorId(new[] { 1, 2, 3 })).Build();
		AssertSame(_authors.Query().JoinWithBook(static b => b.WithAuthorId(new[] { 1, 2, 3 })).Execute(), prepared.Execute(), ManyRow);
	}

	[Test]
	public void ReverseOneToOne_Outer_And_Inner_LikeEager() {
		AssertSame(_authors.Query().JoinWithAuthorProfile().Execute(), _authors.Prepare().JoinWithAuthorProfile().Build().Execute(), OneRow);
		AssertSame(_authors.Query().InnerJoinWithAuthorProfile().Execute(), _authors.Prepare().InnerJoinWithAuthorProfile().Build().Execute(), OneRow);
	}

	[Test]
	public void ParameterizedLeftLane_ThenJoin_LikeEager() {
		var prepared = _authors.Prepare<int>().WithId(static id => id).JoinWithBook().Build();
		foreach (var id in new[] { 0, 7, 11, 99 })
			AssertSame(_authors.Query().WithId(id).JoinWithBook().Execute(), prepared.Execute(id), ManyRow);

		var inner = _authors.Prepare<int>().WithId(static id => id).InnerJoinWithAuthorProfile().Build();
		foreach (var id in new[] { 2, 9 })
			AssertSame(_authors.Query().WithId(id).InnerJoinWithAuthorProfile().Execute(), inner.Execute(id), OneRow);
	}

	[Test]
	public void ForwardManyToOne_Outer_Inner_Parameterized_LikeEager() {
		AssertSame(_m2oBooks.Query().JoinWithM2OAuthor().Execute(), _m2oBooks.Prepare().JoinWithM2OAuthor().Build().Execute(), ForwardRow);
		AssertSame(_m2oBooks.Query().InnerJoinWithM2OAuthor().Execute(), _m2oBooks.Prepare().InnerJoinWithM2OAuthor().Build().Execute(), ForwardRow);

		var prepared = _m2oBooks.Prepare<int>().WithAuthorId(static a => a).JoinWithM2OAuthor().Build();
		foreach (var author in new[] { 0, 1, 3 })
			AssertSame(_m2oBooks.Query().WithAuthorId(author).JoinWithM2OAuthor().Execute(), prepared.Execute(author), ForwardRow);
	}

	[Test]
	public void Sort_ThenJoin_Pages_LikeEager() {
		var cmp = new AuthorByIdDesc();
		var classic = _authors.Prepare().Sort(cmp).JoinWithBook().Build();
		AssertSame(_authors.Query().Sort(cmp).JoinWithBook().Execute(), classic.Execute(), ManyRow);
		AssertSame(_authors.Query().Sort(cmp).JoinWithBook().Execute(2, 4), classic.Execute(skip: 2, take: 4), ManyRow);

		var bounded = _authors.Prepare().SortBounded(cmp).JoinWithBook().Build();
		AssertSame(_authors.Query().SortBounded(cmp).JoinWithBook().Execute(0, 3), bounded.Execute(skip: 0, take: 3), ManyRow);
		AssertSame(_authors.Query().SortBounded(cmp).JoinWithBook().Execute(3, 3), bounded.Execute(skip: 3, take: 3), ManyRow);
		AssertSame(_authors.Query().SortBounded(cmp).JoinWithAuthorProfile().Execute(1, 5), _authors.Prepare().SortBounded(cmp).JoinWithAuthorProfile().Build().Execute(1, 5), OneRow);
	}

	[Test]
	public void Join_ReusedAcrossMutations_LikeEager() {
		var prepared = _authors.Prepare<int>().WithId(static id => id).InnerJoinWithBook().Build();
		AssertSame(_authors.Query().WithId(11).InnerJoinWithBook().Execute(), prepared.Execute(11), ManyRow);
		_books.AddOrUpdate(new Book { Id = 200, Title = "late", AuthorId = 11, Year = 2024 });
		AssertSame(_authors.Query().WithId(11).InnerJoinWithBook().Execute(), prepared.Execute(11), ManyRow);
		_books.Remove(200);
		AssertSame(_authors.Query().WithId(11).InnerJoinWithBook().Execute(), prepared.Execute(11), ManyRow);
	}
}
