namespace Prague.Generated.Tests.Query;

using Prague.Core;
using NUnit.Framework;

// ── Domain model ─────────────────────────────────────────────────────────────
// Two caches linked by a symmetric many-index. The JoinOne filter callback
// over the right cache receives a PAIRED-core builder — calling .Or(...) in
// the filter exercises paired Or's intersecter-mode orchestrator.

[DataCache]
public partial class PairedOrJoinBook {
	[DataCacheKey] public int Id { get; set; }

	/// <summary>FK to PairedOrJoinAuthor.Id. Symmetric many index enables JoinOne.</summary>
	[DataCacheIndex(DataCacheIndexType.Many, Symmetric = true)]
	public int AuthorId { get; set; }

	public string Title { get; set; } = "";
}

[DataCache]
public partial class PairedOrJoinAuthor {
	[DataCacheKey] public int Id { get; set; }
	public string Name { get; set; } = "";

	[DataCacheIndex(DataCacheIndexType.Many)]
	public string Country { get; set; } = "";

	[DataCacheIndex(DataCacheIndexType.Many)]
	public string Genre { get; set; } = "";
}

[TestFixture]
public class OrClausePairedViaJoinTests {
	private DataCacheRegistry _registry = null!;
	private PairedOrJoinBookCache _bookCache = null!;
	private PairedOrJoinAuthorCache _authorCache = null!;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<PairedOrJoinBookCache>()
			.Register<PairedOrJoinAuthorCache>()
			.Build();

		_bookCache = _registry.GetCache<PairedOrJoinBookCache>();
		_authorCache = _registry.GetCache<PairedOrJoinAuthorCache>();

		// Authors:
		// Id=100  Country=US  Genre=Mystery
		// Id=101  Country=US  Genre=Romance
		// Id=102  Country=GB  Genre=Mystery
		// Id=103  Country=FR  Genre=Romance
		// Id=104  Country=DE  Genre=Mystery
		_authorCache.AddOrUpdate(new PairedOrJoinAuthor { Id = 100, Name = "A100", Country = "US", Genre = "Mystery" });
		_authorCache.AddOrUpdate(new PairedOrJoinAuthor { Id = 101, Name = "A101", Country = "US", Genre = "Romance" });
		_authorCache.AddOrUpdate(new PairedOrJoinAuthor { Id = 102, Name = "A102", Country = "GB", Genre = "Mystery" });
		_authorCache.AddOrUpdate(new PairedOrJoinAuthor { Id = 103, Name = "A103", Country = "FR", Genre = "Romance" });
		_authorCache.AddOrUpdate(new PairedOrJoinAuthor { Id = 104, Name = "A104", Country = "DE", Genre = "Mystery" });

