namespace Prague.Core.Tests.Join;

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Prague.Core;
using Prague.Core.Collections;
using Prague.Core.TypeSystem;
using NUnit.Framework;

using MlsNoFilter = Prague.Core.NoFilter<Prague.Core.CacheQueryBuilderCombined<
	Prague.Core.TypeSystem.NonExecutableQuery<Prague.Core.InMemoryDataCache<int, MlsBook>>,
	Prague.Core.PairedCacheQueryBuilderCoreCombined<int, int, MlsBook>,
	int, MlsBook, Prague.Core.Resolvers<Prague.Core.BaseResolver<int, MlsBook>>, MlsBook>>;
using MnNoFilter = Prague.Core.NoFilter<Prague.Core.CacheQueryBuilderCombined<
	Prague.Core.TypeSystem.NonExecutableQuery<Prague.Core.InMemoryDataCache<int, MnTaggedBook>>,
	Prague.Core.PairedCacheQueryBuilderCoreCombined<int, int, MnTaggedBook>,
	int, MnTaggedBook, Prague.Core.Resolvers<Prague.Core.BaseResolver<int, MnTaggedBook>>, MnTaggedBook>>;
using PkNoFilter = Prague.Core.NoFilter<Prague.Core.CacheQueryBuilderCombined<
	Prague.Core.TypeSystem.NonExecutableQuery<Prague.Core.InMemoryDataCache<ProbeKey, PkBook>>,
	Prague.Core.PairedCacheQueryBuilderCoreCombined<int, ProbeKey, PkBook>,
	ProbeKey, PkBook, Prague.Core.Resolvers<Prague.Core.BaseResolver<ProbeKey, PkBook>>, PkBook>>;
using PkTbNoFilter = Prague.Core.NoFilter<Prague.Core.CacheQueryBuilderCombined<
	Prague.Core.TypeSystem.NonExecutableQuery<Prague.Core.InMemoryDataCache<ProbeKey, PkTaggedBook>>,
	Prague.Core.PairedCacheQueryBuilderCoreCombined<int, ProbeKey, PkTaggedBook>,
	ProbeKey, PkTaggedBook, Prague.Core.Resolvers<Prague.Core.BaseResolver<ProbeKey, PkTaggedBook>>, PkTaggedBook>>;

/// <summary>
///   Live-writer regression suite for <c>JoinManyLeftSymResolver</c> and
///   <c>JoinManyCollectionResolver</c> (mirrors <see cref="JoinManyRightListIndexConcurrentMutationTests"/>).
///   Both resolvers record every (left, right) pair from ONE enumeration of the left's right bucket,
///   size the slot from exactly those pairs (<c>Init</c> fires after the walk) and deliver through
///   the fan-out — so whatever the index writer does concurrently, a slot never receives more Adds than
///   it reserved, and every recorded right that is still in the store at execution time is
///   delivered. Each test pins one writer interleaving deterministically through a container hook
///   (Init / PrepareSharedBuffer / Add), through the public join filter, which the store walk
///   invokes right before every <c>Add</c>, or through a <see cref="ProbeKey"/> right key, whose
///   hash fires inside the bucket walk itself; the two stress tests run a real writer thread.
/// </summary>
[TestFixture]
public class JoinManyLeftSymCollectionConcurrentMutationTests {
	private static TimeSpan Duration => TimeSpan.FromSeconds(2);

	// ── LeftSym fixture ──────────────────────────────────────────────────────

	private InMemoryDataCache<int, MlsAuthor> _authors = null!;
	private InMemoryDataCache<int, MlsBook> _books = null!;
	private CacheSymmetricKeyValueListIndex<int, MlsAuthor, string> _authorCountrySymIdx = null!;
	private CacheKeyValueListIndex<int, MlsBook, string> _bookCountryIdx = null!;

	// ── Collection fixture ───────────────────────────────────────────────────

	private InMemoryDataCache<int, MnTag> _tags = null!;
	private InMemoryDataCache<int, MnTaggedBook> _taggedBooks = null!;
	private CacheCollectionSymmetricKeyValueListIndex<int, MnTaggedBook, int> _tagIndex = null!;

	[SetUp]
	public void SetUp() {
		_authors = new InMemoryDataCache<int, MlsAuthor>();
		_books = new InMemoryDataCache<int, MlsBook>();
		_authorCountrySymIdx = _authors.CacheSymmetricKeyValueListIndex<string>((_, v) => v.Country);
		_bookCountryIdx = _books.CacheKeyValueListIndex<string>((_, v) => v.Country);

		_tags = new InMemoryDataCache<int, MnTag>();
		_taggedBooks = new InMemoryDataCache<int, MnTaggedBook>();
		_tagIndex = _taggedBooks.CacheCollectionSymmetricKeyValueListIndex<int>((_, b) => b.TagIds);
	}

	private JoinManyLeftSymResolver<int, MlsAuthor, InMemoryDataCache<int, MlsBook>, string, string, int, MlsBook, MlsNoFilter, IdentitySelector<string>> LeftSymResolver() =>
		new(_authorCountrySymIdx, _books, _bookCountryIdx, default, default);

	private JoinManyCollectionResolver<int, MnTag, InMemoryDataCache<int, MnTaggedBook>, int, MnTaggedBook, MnTaggedBook, MnNoFilter> CollectionResolver() =>
		new(_tagIndex.Forward, _taggedBooks, default);

	private void Author(int id, string country) => _authors.AddOrUpdate(id, new MlsAuthor { Id = id, Country = country, Name = "Author" + id });
	private void Book(int id, string country) => _books.AddOrUpdate(id, new MlsBook { Id = id, Country = country, Title = "Book" + id });
	private void Tag(int id) => _tags.AddOrUpdate(id, new MnTag { Id = id, Name = "tag" + id });
	private void TaggedBook(int id, params int[] tagIds) => _taggedBooks.AddOrUpdate(id, new MnTaggedBook { Id = id, Title = "Book" + id, TagIds = new List<int>(tagIds) });

