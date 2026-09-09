namespace Prague.Core.Tests.Join;

using System.Collections.Generic;
using System.Linq;
using Prague.Core;
using Prague.Core.Collections;
using Prague.Core.Tests.Infrastructure;
using NUnit.Framework;

// ── JoinManyFanOut engine ───────────────────────────────────────────────────
// Direct tests of the pair set + right → extra-lefts chains every JoinMany resolver builds on: one
// pair per distinct right, a chain of the further lefts per pair slot rented only once some right is
// shared, a repeat sighting of a right for the same left ignored, one hash per recorded pair, and
// Dispose returning everything the resolver did not hand off.

[TestFixture]
public class JoinManyFanOutTests {
	// Struct target for Delivery (its container type parameter must be a struct); the log is shared by
	// every copy, so the by-value hand-off does not lose adds.
	private readonly struct Recorder : IJoinedResultContainer<int, string> {
		private readonly List<(int Left, string Value)> _log;

		public Recorder(List<(int Left, string Value)> log) => _log = log;

		int IJoinedResultContainer<int, string>.Add(int foreignKey, string result) {
			_log.Add((foreignKey, result));
			return 0;
		}

		int IJoinedResultContainer<int, string>.TotalCount => _log.Count;
	}

	private static JoinManyFanOut<int, int>.Delivery<string, Recorder> DeliveryOver(
		ref JoinManyFanOut<int, int> fanOut, List<(int Left, string Value)> log) =>
		new(new Recorder(log), fanOut.Heads, fanOut.NodeLefts, fanOut.NodeNexts);

	// The store reports a surviving pair by its slot in the pair set; a test stands in for the store by
	// looking the slot up (pair identity is the right key alone, so the probe's left is irrelevant).
	private static int SlotOf(ref JoinManyFanOut<int, int> fanOut, int right) =>
		fanOut.Pairs.IndexOf(new JoinedKeyPair<int, int>(0, right));

	[Test]
	public void Record_DistinctRights_OnePairEach() {
		var fanOut = new JoinManyFanOut<int, int>(8);
		try {
			for (var right = 100; right < 108; right++) {
				Assert.That(fanOut.Record(1, right), Is.True);
			}

			Assert.That(fanOut.DistinctRights, Is.EqualTo(8));
			Assert.That(fanOut.PairCount, Is.EqualTo(8));
			Assert.That(fanOut.SingleLeftPerRight, Is.True);
			Assert.That(fanOut.HasChains, Is.False, "the FK shape rents no chain arrays");
		} finally {
			fanOut.Dispose();
		}
	}

	[Test]
	public void Record_SharedRight_DeliversToEveryLeftOnce() {
		var fanOut = new JoinManyFanOut<int, int>(4);
		try {
			for (var left = 1; left <= 40; left++) {
				Assert.That(fanOut.Record(left, 100), Is.True);
			}

			Assert.That(fanOut.DistinctRights, Is.EqualTo(1), "one pair per distinct right");
			Assert.That(fanOut.PairCount, Is.EqualTo(40));
			Assert.That(fanOut.SingleLeftPerRight, Is.False);
			Assert.That(fanOut.HasChains, Is.True);

			var log = new List<(int Left, string Value)>();
			var delivery = DeliveryOver(ref fanOut, log);
			delivery.Add(1, SlotOf(ref fanOut, 100), "book");

			Assert.That(log.Select(e => e.Left), Is.EquivalentTo(Enumerable.Range(1, 40)));
			Assert.That(log.All(e => e.Value == "book"), Is.True);
		} finally {
			fanOut.Dispose();
		}
	}