		// Books — each links to an author by AuthorId.
		_bookCache.AddOrUpdate(new PairedOrJoinBook { Id = 1, AuthorId = 100, Title = "Book by A100" });
		_bookCache.AddOrUpdate(new PairedOrJoinBook { Id = 2, AuthorId = 101, Title = "Book by A101" });
		_bookCache.AddOrUpdate(new PairedOrJoinBook { Id = 3, AuthorId = 102, Title = "Book by A102" });
		_bookCache.AddOrUpdate(new PairedOrJoinBook { Id = 4, AuthorId = 103, Title = "Book by A103" });
		_bookCache.AddOrUpdate(new PairedOrJoinBook { Id = 5, AuthorId = 104, Title = "Book by A104" });
	}

	/// <summary>
	/// JoinOne filter uses .Or(...) on the paired right-cache builder.
	/// Filter: Country=US OR Genre=Mystery.
	/// Authors passing the filter: A100 (US ∩ Mystery), A101 (US), A102 (Mystery), A104 (Mystery).
	/// → A103 (FR/Romance) is excluded.
	/// Book→Author: 1→A100 ✓, 2→A101 ✓, 3→A102 ✓, 4→A103 ✗ null, 5→A104 ✓.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_FilterCrossPropertyUnion_AttachesMatchingAuthors() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q.Or(
					b => b.WithCountry("US"),
					b => b.WithGenre("Mystery")))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(5));
		var byBookId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byBookId[1].Right, Is.Not.Null);
		Assert.That(byBookId[1].Right!.Id, Is.EqualTo(100));
		Assert.That(byBookId[2].Right, Is.Not.Null);
		Assert.That(byBookId[2].Right!.Id, Is.EqualTo(101));
		Assert.That(byBookId[3].Right, Is.Not.Null);
		Assert.That(byBookId[3].Right!.Id, Is.EqualTo(102));
		Assert.That(byBookId[4].Right, Is.Null, "A103 (FR/Romance) doesn't match filter");
		Assert.That(byBookId[5].Right, Is.Not.Null);
		Assert.That(byBookId[5].Right!.Id, Is.EqualTo(104));
	}

	/// <summary>
	/// JoinOne filter with chained WithXxx inside one branch — exercises paired AND-within-branch
	/// via mark-then-prune protocol.
	/// Branch1: Country=US AND Genre=Romance → only A101.
	/// Branch2: Country=GB AND Genre=Mystery → only A102.
	/// Union: {A101, A102}.
	/// Books: 1→null, 2→A101, 3→A102, 4→null, 5→null.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_ChainedWithXxxInBranch_ComposesAsAnd() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q.Or(
					b => b.WithCountry("US").WithGenre("Romance"),
					b => b.WithCountry("GB").WithGenre("Mystery")))
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);

		Assert.That(byBookId[1].Right, Is.Null, "A100 (US/Mystery) — Country=US but Genre≠Romance");
		Assert.That(byBookId[2].Right, Is.Not.Null);
		Assert.That(byBookId[2].Right!.Id, Is.EqualTo(101), "A101 (US/Romance) matches branch1");
		Assert.That(byBookId[3].Right, Is.Not.Null);
		Assert.That(byBookId[3].Right!.Id, Is.EqualTo(102), "A102 (GB/Mystery) matches branch2");
		Assert.That(byBookId[4].Right, Is.Null, "A103 (FR/Romance) — wrong country");
		Assert.That(byBookId[5].Right, Is.Null, "A104 (DE/Mystery) — wrong country");
	}

	/// <summary>
	/// Nested Or inside paired filter branch.
	/// Outer Or: b1 = Country=US, b2 = nested Or(Genre=Mystery, Genre=Romance).
	/// Effectively: Country=US OR Genre ∈ {Mystery, Romance} — which covers all authors.
	/// All books get their authors.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_NestedOr_ThreeWayUnion() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q.Or(
					b => b.WithCountry("US"),
					b => b.Or(
						c => c.WithGenre("Mystery"),
						c => c.WithGenre("Romance"))))
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		// Every author matches one of US, Mystery, or Romance → all 5 books resolve.
		for (var id = 1; id <= 5; id++) {
			Assert.That(byBookId[id].Right, Is.Not.Null, $"book {id} should have author attached");
		}
	}

	/// <summary>
	/// One branch is a no-op (lambda doesn't call any WithXxx). The no-op branch contributes
	/// nothing to the union (empty bitmap). Filter narrows to only branch1's matches.
	/// Branch1: Country=US → A100, A101.
	/// Branch2: no-op → empty.
	/// Result: only books linking to A100/A101 get authors attached.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_OneBranchNoOp_FiltersByOther() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q.Or(
					b => b.WithCountry("US"),
					b => b))
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byBookId[1].Right, Is.Not.Null);
		Assert.That(byBookId[1].Right!.Id, Is.EqualTo(100));
		Assert.That(byBookId[2].Right, Is.Not.Null);
		Assert.That(byBookId[2].Right!.Id, Is.EqualTo(101));
		Assert.That(byBookId[3].Right, Is.Null, "A102 (GB) excluded");
		Assert.That(byBookId[4].Right, Is.Null, "A103 (FR) excluded");
		Assert.That(byBookId[5].Right, Is.Null, "A104 (DE) excluded");
	}

	/// <summary>
	/// Baseline: filter lambda is the identity (q => q). No Or, no WithXxx — nothing
	/// narrows the paired set. Should behave as if the filter overload wasn't used.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_IdentityFilter_DoesNothing() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q)
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		for (var id = 1; id <= 5; id++) {
			Assert.That(byBookId[id].Right, Is.Not.Null, $"book {id} should have author attached");
		}
	}

	/// <summary>
	/// Both branches no-op. Neither branch contributed marks; both branch._first stays true;
	/// orchestrator's Dispose(flush) check sees no narrowing happened → no sweep → paired
	/// _candidates is unchanged → effectively "no filter applied", all authors pass.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_BothBranchesNoOp_AllAuthorsPass() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q.Or(b => b, b => b))
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		for (var id = 1; id <= 5; id++) {
			Assert.That(byBookId[id].Right, Is.Not.Null, $"book {id} should have author attached");
		}
	}

	/// <summary>
	/// Both branches narrow by the SAME property — union semantics.
	/// Country=US OR Country=GB → A100, A101 (US) + A102 (GB) = {100, 101, 102}.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_SamePropertyBothBranches_UnionsValues() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q.Or(
					b => b.WithCountry("US"),
					b => b.WithCountry("GB")))
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byBookId[1].Right!.Id, Is.EqualTo(100));
		Assert.That(byBookId[2].Right!.Id, Is.EqualTo(101));
		Assert.That(byBookId[3].Right!.Id, Is.EqualTo(102));
		Assert.That(byBookId[4].Right, Is.Null, "A103 (FR) excluded");
		Assert.That(byBookId[5].Right, Is.Null, "A104 (DE) excluded");
	}

	/// <summary>
	/// Both branches narrow to nonexistent values — filter matches no authors → all books get null.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_FilterNarrowsToEmpty_AllAuthorsNull() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q.Or(
					b => b.WithCountry("ZZ"),     // no author has Country=ZZ
					b => b.WithGenre("Unknown"))) // no author has Genre=Unknown
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		for (var id = 1; id <= 5; id++) {
			Assert.That(byBookId[id].Right, Is.Null, $"book {id} should have no author");
		}
	}

	/// <summary>
	/// Multi-value WithXxx inside an Or branch (uses the ReadOnlySpan overload of the codegen
	/// WithCountry extension). Within a branch this is a UNION over the multi-value lookup.
	/// Branch1: WithCountry({US, GB}) → A100, A101 (US), A102 (GB).
	/// Branch2: WithGenre("Mystery") → A100, A102, A104.
	/// Union: {A100, A101, A102, A104}.
	/// Books 1, 2, 3, 5 get authors; 4 null.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_MultiValueWithXxxInBranch_UnionsLookups() {
		var countries = new[] { "US", "GB" };
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q.Or(
					b => b.WithCountry(countries.AsSpan()),
					b => b.WithGenre("Mystery")))
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byBookId[1].Right!.Id, Is.EqualTo(100));
		Assert.That(byBookId[2].Right!.Id, Is.EqualTo(101));
		Assert.That(byBookId[3].Right!.Id, Is.EqualTo(102));
		Assert.That(byBookId[4].Right, Is.Null, "A103 (FR/Romance) excluded");
		Assert.That(byBookId[5].Right!.Id, Is.EqualTo(104));
	}

	/// <summary>
	/// Empty multi-value span (zero-length) in a branch — semantically "match nothing" within that branch.
	/// Branch1: WithCountry([]) → empty, contributes nothing.
	/// Branch2: WithGenre("Mystery") → {A100, A102, A104}.
	/// Final: {A100, A102, A104}.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_EmptySpanInBranch_TreatedAsNoMatches() {
		var empty = System.Array.Empty<string>();
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q.Or(
					b => b.WithCountry(empty.AsSpan()),
					b => b.WithGenre("Mystery")))
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byBookId[1].Right!.Id, Is.EqualTo(100), "A100 via Mystery");
		Assert.That(byBookId[2].Right, Is.Null, "A101 (US/Romance) — branch1 empty, branch2 misses");
		Assert.That(byBookId[3].Right!.Id, Is.EqualTo(102), "A102 via Mystery");
		Assert.That(byBookId[4].Right, Is.Null);
		Assert.That(byBookId[5].Right!.Id, Is.EqualTo(104), "A104 via Mystery");
	}

	/// <summary>
	/// Chained Or().Or() at filter level — top-level intersect after the first Or.
	/// First Or: Country ∈ {US, GB} → {A100, A101, A102}.
	/// Second Or: Genre ∈ {Mystery, Romance} → {A100, A101, A102, A103, A104}.
	/// Intersect: {A100, A101, A102}.
	/// Books 1, 2, 3 get authors; 4, 5 null.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_ChainedOrAtFilterLevel_IntersectsAtOuter() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q
					.Or(
						b => b.WithCountry("US"),
						b => b.WithCountry("GB"))
					.Or(
						b => b.WithGenre("Mystery"),
						b => b.WithGenre("Romance")))
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byBookId[1].Right!.Id, Is.EqualTo(100));
		Assert.That(byBookId[2].Right!.Id, Is.EqualTo(101));
		Assert.That(byBookId[3].Right!.Id, Is.EqualTo(102));
		Assert.That(byBookId[4].Right, Is.Null, "A103 — first Or excludes FR");
		Assert.That(byBookId[5].Right, Is.Null, "A104 — first Or excludes DE");
	}

	/// <summary>
	/// WithXxx after .Or() at filter level — additional intersect.
	/// Or filters to Country ∈ {US, GB}: {A100, A101, A102}.
	/// Then .WithGenre("Mystery") intersects: {A100, A102}.
	/// Books 1, 3 get authors; 2, 4, 5 null.
	/// </summary>
	[Test]
	public void PairedOrViaJoin_OrThenWithXxx_AdditionalNarrowing() {
		var results = _bookCache.Query()
			.JoinOne(
				_bookCache.AuthorIdIndex,
				_authorCache,
				q => q
					.Or(
						b => b.WithCountry("US"),
						b => b.WithCountry("GB"))
					.WithGenre("Mystery"))
			.Execute();

		var byBookId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byBookId[1].Right!.Id, Is.EqualTo(100), "A100 (US/Mystery)");
		Assert.That(byBookId[2].Right, Is.Null, "A101 (US/Romance) — not Mystery");
		Assert.That(byBookId[3].Right!.Id, Is.EqualTo(102), "A102 (GB/Mystery)");
		Assert.That(byBookId[4].Right, Is.Null);
		Assert.That(byBookId[5].Right, Is.Null, "A104 (DE/Mystery) — not US/GB");
	}
}
