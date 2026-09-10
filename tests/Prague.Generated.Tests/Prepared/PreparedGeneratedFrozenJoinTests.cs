namespace Prague.Generated.Tests.Prepared;

using Prague.Core;
using Prague.Generated.Tests.Join;
using NUnit.Framework;
using static PreparedParity;

// Generated FK JoinWith{T} / InnerJoinWith{T} shapes under BuildFrozen() (stage 3, step 5): after a
// generated narrowing step the reverse one-to-one join (a right-unique resolver), the forward
// many-to-one join (a left-symmetric resolver) and the inner reverse one-to-one fuse into the pipeline
// pass — one right lookup per row — and a SortBounded before them keeps the bounded page flow. The
// reverse one-to-many join (JoinMany) replays, and so does the INNER forward many-to-one: a left-
// symmetric fan-out regroups its rows, so it fuses only under FrozenOptions.FuseSymmetricInnerJoins.
// Every shape is eager == frozen row for row on whichever path it takes.
[TestFixture]
public class PreparedGeneratedFrozenJoinTests {
	private DataCacheRegistry _registry = null!;
	private AuthorCache _authors = null!;
	private BookCache _books = null!;
	private AuthorProfileCache _profiles = null!;
	private M2OAuthorCache _m2oAuthors = null!;
	private M2OBookCache _m2oBooks = null!;

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

		// 12 authors; authors 0..5 have profiles; 40 books over authors 0..10 plus 2 orphans.
		for (var a = 0; a < 12; a++)
			_authors.AddOrUpdate(new Author { Id = a, Name = $"A{a}", Country = a % 2 == 0 ? "UK" : "US" });
		for (var b = 0; b < 40; b++)
			_books.AddOrUpdate(new Book { Id = b, Title = $"B{b}", AuthorId = b % 11, Year = 1990 + b % 7 });
		_books.AddOrUpdate(new Book { Id = 100, Title = "orphan", AuthorId = 500, Year = 2000 });
		for (var p = 0; p < 6; p++)
			_profiles.AddOrUpdate(new AuthorProfile { Id = p, AuthorId = p, Bio = $"bio{p}", Website = "" });