	// The bucket enumerator yielded 100 twice for left 1 (removed and re-added under the walk): the
	// second sighting is not a pair, so the slot capacity it would have inflated stays exact.
	[Test]
	public void Record_RepeatSightingForTheSameLeft_IsIgnored() {
		var fanOut = new JoinManyFanOut<int, int>(4);
		try {
			Assert.That(fanOut.Record(1, 100), Is.True);
			Assert.That(fanOut.Record(1, 101), Is.True);
			Assert.That(fanOut.Record(1, 100), Is.False, "a repeat for the same left");
			Assert.That(fanOut.SingleLeftPerRight, Is.True, "a repeat is not a second left");
			Assert.That(fanOut.Record(2, 100), Is.True, "another left may record the same right");
			Assert.That(fanOut.PairCount, Is.EqualTo(3));
			Assert.That(fanOut.DistinctRights, Is.EqualTo(2));
			Assert.That(fanOut.SingleLeftPerRight, Is.False);

			var log = new List<(int Left, string Value)>();
			var delivery = DeliveryOver(ref fanOut, log);
			delivery.Add(1, SlotOf(ref fanOut, 100), "v");
			Assert.That(log.Select(e => e.Left), Is.EquivalentTo(new[] { 1, 2 }));

			log.Clear();
			delivery.Add(1, SlotOf(ref fanOut, 101), "w");
			Assert.That(log.Select(e => e.Left), Is.EquivalentTo(new[] { 1 }));
		} finally {
			fanOut.Dispose();
		}
	}

	// The chains arrive late: many rights already sit in the set when the first shared one shows up,
	// and more rights are recorded afterwards. Every slot — earlier and later — must resolve to its own
	// chain, and the chains must grow past their initial capacity.
	[Test]
	public void Chains_RentedByTheFirstSharedRight_CoverEarlierAndLaterSlots() {
		var fanOut = new JoinManyFanOut<int, int>(4);
		try {
			for (var right = 100; right < 160; right++) {
				fanOut.Record(1, right);
			}

			Assert.That(fanOut.HasChains, Is.False);
			Assert.That(fanOut.Record(2, 130), Is.True, "the first shared right rents the chains");
			Assert.That(fanOut.HasChains, Is.True);
			Assert.That(fanOut.Record(2, 100), Is.True);
			for (var right = 200; right < 300; right++) {
				fanOut.Record(3, right);
			}

			for (var left = 4; left <= 40; left++) {
				fanOut.Record(left, 250);
			}

			Assert.That(fanOut.DistinctRights, Is.EqualTo(160));
			Assert.That(fanOut.PairCount, Is.EqualTo(160 + 2 + 37));

			var log = new List<(int Left, string Value)>();
			var delivery = DeliveryOver(ref fanOut, log);
			delivery.Add(1, SlotOf(ref fanOut, 130), "a");
			Assert.That(log.Select(e => e.Left), Is.EquivalentTo(new[] { 1, 2 }), "shared before the chains existed");
			log.Clear();
			delivery.Add(1, SlotOf(ref fanOut, 115), "b");
			Assert.That(log.Select(e => e.Left), Is.EqualTo(new[] { 1 }), "an earlier slot with no chain");
			log.Clear();
			delivery.Add(3, SlotOf(ref fanOut, 250), "c");
			Assert.That(log.Select(e => e.Left), Is.EquivalentTo(Enumerable.Range(3, 38)), "a later slot with a long chain");
			log.Clear();
			delivery.Add(3, SlotOf(ref fanOut, 260), "d");
			Assert.That(log.Select(e => e.Left), Is.EqualTo(new[] { 3 }), "a later slot with no chain");
		} finally {
			fanOut.Dispose();
		}
	}

	// The user filter removes pairs from the set the paired core holds; survivors keep their slots, so
	// the chains recorded by slot still name the right rights.
	[Test]
	public void Delivery_SlotsSurviveRemovals() {
		var fanOut = new JoinManyFanOut<int, int>(4);
		try {
			fanOut.Record(1, 100);
			fanOut.Record(2, 200);
			fanOut.Record(3, 200);

			var survivors = fanOut.Pairs;
			Assert.That(survivors.Remove(new JoinedKeyPair<int, int>(0, 100)), Is.True);

			var log = new List<(int Left, string Value)>();
			var delivery = new JoinManyFanOut<int, int>.Delivery<string, Recorder>(
				new Recorder(log), fanOut.Heads, fanOut.NodeLefts, fanOut.NodeNexts);
			delivery.Add(2, survivors.IndexOf(new JoinedKeyPair<int, int>(0, 200)), "v");

			Assert.That(log.Select(e => e.Left), Is.EquivalentTo(new[] { 2, 3 }));
		} finally {
			fanOut.Dispose();
		}
	}