	private static List<int> Ids<TRight>(SlotLog<TRight> log, int left, Func<TRight, int> id) {
		var ids = new List<int>();
		foreach (var right in log.Delivered.GetValueOrDefault(left) ?? new List<TRight>()) {
			ids.Add(id(right));
		}

		return ids;
	}

	// ═════════════════════════════════════════════════════════════════════════
	// JoinManyLeftSymResolver — outer path (ExecuteReverseMany)
	// ═════════════════════════════════════════════════════════════════════════

	// A right lands in the group's bucket after this left's pairs were recorded and its slot sized,
	// before the paired execute: it is in no pair set, so it is neither delivered nor overflowing.
	[Test]
	public void LeftSym_RightAddedAfterSlotSized_SlotReceivesExactlyTheRecordedRights() {
		Author(1, "UK");
		for (var i = 0; i < 11; i++) {
			Book(100 + i, "UK");
		}

		var resolver = LeftSymResolver();
		var books = _books;
		var log = new SlotLog<MlsBook> {
			OnInit = _ => books.AddOrUpdate(999, new MlsBook { Id = 999, Country = "UK", Title = "Late" })
		};
		var container = new RecordingContainer<MlsBook>(log);

		((IJoinManyResolver<int, MlsAuthor, MlsBook>)resolver).ExecuteReverseMany(ref container, new[] { 1 });

		Assert.That(log.Capacity.GetValueOrDefault(1), Is.EqualTo(11));
		Assert.That(log.Adds.GetValueOrDefault(1), Is.EqualTo(log.Capacity.GetValueOrDefault(1)),
			"slot for author 1 must receive exactly the rights recorded for it");
		Assert.That(Ids(log, 1, b => b.Id), Does.Not.Contain(999));
	}

	// Two lefts share one lookup group; a right is added after the second left's slot is sized.
	// Each left's slot is sized from its own bucket walk (its own chain nodes), so the earlier
	// left cannot be short-changed by the later one's larger bucket.
	[Test]
	public void LeftSym_RightAddedBetweenTwoLeftsOfOneGroup_BothSlotsReceiveExactlyTheirRecordedRights() {
		Author(1, "UK");
		Author(2, "UK");
		for (var i = 0; i < 3; i++) {
			Book(100 + i, "UK");
		}

		var resolver = LeftSymResolver();
		var books = _books;
		var log = new SlotLog<MlsBook> {
			OnInit = key => {
				if (key == 2) {
					books.AddOrUpdate(999, new MlsBook { Id = 999, Country = "UK", Title = "Late" });
				}
			}
		};
		var container = new RecordingContainer<MlsBook>(log);

		((IJoinManyResolver<int, MlsAuthor, MlsBook>)resolver).ExecuteReverseMany(ref container, new[] { 1, 2 });

		Assert.That(log.Capacity.GetValueOrDefault(1), Is.EqualTo(3));
		Assert.That(log.Capacity.GetValueOrDefault(2), Is.EqualTo(3));
		Assert.That(log.Adds.GetValueOrDefault(1), Is.EqualTo(3), "author 1 (walked before the write)");
		Assert.That(log.Adds.GetValueOrDefault(2), Is.EqualTo(3), "author 2 (walked before the write)");
	}

	// An input left is skipped because its group had no rights at its turn (capacity stays 0); the
	// group gains a right before a later left of the same group is walked. Only the later left
	// recorded the right, so only the later left receives it.
	[Test]
	public void LeftSym_LeftSkippedForEmptyGroup_GroupGainsRightLater_ZeroCapacitySlotNeverReceivesAdds() {
		Author(3, "UK");
		Author(10, "DE");
		Author(1, "UK");
		Book(200, "DE");

		var resolver = LeftSymResolver();
		var books = _books;
		var log = new SlotLog<MlsBook> {
			OnInit = key => {
				if (key == 10) {
					books.AddOrUpdate(500, new MlsBook { Id = 500, Country = "UK", Title = "FirstUk" });
				}
			}
		};
		var container = new RecordingContainer<MlsBook>(log);

		((IJoinManyResolver<int, MlsAuthor, MlsBook>)resolver).ExecuteReverseMany(ref container, new[] { 3, 10, 1 });

		Assert.That(log.Capacity.ContainsKey(3), Is.False, "author 3 had no rights at its turn — no Init");
		Assert.That(log.Adds.GetValueOrDefault(3), Is.Zero, "slot for author 3 (never sized) received rights");
		Assert.That(log.Adds.GetValueOrDefault(1), Is.EqualTo(1), "author 1 recorded book 500 and must receive it");
	}

	// A left sized for group DE (1 right) is moved to group UK (2 rights) after UK's left is sized
	// and before the paired execute. Pairs are per left, not per group: the moved left keeps the
	// DE right it recorded and never sees UK's.
	[Test]
	public void LeftSym_LeftMovedToBiggerGroupAfterInit_SlotReceivesExactlyTheRecordedRights() {
		Author(3, "DE");
		Author(1, "UK");
		Book(200, "DE");
		Book(100, "UK");
		Book(101, "UK");

		var resolver = LeftSymResolver();
		var authors = _authors;
		var log = new SlotLog<MlsBook> {
			OnInit = key => {
				if (key == 1) {
					authors.AddOrUpdate(3, new MlsAuthor { Id = 3, Country = "UK", Name = "Author3" });
				}
			}
		};
		var container = new RecordingContainer<MlsBook>(log);

		((IJoinManyResolver<int, MlsAuthor, MlsBook>)resolver).ExecuteReverseMany(ref container, new[] { 3, 1 });

		Assert.That(log.Capacity.GetValueOrDefault(3), Is.EqualTo(1));
		Assert.That(Ids(log, 3, b => b.Id), Is.EqualTo(new[] { 200 }), "author 3 keeps the DE right it recorded");
		Assert.That(Ids(log, 1, b => b.Id), Is.EquivalentTo(new[] { 100, 101 }));
	}