		// Authors 1..3 exist; a quarter of the books point at author 0, who does not.
		for (var a = 1; a <= 3; a++)
			_m2oAuthors.AddOrUpdate(new M2OAuthor { Id = a, Name = $"M{a}" });
		for (var b = 0; b < 12; b++)
			_m2oBooks.AddOrUpdate(new M2OBook { Id = b, Title = $"MB{b}", AuthorId = b % 4, PublisherId = 100 });
	}

	private static string OneRow(JoinResult<Author, AuthorProfile?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id.ToString())}";
	private static string ForwardRow(JoinResult<M2OBook, M2OAuthor?> r) => $"{r.Left.Id}|{(r.Right is null ? "-" : r.Right.Id.ToString())}";

	private static string ManyRow(JoinResult<Author, QueryResults<Book>> r) {
		var ids = new int[r.Right.Count];
		for (var i = 0; i < ids.Length; i++) ids[i] = r.Right[i].Id;
		Array.Sort(ids);
		return r.Left.Id + "|" + string.Join(',', ids);
	}

	[Test]
	public void ReverseOneToOne_AfterWithId_Outer_Inner_FrozenFuses_LikeEager() {
		var outer = _authors.Prepare<int>().WithId(static id => id).JoinWithAuthorProfile().BuildFrozen();
		var inner = _authors.Prepare<int>().WithId(static id => id).InnerJoinWithAuthorProfile().BuildFrozen();
		Assert.That(outer.Explain(), Does.Contain("executor: Pipeline").And.Contain("joins: 1 (fused: 1, unfused: 0"));
		Assert.That(inner.Explain(), Does.Contain("executor: Pipeline").And.Contain("joins: 1 (fused: 1, unfused: 0"));
		foreach (var id in new[] { 0, 5, 7, 11, 99 }) {
			AssertSame(_authors.Query().WithId(id).JoinWithAuthorProfile().Execute(), outer.Execute(id), OneRow);
			AssertSame(_authors.Query().WithId(id).JoinWithAuthorProfile().ExecutePooledCloned(), outer.ExecutePooledCloned(id), OneRow);
			AssertSame(_authors.Query().WithId(id).InnerJoinWithAuthorProfile().Execute(), inner.Execute(id), OneRow);
			AssertSame(_authors.Query().WithId(id).InnerJoinWithAuthorProfile().ExecutePooled(), inner.ExecutePooled(id), OneRow);
			Assert.That(outer.Count(id), Is.EqualTo(_authors.Query().WithId(id).JoinWithAuthorProfile().Count()), "outer Count " + id);
			Assert.That(inner.Count(id), Is.EqualTo(_authors.Query().WithId(id).InnerJoinWithAuthorProfile().Count()), "inner Count " + id);
		}
	}

	[Test]
	public void ForwardManyToOne_AfterWithAuthorId_Outer_Inner_FrozenFuses_LikeEager() {
		var outer = _m2oBooks.Prepare<int>().WithAuthorId(static a => a).JoinWithM2OAuthor().BuildFrozen();
		// Inner: a left-symmetric fan-out regroups its rows, so it replays (byte-identical) unless opted in.
		var inner = _m2oBooks.Prepare<int>().WithAuthorId(static a => a).InnerJoinWithM2OAuthor().BuildFrozen();
		var innerFused = _m2oBooks.Prepare<int>().WithAuthorId(static a => a).InnerJoinWithM2OAuthor().BuildFrozen(new FrozenOptions { FuseSymmetricInnerJoins = true });
		Assert.That(outer.Explain(), Does.Contain("executor: Pipeline").And.Contain("joins: 1 (fused: 1, unfused: 0"));
		Assert.That(inner.Explain(), Does.Contain("executor: Replay"));
		Assert.That(innerFused.Explain(), Does.Contain("executor: Pipeline"));
		foreach (var author in new[] { 0, 1, 3, 9 }) {
			AssertSame(_m2oBooks.Query().WithAuthorId(author).JoinWithM2OAuthor().Execute(), outer.Execute(author), ForwardRow);
			AssertSame(_m2oBooks.Query().WithAuthorId(author).JoinWithM2OAuthor().ExecutePooled(1, 2), outer.ExecutePooled(author, 1, 2), ForwardRow);
			AssertSame(_m2oBooks.Query().WithAuthorId(author).InnerJoinWithM2OAuthor().Execute(), inner.Execute(author), ForwardRow);
			Assert.That(outer.Count(author), Is.EqualTo(_m2oBooks.Query().WithAuthorId(author).JoinWithM2OAuthor().Count()), "outer Count " + author);
			Assert.That(inner.Count(author), Is.EqualTo(_m2oBooks.Query().WithAuthorId(author).InnerJoinWithM2OAuthor().Count()), "inner Count " + author);
			Assert.That(innerFused.Count(author), Is.EqualTo(inner.Count(author)), "fused inner Count " + author);
			using var fusedRows = innerFused.Execute(author);
			using var replayRows = inner.Execute(author);
			Assert.That(fusedRows.Count, Is.EqualTo(replayRows.Count), "fused inner rows " + author);
		}

		// Author 0 does not exist: the outer join keeps its three books with a null right, the inner drops them.
		using var outerRows = outer.Execute(0);
		Assert.That(outerRows.Count, Is.EqualTo(3));
		for (var i = 0; i < outerRows.Count; i++) Assert.That(outerRows[i].Right, Is.Null);
		using var innerRows = inner.Execute(0);
		Assert.That(innerRows.Count, Is.Zero);
		Assert.That(inner.Count(0), Is.Zero);
		using var innerFusedRows = innerFused.Execute(0);
		Assert.That(innerFusedRows.Count, Is.Zero);
		Assert.That(innerFused.Count(0), Is.Zero);
	}

	private readonly struct AuthorByIdDesc : IComparer<Author> {
		public int Compare(Author? x, Author? y) => (y?.Id ?? 0).CompareTo(x?.Id ?? 0);
	}

	// The forward and the inner JoinWith bind on ICacheCarrier, which a SortedQuery discriminator does not
	// carry (eager too); the reverse outer one-to-one join after a SortBounded is the generated bounded shape.
	[Test]
	public void SortBounded_ThenJoinWith_Outer_FrozenIsBoundedPipeline_LikeEager() {
		var cmp = new AuthorByIdDesc();
		var outer = _authors.Prepare<int>().WithId(static id => id).SortBounded(cmp).JoinWithAuthorProfile().BuildFrozen();
		Assert.That(outer.Explain(), Does.Contain("executor: Pipeline").And.Contain("sort: bounded").And.Contain("fused: 1"));
		foreach (var id in new[] { 0, 5, 7, 99 })
			foreach (var (skip, take) in new[] { (0, 2), (1, 2), (0, int.MaxValue) }) {
				AssertSame(_authors.Query().WithId(id).SortBounded(cmp).JoinWithAuthorProfile().Execute(skip, take), outer.Execute(id, skip, take), OneRow);
				AssertSame(_authors.Query().WithId(id).SortBounded(cmp).JoinWithAuthorProfile().ExecutePooledCloned(skip, take), outer.ExecutePooledCloned(id, skip, take), OneRow);
			}
	}

	[Test]
	public void ReverseOneToMany_JoinMany_Replays_LikeEager() {
		var frozen = _authors.Prepare<int>().WithId(static id => id).JoinWithBook().BuildFrozen();
		Assert.That(frozen.Explain(), Does.Contain("executor: Replay"));
		foreach (var id in new[] { 0, 7, 11 })
			AssertSame(_authors.Query().WithId(id).JoinWithBook().Execute(), frozen.Execute(id), ManyRow);
	}
}