	// A delivered right reaches every left in its chain through the slot the store reports — the
	// right key is never hashed again.
	[Test]
	public void Delivery_SharedRight_NeverHashesTheRight() {
		var fanOut = new JoinManyFanOut<int, ProbeKey>(4);
		var hashes = 0;
		try {
			fanOut.Record(1, new ProbeKey(100));
			fanOut.Record(2, new ProbeKey(100));
			fanOut.Record(3, new ProbeKey(101));
			Assert.That(fanOut.SingleLeftPerRight, Is.False);
			var sharedSlot = fanOut.Pairs.IndexOf(new JoinedKeyPair<int, ProbeKey>(0, new ProbeKey(100)));
			var ownSlot = fanOut.Pairs.IndexOf(new JoinedKeyPair<int, ProbeKey>(0, new ProbeKey(101)));

			var log = new List<(int Left, string Value)>();
			var delivery = new JoinManyFanOut<int, ProbeKey>.Delivery<string, Recorder>(
				new Recorder(log), fanOut.Heads, fanOut.NodeLefts, fanOut.NodeNexts);
			ProbeKey.Arm(_ => hashes++);
			delivery.Add(1, sharedSlot, "shared");
			delivery.Add(3, ownSlot, "own");

			Assert.That(hashes, Is.Zero, "the slot comes from the store walk; delivery does not hash");
			Assert.That(log, Is.EquivalentTo(new[] { (1, "shared"), (2, "shared"), (3, "own") }));
		} finally {
			ProbeKey.Disarm();
			fanOut.Dispose();
		}
	}

	// m lefts sharing one right: m hashes and at most one equality probe per pair — the existing pair
	// is found in one step. A per-round first-fit cost m(m+1)/2 hashes here.
	[TestCase(64)]
	[TestCase(512)]
	public void Record_SharedRight_HashesOncePerPair_AndProbesOncePerPair(int pairs) {
		var fanOut = new JoinManyFanOut<int, ProbeKey>(4);
		var hashes = 0;
		ProbeKey.Arm(_ => hashes++);
		ProbeKey.ResetEqualsCalls();
		try {
			for (var left = 1; left <= pairs; left++) {
				Assert.That(fanOut.Record(left, new ProbeKey(7)), Is.True);
			}

			Assert.That(fanOut.PairCount, Is.EqualTo(pairs));
			Assert.That(fanOut.DistinctRights, Is.EqualTo(1));
			Assert.That(hashes, Is.EqualTo(pairs), "one hash per recorded pair");
			Assert.That(ProbeKey.EqualsCalls, Is.LessThanOrEqualTo(pairs), "at most one probe per pair");
		} finally {
			ProbeKey.Disarm();
			fanOut.Dispose();
		}
	}

	// Heads, nodes and the pair set all outgrow their initial capacity; everything comes back.
	[Test]
	public void Growth_AndDispose_PoolBalanced() =>
		LeakAssert.Balanced(() => {
			var fanOut = new JoinManyFanOut<int, int>(4);
			try {
				for (var left = 1; left <= 70; left++) {
					for (var right = 100; right < 170; right++) {
						fanOut.Record(left, right);
					}
				}

				Assert.That(fanOut.DistinctRights, Is.EqualTo(70));
				Assert.That(fanOut.PairCount, Is.EqualTo(4900));
			} finally {
				fanOut.Dispose();
			}
		});

	// The paired core receives a copy of the set and disposes it; the fan-out must not dispose it too.
	[Test]
	public void MarkPairsHandedOff_LeavesThePairsToTheCore() =>
		LeakAssert.Balanced(() => {
			var fanOut = new JoinManyFanOut<int, int>(4);
			for (var right = 100; right < 160; right++) {
				fanOut.Record(1, right);
			}

			var pairs = fanOut.Pairs;
			fanOut.MarkPairsHandedOff();
			Assert.That(fanOut.DistinctRights, Is.EqualTo(60), "the counts outlive the hand-off");
			Assert.That(fanOut.SingleLeftPerRight, Is.True);
			fanOut.Dispose();
			pairs.Dispose();
		});