	// A right recorded under group UK is moved to group DE after UK's left was walked and before
	// DE's left is walked. Every left reflects its own bucket snapshot: UK's left still receives the
	// right it recorded (a consistent stale read — no Add-time re-validation, same as the right-list
	// resolver), and DE's left receives ALL three rights it recorded, the shared one through its chain.
	[Test]
	public void LeftSym_RightMovedBetweenGroups_BothGroupsReceiveTheirOwnSnapshots() {
		Author(1, "UK");
		Author(5, "FR");
		Author(2, "DE");
		Book(300, "UK");
		Book(550, "FR");
		Book(400, "DE");
		Book(401, "DE");

		var resolver = LeftSymResolver();
		var books = _books;
		var log = new SlotLog<MlsBook> {
			OnInit = key => {
				if (key == 5) {
					books.AddOrUpdate(300, new MlsBook { Id = 300, Country = "DE", Title = "Book300" });
				}
			}
		};
		var container = new RecordingContainer<MlsBook>(log);

		((IJoinManyResolver<int, MlsAuthor, MlsBook>)resolver).ExecuteReverseMany(ref container, new[] { 1, 5, 2 });

		Assert.That(log.Capacity.GetValueOrDefault(2), Is.EqualTo(3), "author 2 recorded three DE rights");
		Assert.That(Ids(log, 2, b => b.Id), Is.EquivalentTo(new[] { 300, 400, 401 }),
			"author 2 must receive every right it recorded, including the one already paired with author 1");
		Assert.That(Ids(log, 1, b => b.Id), Is.EqualTo(new[] { 300 }),
			"author 1 receives the right it recorded while it was still in UK (stale but consistent)");
		Assert.That(log.Adds.GetValueOrDefault(1), Is.EqualTo(log.Capacity.GetValueOrDefault(1)));
	}

	// No writer. A many-to-one selector folds two lookup groups onto one right-index key, so both
	// lefts record the same two rights; the second left joins the chains of both and receives
	// every right it reserved.
	[Test]
	public void LeftSym_ManyToOneSelector_SecondGroupReceivesEveryReservedRight() {
		Author(1, "UK-A");
		Author(2, "UK-B");
		Book(100, "UK");
		Book(101, "UK");

		var resolver = new JoinManyLeftSymResolver<int, MlsAuthor, InMemoryDataCache<int, MlsBook>, string, string, int, MlsBook, MlsNoFilter, KeySelector<string, string>>(
			_authorCountrySymIdx, _books, _bookCountryIdx, default, new KeySelector<string, string>(static k => k.Substring(0, 2)));

		var log = new SlotLog<MlsBook>();
		var container = new RecordingContainer<MlsBook>(log);

		((IJoinManyResolver<int, MlsAuthor, MlsBook>)resolver).ExecuteReverseMany(ref container, new[] { 1, 2 });

		Assert.That(log.Capacity.GetValueOrDefault(2), Is.EqualTo(2));
		Assert.That(Ids(log, 1, b => b.Id), Is.EquivalentTo(new[] { 100, 101 }));
		Assert.That(Ids(log, 2, b => b.Id), Is.EquivalentTo(new[] { 100, 101 }),
			"no writer involved: every right reserved for author 2 must be delivered to it");
	}

	// No right is shared, so the resolver skips the fan-out delivery and runs the plain paired execute:
	// every right key is hashed exactly twice — once by the walk recording its pair, once by the store
	// lookup — and never a third time for a slot lookup. Every left still receives exactly its rights.
	[Test]
	public void LeftSym_NoSharedRights_DeliversWithoutSlotLookups() {
		var books = new InMemoryDataCache<ProbeKey, PkBook>();
		var bookCountryIdx = books.CacheKeyValueListIndex<string>((_, v) => v.Country);
		var countries = new[] { "UK", "DE", "FR" };
		for (var a = 0; a < countries.Length; a++) {
			Author(1 + a, countries[a]);
			for (var b = 0; b < 2; b++) {
				var id = 100 + 10 * a + b;
				books.AddOrUpdate(new ProbeKey(id), new PkBook { Id = id, Country = countries[a], Title = "Book" + id });
			}
		}

		var resolver = new JoinManyLeftSymResolver<int, MlsAuthor, InMemoryDataCache<ProbeKey, PkBook>, string, string, ProbeKey, PkBook, PkNoFilter, IdentitySelector<string>>(
			_authorCountrySymIdx, books, bookCountryIdx, default, default);
		var log = new SlotLog<PkBook>();
		var container = new RecordingContainer<PkBook>(log);

		var hashes = 0;
		ProbeKey.Arm(_ => hashes++);
		try {
			((IJoinManyResolver<int, MlsAuthor, PkBook>)resolver).ExecuteReverseMany(ref container, new[] { 1, 2, 3 });
		} finally {
			ProbeKey.Disarm();
		}

		Assert.That(hashes, Is.EqualTo(2 * 6), "one hash per pair for the walk, one per pair for the store lookup, none for slots");
		Assert.That(Ids(log, 1, b => b.Id), Is.EquivalentTo(new[] { 100, 101 }));
		Assert.That(Ids(log, 2, b => b.Id), Is.EquivalentTo(new[] { 110, 111 }));
		Assert.That(Ids(log, 3, b => b.Id), Is.EquivalentTo(new[] { 120, 121 }));
	}

	// One shared right turns the fan-out delivery on: the walk hashes every pair, the store looks each
	// distinct right up once and delivers it by pair slot — the chain walk never hashes.
	[Test]
	public void LeftSym_SharedRights_DeliverThroughTheFanOut_NoSlotLookups() {
		var books = new InMemoryDataCache<ProbeKey, PkBook>();
		var bookCountryIdx = books.CacheKeyValueListIndex<string>((_, v) => v.Country);
		Author(1, "UK");
		Author(2, "UK");
		for (var i = 100; i < 103; i++) {
			books.AddOrUpdate(new ProbeKey(i), new PkBook { Id = i, Country = "UK", Title = "Book" + i });
		}

		var resolver = new JoinManyLeftSymResolver<int, MlsAuthor, InMemoryDataCache<ProbeKey, PkBook>, string, string, ProbeKey, PkBook, PkNoFilter, IdentitySelector<string>>(
			_authorCountrySymIdx, books, bookCountryIdx, default, default);
		var log = new SlotLog<PkBook>();
		var container = new RecordingContainer<PkBook>(log);

		var hashes = 0;
		ProbeKey.Arm(_ => hashes++);
		try {
			((IJoinManyResolver<int, MlsAuthor, PkBook>)resolver).ExecuteReverseMany(ref container, new[] { 1, 2 });
		} finally {
			ProbeKey.Disarm();
		}

		Assert.That(hashes, Is.EqualTo(6 + 3), "six pairs walked, three distinct rights looked up in the store, none for slots");
		Assert.That(Ids(log, 1, b => b.Id), Is.EquivalentTo(new[] { 100, 101, 102 }));
		Assert.That(Ids(log, 2, b => b.Id), Is.EquivalentTo(new[] { 100, 101, 102 }));
	}

	// The bucket enumerator yields a right twice when the writer removes it while the walk sits on
	// it, hands its slot to another right and re-adds it into a slot freed earlier that the walk has
	// not reached yet. The fan-out sees the second sighting as a repeat for the same left (the
	// right's chain head is this left), records nothing, and the slot reserves and receives the
	// right once: the exactness invariant (Adds == Capacity) holds with no duplicate.
	[Test]
	public void LeftSym_RightRemovedAndReaddedMidWalk_IsRecordedAndDeliveredOnce() {
		var books = new InMemoryDataCache<ProbeKey, PkBook>();
		var bookCountryIdx = books.CacheKeyValueListIndex<string>((_, v) => v.Country);
		Author(1, "UK");
		for (var i = 100; i < 105; i++) {
			books.AddOrUpdate(new ProbeKey(i), new PkBook { Id = i, Country = "UK", Title = "Book" + i });
		}

		// Free bucket slot 3 (book 103) ahead of the walk: the re-added right lands there.
		books.Remove(new ProbeKey(103));

		var resolver = new JoinManyLeftSymResolver<int, MlsAuthor, InMemoryDataCache<ProbeKey, PkBook>, string, string, ProbeKey, PkBook, PkNoFilter, IdentitySelector<string>>(
			_authorCountrySymIdx, books, bookCountryIdx, default, default);
		var log = new SlotLog<PkBook>();
		var container = new RecordingContainer<PkBook>(log);

		// The walk hashes one right per pair in slot order, so call 2 is the pair (1, 101).
		ProbeKey.Arm(call => {
			if (call != 2) {
				return;
			}

			ProbeKey.Disarm();
			books.Remove(new ProbeKey(101));
			books.AddOrUpdate(new ProbeKey(999), new PkBook { Id = 999, Country = "UK", Title = "Late" }); // takes the slot 101 freed, behind the walk
			books.AddOrUpdate(new ProbeKey(101), new PkBook { Id = 101, Country = "UK", Title = "Book101" }); // lands in the slot 103 freed, ahead of it
		});
		try {
			((IJoinManyResolver<int, MlsAuthor, PkBook>)resolver).ExecuteReverseMany(ref container, new[] { 1 });
		} finally {
			ProbeKey.Disarm();
		}

		Assert.That(log.Capacity.GetValueOrDefault(1), Is.EqualTo(4), "100, 101, 102, 104: the second 101 is a repeat");
		Assert.That(log.Adds.GetValueOrDefault(1), Is.EqualTo(4), "the slot receives exactly what it reserved");
		Assert.That(Ids(log, 1, b => b.Id), Is.EquivalentTo(new[] { 100, 101, 102, 104 }),
			"101 once; 999 was never walked, 103 left before the walk");
	}

	// ═════════════════════════════════════════════════════════════════════════
	// JoinManyLeftSymResolver — inner path (public API; the join filter predicate runs inside the
	// store walk immediately before each Add, after all slots are sized and partitioned)
	// ═════════════════════════════════════════════════════════════════════════

	// A candidate left sized for DE (1 right) is moved into UK (2 rights) while the paired execute runs;
	// the moved left's pairs were recorded from DE, so the real container never overflows.
	[Test]
	public void InnerJoinMany_LeftSym_LeftMovedToBiggerGroupDuringExecution_DoesNotOverflow() {
		Author(3, "DE");
		Author(1, "UK");
		Book(200, "DE");
		Book(100, "UK");
		Book(101, "UK");

		var authors = _authors;
		var moved = false;
		Assert.That(() => {
			var results = authors.Query()
				.InnerJoinMany(_authorCountrySymIdx, _books, _bookCountryIdx, q => q.Where(_ => {
					if (!moved) {
						moved = true;
						authors.AddOrUpdate(3, new MlsAuthor { Id = 3, Country = "UK", Name = "Author3" });
					}

					return true;
				}))
				.Execute();
			Assert.That(results.Count, Is.EqualTo(2));
		}, Throws.Nothing, "inner JoinMany overflowed a slot after a left changed group during execution");
	}

	// ═════════════════════════════════════════════════════════════════════════
	// JoinManyCollectionResolver — outer path (ExecuteReverseMany, reverse direction tag -> books)
	// ═════════════════════════════════════════════════════════════════════════