	// The right key's hash throws inside Record after the set rented its arrays and the chains grew:
	// Dispose must still return every array exactly once.
	[Test]
	public void Record_RightKeyHashThrows_DisposeReturnsEverything() =>
		LeakAssert.Balanced(() => {
			var fanOut = new JoinManyFanOut<int, ProbeKey>(4);
			try {
				for (var right = 0; right < 60; right++) {
					fanOut.Record(1, new ProbeKey(right));
				}

				for (var left = 2; left <= 20; left++) {
					fanOut.Record(left, new ProbeKey(0));
				}

				ProbeKey.ThrowOnHashCall(1);
				try {
					fanOut.Record(21, new ProbeKey(0));
					Assert.Fail("the right key hash must throw");
				} catch (InvalidOperationException) {
				}

				Assert.That(fanOut.PairCount, Is.EqualTo(60 + 19), "a failed Record leaves the chains as they were");
			Assert.That(fanOut.HasChains, Is.True);
			} finally {
				ProbeKey.Disarm();
				fanOut.Dispose();
			}
		});
}

// ── Shared-right joins through the public API ───────────────────────────────
// m lefts sharing n rights: here m = 6 and n = 60 (past the inline ValueSet capacity) for the
// LeftSym and reverse-collection shapes, and 60 owners sharing 6 tags (chains of 60 lefts) for the
// forward-collection shape. Every left must receive every right, outer and inner, pooled and
// allocating.

[TestFixture]
public class JoinManyFanOutCoreTests {
	private const int SharingLefts = 6;
	private const int SharedRights = 60;

	private InMemoryDataCache<int, MlsAuthor> _authors = null!;
	private InMemoryDataCache<int, MlsBook> _books = null!;
	private CacheSymmetricKeyValueListIndex<int, MlsAuthor, string> _authorCountrySymIdx = null!;
	private CacheKeyValueListIndex<int, MlsBook, string> _bookCountryIdx = null!;

	private InMemoryDataCache<int, MnTag> _tags = null!;
	private InMemoryDataCache<int, MnTaggedBook> _taggedBooks = null!;
	private CacheCollectionSymmetricKeyValueListIndex<int, MnTaggedBook, int> _tagIndex = null!;

	[SetUp]
	public void SetUp() {
		_authors = new InMemoryDataCache<int, MlsAuthor>();
		_books = new InMemoryDataCache<int, MlsBook>();
		_authorCountrySymIdx = _authors.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Country);
		_bookCountryIdx = _books.CacheKeyValueListIndex<string>((_, v) => v.Country);

		// Authors 1..6 share the UK bucket of 60 books; author 7 (DE) has two; author 8 (FR) none.
		for (var a = 1; a <= SharingLefts; a++) {
			_authors.AddOrUpdate(a, new MlsAuthor { Id = a, Country = "UK", Name = "Author" + a });
		}

		_authors.AddOrUpdate(7, new MlsAuthor { Id = 7, Country = "DE", Name = "Author7" });
		_authors.AddOrUpdate(8, new MlsAuthor { Id = 8, Country = "FR", Name = "Author8" });
		for (var b = 0; b < SharedRights; b++) {
			_books.AddOrUpdate(101 + b, new MlsBook { Id = 101 + b, Country = "UK", Title = "Book" + b });
		}

		_books.AddOrUpdate(201, new MlsBook { Id = 201, Country = "DE", Title = "De1" });
		_books.AddOrUpdate(202, new MlsBook { Id = 202, Country = "DE", Title = "De2" });

		_tags = new InMemoryDataCache<int, MnTag>();
		_taggedBooks = new InMemoryDataCache<int, MnTaggedBook>();
		_tagIndex = _taggedBooks.CacheCollectionSymmetricKeyValueListIndex<int>((_, b) => b.TagIds);

		// Tags 1..6; 60 books carrying every tag; tag 7 unused.
		for (var t = 1; t <= SharingLefts + 1; t++) {
			_tags.AddOrUpdate(t, new MnTag { Id = t, Name = "tag" + t });
		}