	// A new owner containing this left lands after the left's pairs were recorded and its slot
	// sized: not in any pair set, so neither delivered nor overflowing.
	[Test]
	public void Collection_OwnerAddedAfterSlotSized_SlotReceivesExactlyTheRecordedOwners() {
		Tag(10);
		for (var i = 0; i < 3; i++) {
			TaggedBook(100 + i, 10);
		}

		var resolver = CollectionResolver();
		var books = _taggedBooks;
		var log = new SlotLog<MnTaggedBook> {
			OnInit = _ => books.AddOrUpdate(999, new MnTaggedBook { Id = 999, Title = "Late", TagIds = new List<int> { 10 } })
		};
		var container = new RecordingContainer<MnTaggedBook>(log);

		((IJoinManyResolver<int, MnTag, MnTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 10 });

		Assert.That(log.Capacity.GetValueOrDefault(10), Is.EqualTo(3));
		Assert.That(Ids(log, 10, b => b.Id), Is.EquivalentTo(new[] { 100, 101, 102 }));
	}

	// An owner recorded from ANOTHER left's walk gains this left after this left was sized. Pairs
	// are (left, owner) as recorded per left — the owner's new membership is not delivered to the
	// already-sized slot.
	[Test]
	public void Collection_OwnerGainsAlreadySizedLeftDuringWalk_SlotReceivesExactlyTheRecordedOwners() {
		Tag(10);
		Tag(20);
		TaggedBook(1, 20);
		TaggedBook(2, 10);

		var resolver = CollectionResolver();
		var books = _taggedBooks;
		var log = new SlotLog<MnTaggedBook> {
			OnInit = key => {
				if (key == 20) {
					books.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Book1", TagIds = new List<int> { 20, 10 } });
				}
			}
		};
		var container = new RecordingContainer<MnTaggedBook>(log);

		((IJoinManyResolver<int, MnTag, MnTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 10, 20 });

		Assert.That(Ids(log, 10, b => b.Id), Is.EqualTo(new[] { 2 }), "tag 10 recorded only book 2");
		Assert.That(Ids(log, 20, b => b.Id), Is.EqualTo(new[] { 1 }));
	}

	// Same writer, landing after the whole walk — inside PrepareSharedBuffer, when every slot is
	// already sized and the pairs are sealed.
	[Test]
	public void Collection_OwnerGainsLeftAfterAllSlotsSized_SlotReceivesExactlyTheRecordedOwners() {
		Tag(10);
		Tag(20);
		TaggedBook(1, 20);
		TaggedBook(2, 10);

		var resolver = CollectionResolver();
		var books = _taggedBooks;
		var log = new SlotLog<MnTaggedBook> {
			OnPrepareSharedBuffer = () => books.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Book1", TagIds = new List<int> { 20, 10 } })
		};
		var container = new RecordingContainer<MnTaggedBook>(log);

		((IJoinManyResolver<int, MnTag, MnTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 10, 20 });

		Assert.That(Ids(log, 10, b => b.Id), Is.EqualTo(new[] { 2 }), "tag 10 recorded only book 2");
		Assert.That(Ids(log, 20, b => b.Id), Is.EqualTo(new[] { 1 }));
	}

	// A left with NO owners at walk time is skipped (capacity stays 0); an owner already in the pair
	// set gains it before the paired execute. The zero-capacity slot never receives an Add.
	[Test]
	public void Collection_LeftWithoutOwnersGainsOneBeforeExecution_ZeroCapacitySlotNeverReceivesAdds() {
		Tag(10);
		Tag(20);
		TaggedBook(1, 20);

		var resolver = CollectionResolver();
		var books = _taggedBooks;
		var log = new SlotLog<MnTaggedBook> {
			OnInit = key => {
				if (key == 20) {
					books.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Book1", TagIds = new List<int> { 20, 10 } });
				}
			}
		};
		var container = new RecordingContainer<MnTaggedBook>(log);

		((IJoinManyResolver<int, MnTag, MnTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 10, 20 });

		Assert.That(log.Capacity.ContainsKey(10), Is.False, "tag 10 had no owners at its turn — no Init");
		Assert.That(log.Adds.GetValueOrDefault(10), Is.Zero, "slot for tag 10 (never sized) received owners");
		Assert.That(Ids(log, 20, b => b.Id), Is.EqualTo(new[] { 1 }));
	}

	// While the delivery adds (10, book 1), the writer removes this left from the owner and re-adds
	// it. Pairs are fixed at record time, so the owner reaches slot 10 exactly once and slot 20
	// (next in the owner's chain) still receives the owner it recorded.
	[Test]
	public void Collection_LeftRemovedAndReaddedDuringExecution_OwnerIsDeliveredOncePerLeft() {
		Tag(10);
		Tag(20);
		TaggedBook(1, 10, 20);

		var resolver = CollectionResolver();
		var books = _taggedBooks;
		var fired = false;
		var log = new SlotLog<MnTaggedBook> {
			OnAdd = (key, book) => {
				if (fired || key != 10 || book.Id != 1) {
					return;
				}

				fired = true;
				books.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Book1", TagIds = new List<int> { 30 } });
				books.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Book1", TagIds = new List<int> { 30, 10 } });
			}
		};
		var container = new RecordingContainer<MnTaggedBook>(log);

		((IJoinManyResolver<int, MnTag, MnTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 10, 20 });

		Assert.That(fired, Is.True);
		Assert.That(log.Adds.GetValueOrDefault(10), Is.EqualTo(1), "slot for tag 10 received the same owner twice");
		Assert.That(log.Adds.GetValueOrDefault(20), Is.EqualTo(1), "slot for tag 20 must still receive the owner it recorded");
	}

	// The owner is removed and re-added (a fresh index entry and element set) after its pair was
	// recorded. The pair carries the owner's KEY, not its element set, so the re-added owner is
	// delivered from the store.
	[Test]
	public void Collection_OwnerRemovedAndReaddedAfterPairRecorded_IsStillDelivered() {
		Tag(10);
		Tag(20);
		TaggedBook(1, 10);
		TaggedBook(2, 20);

		var resolver = CollectionResolver();
		var books = _taggedBooks;
		var log = new SlotLog<MnTaggedBook> {
			OnInit = key => {
				if (key == 20) {
					books.Remove(1);
					books.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Book1", TagIds = new List<int> { 10 } });
				}
			}
		};
		var container = new RecordingContainer<MnTaggedBook>(log);

		((IJoinManyResolver<int, MnTag, MnTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 10, 20 });

		Assert.That(Ids(log, 10, b => b.Id), Is.EqualTo(new[] { 1 }),
			"book 1 is in the store at execution time yet was not delivered to tag 10");
	}

	// Collection twin of LeftSym_NoSharedRights_DeliversWithoutSlotLookups: owners referenced by one
	// tag each are delivered through the plain paired execute — two hashes per pair, none for slots.
	[Test]
	public void Collection_NoSharedOwners_DeliversWithoutSlotLookups() {
		var owners = new InMemoryDataCache<ProbeKey, PkTaggedBook>();
		var index = owners.CacheCollectionSymmetricKeyValueListIndex<int>((_, b) => b.TagIds);
		Tag(10);
		Tag(20);
		for (var i = 100; i < 102; i++) {
			owners.AddOrUpdate(new ProbeKey(i), new PkTaggedBook { Id = i, Title = "Book" + i, TagIds = new List<int> { 10 } });
			owners.AddOrUpdate(new ProbeKey(i + 100), new PkTaggedBook { Id = i + 100, Title = "Book" + (i + 100), TagIds = new List<int> { 20 } });
		}

		var resolver = new JoinManyCollectionResolver<int, MnTag, InMemoryDataCache<ProbeKey, PkTaggedBook>, ProbeKey, PkTaggedBook, PkTaggedBook, PkTbNoFilter>(
			index.Forward, owners, default);
		var log = new SlotLog<PkTaggedBook>();
		var container = new RecordingContainer<PkTaggedBook>(log);

		var hashes = 0;
		ProbeKey.Arm(_ => hashes++);
		try {
			((IJoinManyResolver<int, MnTag, PkTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 10, 20 });
		} finally {
			ProbeKey.Disarm();
		}

		Assert.That(hashes, Is.EqualTo(2 * 4), "one hash per pair for the walk, one per pair for the store lookup, none for slots");
		Assert.That(Ids(log, 10, b => b.Id), Is.EquivalentTo(new[] { 100, 101 }));
		Assert.That(Ids(log, 20, b => b.Id), Is.EquivalentTo(new[] { 200, 201 }));
	}

	// One owner carrying both tags turns the fan-out delivery on: two pairs walked, one distinct owner
	// looked up in the store and delivered by pair slot to both tags.
	[Test]
	public void Collection_SharedOwner_DeliversThroughTheFanOut_NoSlotLookups() {
		var owners = new InMemoryDataCache<ProbeKey, PkTaggedBook>();
		var index = owners.CacheCollectionSymmetricKeyValueListIndex<int>((_, b) => b.TagIds);
		Tag(10);
		Tag(20);
		owners.AddOrUpdate(new ProbeKey(100), new PkTaggedBook { Id = 100, Title = "Both", TagIds = new List<int> { 10, 20 } });

		var resolver = new JoinManyCollectionResolver<int, MnTag, InMemoryDataCache<ProbeKey, PkTaggedBook>, ProbeKey, PkTaggedBook, PkTaggedBook, PkTbNoFilter>(
			index.Forward, owners, default);
		var log = new SlotLog<PkTaggedBook>();
		var container = new RecordingContainer<PkTaggedBook>(log);

		var hashes = 0;
		ProbeKey.Arm(_ => hashes++);
		try {
			((IJoinManyResolver<int, MnTag, PkTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 10, 20 });
		} finally {
			ProbeKey.Disarm();
		}

		Assert.That(hashes, Is.EqualTo(2 + 1), "two pairs walked, one distinct owner looked up in the store, none for slots");
		Assert.That(Ids(log, 10, b => b.Id), Is.EqualTo(new[] { 100 }));
		Assert.That(Ids(log, 20, b => b.Id), Is.EqualTo(new[] { 100 }));
	}

	// Collection twin of LeftSym_RightRemovedAndReaddedMidWalk_IsRecordedAndDeliveredOnce: an
	// owner removed and re-added under the walk of its tag's Forward bucket is a repeat sighting for
	// that tag — recorded once, delivered once, the slot reserved one.
	[Test]
	public void Collection_OwnerRemovedAndReaddedMidWalk_IsRecordedAndDeliveredOnce() {
		var owners = new InMemoryDataCache<ProbeKey, PkTaggedBook>();
		var index = owners.CacheCollectionSymmetricKeyValueListIndex<int>((_, b) => b.TagIds);
		Tag(10);
		for (var i = 100; i < 105; i++) {
			owners.AddOrUpdate(new ProbeKey(i), new PkTaggedBook { Id = i, Title = "Book" + i, TagIds = new List<int> { 10 } });
		}

		owners.Remove(new ProbeKey(103));

		var resolver = new JoinManyCollectionResolver<int, MnTag, InMemoryDataCache<ProbeKey, PkTaggedBook>, ProbeKey, PkTaggedBook, PkTaggedBook, PkTbNoFilter>(
			index.Forward, owners, default);
		var log = new SlotLog<PkTaggedBook>();
		var container = new RecordingContainer<PkTaggedBook>(log);

		ProbeKey.Arm(call => {
			if (call != 2) {
				return;
			}

			ProbeKey.Disarm();
			owners.Remove(new ProbeKey(101));
			owners.AddOrUpdate(new ProbeKey(999), new PkTaggedBook { Id = 999, Title = "Late", TagIds = new List<int> { 10 } });
			owners.AddOrUpdate(new ProbeKey(101), new PkTaggedBook { Id = 101, Title = "Book101", TagIds = new List<int> { 10 } });
		});
		try {
			((IJoinManyResolver<int, MnTag, PkTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 10 });
		} finally {
			ProbeKey.Disarm();
		}

		Assert.That(log.Capacity.GetValueOrDefault(10), Is.EqualTo(4));
		Assert.That(log.Adds.GetValueOrDefault(10), Is.EqualTo(4));
		Assert.That(Ids(log, 10, b => b.Id), Is.EquivalentTo(new[] { 100, 101, 102, 104 }));
	}