		var allTags = Enumerable.Range(1, SharingLefts).ToList();
		for (var b = 0; b < SharedRights; b++) {
			_taggedBooks.AddOrUpdate(1001 + b, new MnTaggedBook { Id = 1001 + b, Title = "Book" + b, TagIds = new List<int>(allTags) });
		}
	}

	private static int[] UkBookIds => Enumerable.Range(101, SharedRights).ToArray();
	private static int[] AllTagIds => Enumerable.Range(1, SharingLefts).ToArray();
	private static int[] AllBookIds => Enumerable.Range(1001, SharedRights).ToArray();

	private static int[] Ids<TLeft, TRight>(JoinResult<TLeft, QueryResults<TRight>> row, Func<TRight, int> id) =>
		row.Right.Select(id).OrderBy(i => i).ToArray();

	// ── LeftSym ──────────────────────────────────────────────────────────────

	[Test]
	public void JoinMany_LeftSym_SixLeftsSharingSixtyRights_EachReceivesAll() {
		var results = _authors.Query().JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx).Execute();

		Assert.That(results.Count, Is.EqualTo(8));
		var byId = results.ToDictionary(r => r.Left.Id);
		for (var a = 1; a <= SharingLefts; a++) {
			Assert.That(Ids(byId[a], b => b.Id), Is.EqualTo(UkBookIds), $"author {a}");
		}

		Assert.That(Ids(byId[7], b => b.Id), Is.EqualTo(new[] { 201, 202 }));
		Assert.That(byId[8].Right.Count, Is.Zero);
	}

	[Test]
	public void InnerJoinMany_LeftSym_SixLeftsSharingSixtyRights_EachReceivesAll() {
		var results = _authors.Query().InnerJoinMany(_authorCountrySymIdx, _books, _bookCountryIdx).Execute();

		Assert.That(results.Select(r => r.Left.Id).OrderBy(i => i), Is.EqualTo(Enumerable.Range(1, 7)));
		var byId = results.ToDictionary(r => r.Left.Id);
		for (var a = 1; a <= SharingLefts; a++) {
			Assert.That(Ids(byId[a], b => b.Id), Is.EqualTo(UkBookIds), $"author {a}");
		}

		Assert.That(Ids(byId[7], b => b.Id), Is.EqualTo(new[] { 201, 202 }));
	}

	[Test]
	public void JoinMany_LeftSym_SharedRights_PooledEqualsExecute() {
		var expected = _authors.Query().JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx).Execute()
			.ToDictionary(r => r.Left.Id, r => Ids(r, b => b.Id));
		using var pooled = _authors.Query().JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx).ExecutePooled();

		var actual = pooled.ToDictionary(r => r.Left.Id, r => Ids(r, b => b.Id));
		Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
		foreach (var (id, rights) in expected) {
			Assert.That(actual[id], Is.EqualTo(rights), $"author {id}");
		}
	}

	[Test]
	public void InnerJoinMany_LeftSym_SharedRights_CountMatchesExecute() {
		var executed = _authors.Query().InnerJoinMany(_authorCountrySymIdx, _books, _bookCountryIdx).Execute().Count;
		var counted = _authors.Query().InnerJoinMany(_authorCountrySymIdx, _books, _bookCountryIdx).Count();
		Assert.That(counted, Is.EqualTo(executed));
	}

	// ── Collection, reverse (element → owners) ───────────────────────────────

	[Test]
	public void JoinManyCollection_OwnersSharedBySixElements_EachElementReceivesAll() {
		var results = _tags.Query().JoinManyCollection(_taggedBooks, _tagIndex).Execute();

		Assert.That(results.Count, Is.EqualTo(SharingLefts + 1));
		var byId = results.ToDictionary(r => r.Left.Id);
		for (var t = 1; t <= SharingLefts; t++) {
			Assert.That(Ids(byId[t], b => b.Id), Is.EqualTo(AllBookIds), $"tag {t}");
		}

		Assert.That(byId[SharingLefts + 1].Right.Count, Is.Zero);
	}

	[Test]
	public void InnerJoinManyCollection_OwnersSharedBySixElements_EachElementReceivesAll() {
		var results = _tags.Query().InnerJoinManyCollection(_taggedBooks, _tagIndex).Execute();

		Assert.That(results.Select(r => r.Left.Id).OrderBy(i => i), Is.EqualTo(AllTagIds));
		foreach (var row in results) {
			Assert.That(Ids(row, b => b.Id), Is.EqualTo(AllBookIds), $"tag {row.Left.Id}");
		}
	}

	[Test]
	public void JoinManyCollection_SharedRights_PooledEqualsExecute() {
		var expected = _tags.Query().JoinManyCollection(_taggedBooks, _tagIndex).Execute()
			.ToDictionary(r => r.Left.Id, r => Ids(r, b => b.Id));
		using var pooled = _tags.Query().JoinManyCollection(_taggedBooks, _tagIndex).ExecutePooled();

		var actual = pooled.ToDictionary(r => r.Left.Id, r => Ids(r, b => b.Id));
		Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
		foreach (var (id, rights) in expected) {
			Assert.That(actual[id], Is.EqualTo(rights), $"tag {id}");
		}
	}

	// ── Collection, forward (owner → referenced) — six tags shared by sixty owners ──

	[Test]
	public void JoinManyCollectionForward_SixtyOwnersSharingSixTags_EachReceivesAll() {
		var results = _taggedBooks.Query().JoinManyCollectionForward(_tags, _tagIndex).Execute();

		Assert.That(results.Count, Is.EqualTo(SharedRights));
		foreach (var row in results) {
			Assert.That(Ids(row, t => t.Id), Is.EqualTo(AllTagIds), $"book {row.Left.Id}");
		}
	}

	[Test]
	public void InnerJoinManyCollectionForward_SixtyOwnersSharingSixTags_EachReceivesAll() {
		var results = _taggedBooks.Query().InnerJoinManyCollectionForward(_tags, _tagIndex).Execute();

		Assert.That(results.Count, Is.EqualTo(SharedRights));
		foreach (var row in results) {
			Assert.That(Ids(row, t => t.Id), Is.EqualTo(AllTagIds), $"book {row.Left.Id}");
		}
	}

	[Test]
	public void JoinManyCollectionForward_SharedRights_PooledEqualsExecute() {
		var expected = _taggedBooks.Query().JoinManyCollectionForward(_tags, _tagIndex).Execute()
			.ToDictionary(r => r.Left.Id, r => Ids(r, t => t.Id));
		using var pooled = _taggedBooks.Query().JoinManyCollectionForward(_tags, _tagIndex).ExecutePooled();

		var actual = pooled.ToDictionary(r => r.Left.Id, r => Ids(r, t => t.Id));
		Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
		foreach (var (id, rights) in expected) {
			Assert.That(actual[id], Is.EqualTo(rights), $"book {id}");
		}
	}

	// ── The user filter runs once per query; its predicate sees every distinct right exactly once ──
	//
	// Three lefts sharing four rights: one pair set of four rights, chains of three lefts. The filter
	// lambda (the builder-shaping callback) is invoked once; the Where predicate it installs runs
	// inside the store walk once per distinct right, so an always-true predicate yields exactly the
	// unfiltered result and each right is delivered to every left in its chain.

	private void ThreeAuthorsFourBooks() {
		_authors = new InMemoryDataCache<int, MlsAuthor>();
		_books = new InMemoryDataCache<int, MlsBook>();
		_authorCountrySymIdx = _authors.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Country);
		_bookCountryIdx = _books.CacheKeyValueListIndex<string>((_, v) => v.Country);
		for (var a = 1; a <= 3; a++) {
			_authors.AddOrUpdate(a, new MlsAuthor { Id = a, Country = "UK", Name = "Author" + a });
		}

		for (var b = 101; b <= 104; b++) {
			_books.AddOrUpdate(b, new MlsBook { Id = b, Country = "UK", Title = "Book" + b });
		}
	}

	private void FourBooksWithThreeTags() {
		_tags = new InMemoryDataCache<int, MnTag>();
		_taggedBooks = new InMemoryDataCache<int, MnTaggedBook>();
		_tagIndex = _taggedBooks.CacheCollectionSymmetricKeyValueListIndex<int>((_, b) => b.TagIds);
		for (var t = 1; t <= 3; t++) {
			_tags.AddOrUpdate(t, new MnTag { Id = t, Name = "tag" + t });
		}

		for (var b = 1001; b <= 1004; b++) {
			_taggedBooks.AddOrUpdate(b, new MnTaggedBook { Id = b, Title = "Book" + b, TagIds = new List<int> { 1, 2, 3 } });
		}
	}

	[Test]
	public void JoinMany_LeftSym_FilterRunsOnce_PredicateSeesEachRightOnce() {
		ThreeAuthorsFourBooks();
		var filterCalls = 0;
		var predicateCalls = 0;

		var unfiltered = _authors.Query().JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx).Execute()
			.ToDictionary(r => r.Left.Id, r => Ids(r, b => b.Id));
		var filtered = _authors.Query()
			.JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx, q => {
				filterCalls++;
				return q.Where(_ => {
					predicateCalls++;
					return true;
				});
			})
			.Execute()
			.ToDictionary(r => r.Left.Id, r => Ids(r, b => b.Id));

		Assert.That(filterCalls, Is.EqualTo(1), "one filter application per query");
		Assert.That(predicateCalls, Is.EqualTo(4), "the predicate sees every distinct right exactly once");
		Assert.That(filtered, Is.EqualTo(unfiltered));
	}

	[Test]
	public void InnerJoinMany_LeftSym_FilterRunsOnce_PredicateSeesEachRightOnce() {
		ThreeAuthorsFourBooks();
		var filterCalls = 0;
		var predicateCalls = 0;

		var results = _authors.Query()
			.InnerJoinMany(_authorCountrySymIdx, _books, _bookCountryIdx, q => {
				filterCalls++;
				return q.Where(_ => {
					predicateCalls++;
					return true;
				});
			})
			.Execute();

		Assert.That(filterCalls, Is.EqualTo(1));
		Assert.That(predicateCalls, Is.EqualTo(4));
		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results.All(r => r.Right.Count == 4), Is.True);
	}

	[Test]
	public void JoinMany_LeftSym_FilterRejectingPairs_NarrowsEveryLeftAlike() {
		ThreeAuthorsFourBooks();

		var results = _authors.Query()
			.JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx, q => q.Where(b => b.Id % 2 == 0))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results) {
			Assert.That(Ids(row, b => b.Id), Is.EqualTo(new[] { 102, 104 }), $"author {row.Left.Id}");
		}
	}

	[Test]
	public void JoinManyCollection_FilterRunsOnce_PredicateSeesEachRightOnce() {
		FourBooksWithThreeTags();
		var filterCalls = 0;
		var predicateCalls = 0;

		var results = _tags.Query()
			.JoinManyCollection(_taggedBooks, _tagIndex, q => {
				filterCalls++;
				return q.Where(_ => {
					predicateCalls++;
					return true;
				});
			})
			.Execute();

		Assert.That(filterCalls, Is.EqualTo(1), "three tags share four books: one filter application");
		Assert.That(predicateCalls, Is.EqualTo(4), "four distinct books");
		Assert.That(results.Count, Is.EqualTo(3));
		Assert.That(results.All(r => r.Right.Count == 4), Is.True);
	}

	[Test]
	public void JoinManyCollectionForward_FilterRunsOnce_PredicateSeesEachRightOnce() {
		FourBooksWithThreeTags();
		var filterCalls = 0;
		var predicateCalls = 0;

		var results = _taggedBooks.Query()
			.JoinManyCollectionForward(_tags, _tagIndex, q => {
				filterCalls++;
				return q.Where(_ => {
					predicateCalls++;
					return true;
				});
			})
			.Execute();

		Assert.That(filterCalls, Is.EqualTo(1), "four books share three tags: one filter application");
		Assert.That(predicateCalls, Is.EqualTo(3), "three distinct tags");
		Assert.That(results.Count, Is.EqualTo(4));
		Assert.That(results.All(r => r.Right.Count == 3), Is.True);
	}
}