	// The writer publishes Forward (element -> owners) before Reverse (owner -> elements); the walk
	// falls between the two. The resolver reads only the Forward half plus the store, so the owner
	// is delivered even though its Reverse entry does not exist yet. Emulated by driving the index
	// halves directly, one per hook.
	[Test]
	public void Collection_OwnerSeenInForwardBeforeReverse_IsStillDelivered() {
		Tag(5);
		Tag(10);
		Tag(7);
		TaggedBook(500, 5);
		TaggedBook(100, 10);
		TaggedBook(700, 7);

		var resolver = CollectionResolver();
		var books = _taggedBooks;
		var index = _tagIndex;
		var comparer = default(DefaultKeyComparer<int>);
		var log = new SlotLog<MnTaggedBook> {
			OnInit = key => {
				if (key == 5) {
					// Store node plus the Forward half only: tag 10 -> {100, 999}.
					books.AddOrUpdate(999, new MnTaggedBook { Id = 999, Title = "Late", TagIds = new List<int>() });
					index.Forward.AddUnderKey(10, 999, comparer.GetHashCode(999), 0);
				} else if (key == 7) {
					// The Reverse half lands after tag 10's walk: 999 -> {10}.
					index.Reverse.AddUnderKey(999, 10, comparer.GetHashCode(10), 0);
				}
			}
		};
		var container = new RecordingContainer<MnTaggedBook>(log);

		((IJoinManyResolver<int, MnTag, MnTaggedBook>)resolver).ExecuteReverseMany(ref container, new[] { 5, 10, 7 });

		Assert.That(log.Capacity.GetValueOrDefault(10), Is.EqualTo(2));
		Assert.That(Ids(log, 10, b => b.Id), Is.EquivalentTo(new[] { 100, 999 }),
			"book 999 is in the store and in the Forward half at record time yet was not delivered to tag 10");
	}

	// ═════════════════════════════════════════════════════════════════════════
	// JoinManyCollectionResolver — public API, real container (outer and inner paths); the join
	// filter predicate runs inside the store walk right before each Add
	// ═════════════════════════════════════════════════════════════════════════

	// Outer: an owner gains an already-sized tag while the paired execute runs; the real container must
	// not overflow because the new membership was never recorded as a pair.
	[Test]
	public void JoinManyCollection_OwnerGainsSizedTagDuringExecution_DoesNotOverflow() {
		Tag(10);
		Tag(20);
		TaggedBook(1, 20);
		TaggedBook(2, 10);

		var books = _taggedBooks;
		var moved = false;
		Assert.That(() => {
			var results = _tags.Query()
				.JoinManyCollection(books, _tagIndex, q => q.Where(_ => {
					if (!moved) {
						moved = true;
						books.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Book1", TagIds = new List<int> { 20, 10 } });
					}

					return true;
				}))
				.Execute();
			Assert.That(results.Count, Is.EqualTo(2));
		}, Throws.Nothing, "outer JoinManyCollection overflowed a slot after an owner gained a tag during execution");
	}

	// Inner: same interleaving on the candidate-narrowed path.
	[Test]
	public void InnerJoinManyCollection_OwnerGainsSizedTagDuringExecution_DoesNotOverflow() {
		Tag(10);
		Tag(20);
		TaggedBook(1, 20);
		TaggedBook(2, 10);

		var books = _taggedBooks;
		var moved = false;
		Assert.That(() => {
			var results = _tags.Query()
				.InnerJoinManyCollection(books, _tagIndex, q => q.Where(_ => {
					if (!moved) {
						moved = true;
						books.AddOrUpdate(1, new MnTaggedBook { Id = 1, Title = "Book1", TagIds = new List<int> { 20, 10 } });
					}

					return true;
				}))
				.Execute();
			Assert.That(results.Count, Is.EqualTo(2));
		}, Throws.Nothing, "inner JoinManyCollection overflowed a slot after an owner gained a tag during execution");
	}

	// ═════════════════════════════════════════════════════════════════════════
	// Real interleavings: a single writer thread churns the indexes while queries run back to back.
	// Bounded to the same wall-clock budget as JoinManyRightListIndexConcurrentMutationTests.
	// ═════════════════════════════════════════════════════════════════════════

	// The writer moves authors between countries and grows/trims every country's book bucket while
	// outer and inner LeftSym JoinMany queries (pooled) run: no slot overflow, no exception.
	[Test]
	public void LeftSym_LiveLeftMovesAndRightWrites_NeverThrows() {
		const int authors = 6;
		const int booksPerCountry = 64;
		var countries = new[] { "UK", "DE" };
		var next = new int[countries.Length];
		for (var a = 1; a <= authors; a++) {
			Author(a, countries[a % countries.Length]);
		}

		for (var c = 0; c < countries.Length; c++) {
			for (var b = 0; b < booksPerCountry; b++) {
				Book(BookId(c, next[c]++), countries[c]);
			}
		}

		var stop = false;
		var rounds = 0;
		var writer = new Thread(() => {
			while (!Volatile.Read(ref stop)) {
				// Move one author to the other country every round, then grow and trim each bucket.
				var author = 1 + rounds % authors;
				Author(author, countries[(author + rounds) % countries.Length]);
				for (var c = 0; c < countries.Length; c++) {
					Book(BookId(c, next[c]++), countries[c]);
					_books.Remove(BookId(c, next[c] - 1 - booksPerCountry));
				}

				rounds++;
			}
		});

		Exception? failure = null;
		var queries = 0;
		writer.Start();
		try {
			var sw = Stopwatch.StartNew();
			while (sw.Elapsed < Duration) {
				try {
					if ((queries & 1) == 0) {
						using var results = _authors.Query().JoinMany(_authorCountrySymIdx, _books, _bookCountryIdx).ExecutePooled();
					} else {
						using var results = _authors.Query().InnerJoinMany(_authorCountrySymIdx, _books, _bookCountryIdx).ExecutePooled();
					}

					queries++;
				} catch (Exception e) {
					failure = e;
					break;
				}
			}
		} finally {
			Volatile.Write(ref stop, true);
			writer.Join();
		}

		Assert.That(failure, Is.Null, $"LeftSym JoinMany threw after {queries} queries / {rounds} writer rounds: {failure}");
	}

	// The writer rewrites every book's tag list (rotating which tags it carries) and grows/trims the
	// book set while reverse and forward collection JoinMany queries (pooled) run: no exception.
	[Test]
	public void Collection_LiveCollectionRewrites_NeverThrows() {
		const int tags = 4;
		const int books = 64;
		for (var t = 1; t <= tags; t++) {
			Tag(t);
		}

		var next = 0;
		for (var b = 0; b < books; b++) {
			TaggedBook(next, TagsFor(next, 0));
			next++;
		}

		var stop = false;
		var rounds = 0;
		var writer = new Thread(() => {
			while (!Volatile.Read(ref stop)) {
				// Rewrite every live book's tags for this generation, then add one and trim the oldest.
				for (var id = next - books; id < next; id++) {
					TaggedBook(id, TagsFor(id, rounds + 1));
				}

				TaggedBook(next, TagsFor(next, rounds + 1));
				next++;
				_taggedBooks.Remove(next - 1 - books);
				rounds++;
			}
		});

		Exception? failure = null;
		var queries = 0;
		writer.Start();
		try {
			var sw = Stopwatch.StartNew();
			while (sw.Elapsed < Duration) {
				try {
					switch (queries & 3) {
						case 0: {
							using var results = _tags.Query().JoinManyCollection(_taggedBooks, _tagIndex).ExecutePooled();
							break;
						}
						case 1: {
							using var results = _tags.Query().InnerJoinManyCollection(_taggedBooks, _tagIndex).ExecutePooled();
							break;
						}
						case 2: {
							using var results = _taggedBooks.Query().JoinManyCollectionForward(_tags, _tagIndex).ExecutePooled();
							break;
						}
						default: {
							using var results = _taggedBooks.Query().InnerJoinManyCollectionForward(_tags, _tagIndex).ExecutePooled();
							break;
						}
					}

					queries++;
				} catch (Exception e) {
					failure = e;
					break;
				}
			}
		} finally {
			Volatile.Write(ref stop, true);
			writer.Join();
		}

		Assert.That(failure, Is.Null, $"collection JoinMany threw after {queries} queries / {rounds} writer rounds: {failure}");

		// A book carries 1..3 of the 4 tags depending on its id and the writer generation.
		static int[] TagsFor(int id, int generation) {
			var count = 1 + (id + generation) % 3;
			var result = new int[count];
			for (var i = 0; i < count; i++) {
				result[i] = 1 + (id + generation + i) % 4;
			}

			return result;
		}
	}

	private static int BookId(int country, int ordinal) => (country + 1) * 1_000_000 + ordinal;

	// ── Recording scaffolding ────────────────────────────────────────────────

	private sealed class SlotLog<TRight> {
		public readonly Dictionary<int, int> Capacity = new();
		public readonly Dictionary<int, int> Adds = new();
		public readonly Dictionary<int, List<TRight>> Delivered = new();
		public Action<int>? OnInit;
		public Action<int, TRight>? OnAdd;
		public Action? OnPrepareSharedBuffer;
	}

	// Mirrors the resolver's own keyed container contract without a backing buffer: records what
	// Init reserved and what Add delivered per left, and lets a test stand in for the writer at
	// three points of the resolver's life cycle.
	private readonly struct RecordingContainer<TRight> : IJoinedKeyedResultContainer<int, TRight> {
		private readonly SlotLog<TRight> _log;

		public RecordingContainer(SlotLog<TRight> log) => _log = log;

		public void Init(int key, int maxCount) {
			_log.Capacity[key] = _log.Capacity.GetValueOrDefault(key) + maxCount;
			_log.OnInit?.Invoke(key);
		}

		public void Seal(int key, int actualCount) {
		}

		public int Add(int foreignKey, TRight result) {
			_log.OnAdd?.Invoke(foreignKey, result);
			var index = _log.Adds.GetValueOrDefault(foreignKey);
			_log.Adds[foreignKey] = index + 1;
			if (!_log.Delivered.TryGetValue(foreignKey, out var list)) {
				list = new List<TRight>();
				_log.Delivered[foreignKey] = list;
			}

			list.Add(result);
			return index;
		}

		public int TotalCount {
			get {
				var total = 0;
				foreach (var capacity in _log.Capacity.Values) {
					total += capacity;
				}

				return total;
			}
		}

		public void PrepareSharedBuffer() => _log.OnPrepareSharedBuffer?.Invoke();

		public TRight[]? GetSharedBuffer() => null;
	}
}
