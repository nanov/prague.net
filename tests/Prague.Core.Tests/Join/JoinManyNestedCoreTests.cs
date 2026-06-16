namespace Prague.Core.Tests.Join;

using System.Linq;
using Prague.Core;
using NUnit.Framework;

// ── Domain ──────────────────────────────────────────────────────────────────
// Nested-join verification: Room → JoinMany(Desk) → each Desk JoinOne(Occupier).
//
// Room  1:N Desk   (Desk.RoomId list index)
// Desk  1:1 Occupier (PK-to-PK: Occupier.Id == Desk.Id)
//
// Result shape: QueryResults<JoinResult<Room, QueryResults<JoinResult<Desk, Occupier?>>>>

internal sealed class NRoom : ICacheEquatable<NRoom>, ICacheClonable<NRoom> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public bool CacheEquals(NRoom? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public NRoom Clone() => new() { Id = Id, Name = Name };
}

internal sealed class NDesk : ICacheEquatable<NDesk>, ICacheClonable<NDesk> {
	public int Id { get; init; }
	public int RoomId { get; init; }
	public string Label { get; init; } = "";
	public bool CacheEquals(NDesk? other) => other is not null && other.Id == Id && other.RoomId == RoomId && other.Label == Label;
	public int CacheGetHashCode() => HashCode.Combine(Id, RoomId, Label);
	public NDesk Clone() => new() { Id = Id, RoomId = RoomId, Label = Label };
}

internal sealed class NOccupier : ICacheEquatable<NOccupier>, ICacheClonable<NOccupier> {
	public int Id { get; init; }
	public string Name { get; init; } = "";
	public bool CacheEquals(NOccupier? other) => other is not null && other.Id == Id && other.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public NOccupier Clone() => new() { Id = Id, Name = Name };
}

internal sealed class NTag : ICacheEquatable<NTag>, ICacheClonable<NTag> {
	public int Id { get; init; }
	public int DeskId { get; init; }
	public string Text { get; init; } = "";
	public bool CacheEquals(NTag? other) => other is not null && other.Id == Id && other.DeskId == DeskId && other.Text == Text;
	public int CacheGetHashCode() => HashCode.Combine(Id, DeskId, Text);
	public NTag Clone() => new() { Id = Id, DeskId = DeskId, Text = Text };
}

[TestFixture]
public class JoinManyNestedCoreTests {
	private InMemoryDataCache<int, NRoom> _rooms = null!;
	private InMemoryDataCache<int, NDesk> _desks = null!;
	private InMemoryDataCache<int, NOccupier> _occupiers = null!;
	private InMemoryDataCache<int, NTag> _tags = null!;
	private CacheKeyValueListIndex<int, NDesk, int> _deskByRoomId = null!;
	private CacheKeyValueListIndex<int, NTag, int> _tagByDeskId = null!;

	[SetUp]
	public void SetUp() {
		_rooms = new InMemoryDataCache<int, NRoom>();
		_desks = new InMemoryDataCache<int, NDesk>();
		_occupiers = new InMemoryDataCache<int, NOccupier>();
		_tags = new InMemoryDataCache<int, NTag>();
		_deskByRoomId = _desks.CacheKeyValueListIndex<int>((_, v) => v.RoomId);
		_tagByDeskId = _tags.CacheKeyValueListIndex<int>((_, v) => v.DeskId);

		_rooms.AddOrUpdate(1, new NRoom { Id = 1, Name = "Alpha" });
		_rooms.AddOrUpdate(2, new NRoom { Id = 2, Name = "Beta" });
		_rooms.AddOrUpdate(3, new NRoom { Id = 3, Name = "Empty" }); // no desks

		_desks.AddOrUpdate(10, new NDesk { Id = 10, RoomId = 1, Label = "A1" });
		_desks.AddOrUpdate(11, new NDesk { Id = 11, RoomId = 1, Label = "A2" });
		_desks.AddOrUpdate(20, new NDesk { Id = 20, RoomId = 2, Label = "B1" });

		_occupiers.AddOrUpdate(10, new NOccupier { Id = 10, Name = "Alice" }); // desk 10
		_occupiers.AddOrUpdate(20, new NOccupier { Id = 20, Name = "Carol" }); // desk 20
		// desk 11 has no occupier → inner Right null

		// Tags: desk 10 → 2 tags, desk 20 → 1 tag, desk 11 → none.
		_tags.AddOrUpdate(100, new NTag { Id = 100, DeskId = 10, Text = "window" });
		_tags.AddOrUpdate(101, new NTag { Id = 101, DeskId = 10, Text = "standing" });
		_tags.AddOrUpdate(200, new NTag { Id = 200, DeskId = 20, Text = "corner" });
	}

	[Test]
	public void NestedJoinMany_InnerJoinMany_Depth3_Execute() {
		// QueryResults<JoinResult<Room, QueryResults<JoinResult<Desk, QueryResults<Tag>>>>>
		var results = _rooms.Query()
			.JoinMany(_desks, _deskByRoomId, db => db.JoinMany(_tags, _tagByDeskId))
			.Execute();

		var byRoom = results.ToDictionary(r => r.Left.Id);
		var room1Desks = byRoom[1].Right.ToDictionary(jr => jr.Left.Id);
		Assert.That(room1Desks[10].Right.Count, Is.EqualTo(2)); // desk 10 → 2 tags
		Assert.That(room1Desks[11].Right.Count, Is.EqualTo(0)); // desk 11 → 0 tags
		Assert.That(room1Desks[10].Right.Select(t => t.Text).OrderBy(t => t),
			Is.EqualTo(new[] { "standing", "window" }));

		var room2Desks = byRoom[2].Right.ToDictionary(jr => jr.Left.Id);
		Assert.That(room2Desks[20].Right.Count, Is.EqualTo(1));
		Assert.That(room2Desks[20].Right[0].Text, Is.EqualTo("corner"));
	}

	[Test]
	public void NestedJoinMany_InnerJoinMany_Depth3_ExecutePooled() {
		using var pooled = _rooms.Query()
			.JoinMany(_desks, _deskByRoomId, db => db.JoinMany(_tags, _tagByDeskId))
			.ExecutePooled();

		// Read tag CONTENTS (not just Count) inside the using — exercises the absorbed inner-Many
		// buffer; a leaked/returned buffer would corrupt these.
		var tagsByDesk = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<string>>();
		foreach (var roomRow in pooled)
			foreach (var deskRow in roomRow.Right)
				tagsByDesk[deskRow.Left.Id] = deskRow.Right.Select(t => t.Text).OrderBy(t => t).ToList();

		Assert.That(tagsByDesk[10], Is.EqualTo(new[] { "standing", "window" }));
		Assert.That(tagsByDesk[11], Is.Empty);
		Assert.That(tagsByDesk[20], Is.EqualTo(new[] { "corner" }));
	}

	[Test]
	public void NestedJoinMany_ExecuteCloned_DeepIsolation() {
		_desks.TryGet(10, out var srcDesk);
		_occupiers.TryGet(10, out var srcOccupier);

		var results = _rooms.Query()
			.JoinMany(_desks, _deskByRoomId, db => db.JoinOne(_occupiers))
			.ExecuteCloned();

		var byRoom = results.ToDictionary(r => r.Left.Id);
		var room1Desks = byRoom[1].Right.ToDictionary(jr => jr.Left.Id);

		// Content matches…
		Assert.That(room1Desks[10].Left.Label, Is.EqualTo("A1"));
		Assert.That(room1Desks[10].Right!.Name, Is.EqualTo("Alice"));
		// …but the inner Desk/Occupier are CLONES, not the cache's instances (deep isolation).
		// Immutable POCOs can't show in-place mutation, so reference identity is the real check.
		Assert.That(ReferenceEquals(room1Desks[10].Left, srcDesk), Is.False, "cloned desk must be a distinct instance");
		Assert.That(ReferenceEquals(room1Desks[10].Right, srcOccupier), Is.False, "cloned occupier must be a distinct instance");
	}

	[Test]
	public void NestedJoinMany_InnerJoinOne_Execute() {
		var results = _rooms.Query()
			.JoinMany(_desks, _deskByRoomId, db => db.JoinOne(_occupiers))
			.Execute();

		Assert.That(results.Count, Is.EqualTo(3));
		var byRoom = results.ToDictionary(r => r.Left.Id);

		// Room 1: desks 10 (Alice) and 11 (null occupier).
		Assert.That(byRoom[1].Right.Count, Is.EqualTo(2));
		var room1Desks = byRoom[1].Right.ToDictionary(jr => jr.Left.Id);
		Assert.That(room1Desks[10].Right, Is.Not.Null);
		Assert.That(room1Desks[10].Right!.Name, Is.EqualTo("Alice"));
		Assert.That(room1Desks[11].Right, Is.Null, "desk 11 has no occupier → outer inner-join keeps null");

		// Room 2: desk 20 (Carol).
		Assert.That(byRoom[2].Right.Count, Is.EqualTo(1));
		var room2Desks = byRoom[2].Right.ToDictionary(jr => jr.Left.Id);
		Assert.That(room2Desks[20].Right!.Name, Is.EqualTo("Carol"));

		// Room 3: no desks → empty inner collection.
		Assert.That(byRoom[3].Right.Count, Is.EqualTo(0));
	}

	[Test]
	public void NestedJoinMany_InnerJoinOne_ExecutePooled_MatchesExecute() {
		var expected = _rooms.Query()
			.JoinMany(_desks, _deskByRoomId, db => db.JoinOne(_occupiers))
			.Execute();
		var expByRoom = expected.ToDictionary(r => r.Left.Id);

		using var pooled = _rooms.Query()
			.JoinMany(_desks, _deskByRoomId, db => db.JoinOne(_occupiers))
			.ExecutePooled();

		// Read all contents INSIDE the using block — ToDictionary() would dispose `pooled`
		// and return its pooled buffers, invalidating the inner collections.
		Assert.That(pooled.Count, Is.EqualTo(expected.Count));
		foreach (var roomRow in pooled) {
			var exp = expByRoom[roomRow.Left.Id];
			Assert.That(roomRow.Right.Count, Is.EqualTo(exp.Right.Count));
			foreach (var deskRow in roomRow.Right) {
				var expDesk = exp.Right.First(d => d.Left.Id == deskRow.Left.Id);
				Assert.That(deskRow.Right?.Name, Is.EqualTo(expDesk.Right?.Name));
			}
		}
	}

	[Test]
	public void NestedJoinMany_InnerFilter_Execute() {
		// Inner filter lives entirely inside the continuation lambda.
		var results = _rooms.Query()
			.JoinMany(_desks, _deskByRoomId,
				db => db.JoinOne(_occupiers, q => q.Where(o => o.Name.StartsWith("A"))))
			.Execute();

		var byRoom = results.ToDictionary(r => r.Left.Id);
		var room1Desks = byRoom[1].Right.ToDictionary(jr => jr.Left.Id);
		Assert.That(room1Desks[10].Right!.Name, Is.EqualTo("Alice"));   // passes filter
		var room2Desks = byRoom[2].Right.ToDictionary(jr => jr.Left.Id);
		Assert.That(room2Desks[20].Right, Is.Null, "Carol filtered out → desk 20 keeps null occupier");
	}

	[Test]
	public void NestedInnerJoinMany_DropsParentsWithEmptyInner() {
		// Room A: desk 10 (occupied) + desk 11 (no occupier). Room B: desk 20 (no occupier).
		// Room C: no desks.
		var rooms = new InMemoryDataCache<int, NRoom>();
		var desks = new InMemoryDataCache<int, NDesk>();
		var occ = new InMemoryDataCache<int, NOccupier>();
		var deskByRoom = desks.CacheKeyValueListIndex<int>((_, v) => v.RoomId);

		rooms.AddOrUpdate(1, new NRoom { Id = 1, Name = "A" });
		rooms.AddOrUpdate(2, new NRoom { Id = 2, Name = "B" });
		rooms.AddOrUpdate(3, new NRoom { Id = 3, Name = "C" });
		desks.AddOrUpdate(10, new NDesk { Id = 10, RoomId = 1 });
		desks.AddOrUpdate(11, new NDesk { Id = 11, RoomId = 1 });
		desks.AddOrUpdate(20, new NDesk { Id = 20, RoomId = 2 });
		occ.AddOrUpdate(10, new NOccupier { Id = 10, Name = "Alice" });

		// Outer InnerJoinMany + inner (outer) JoinOne: only Room C (no desks) is dropped.
		var outerInner = rooms.Query()
			.InnerJoinMany(desks, deskByRoom, db => db.JoinOne(occ))
			.Execute();
		Assert.That(outerInner.Select(x => x.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1, 2 }),
			"Room C (no desks) dropped; A keeps 2 desks, B keeps 1");
		var oiByRoom = outerInner.ToDictionary(x => x.Left.Id);
		Assert.That(oiByRoom[1].Right.Count, Is.EqualTo(2));
		Assert.That(oiByRoom[2].Right.Count, Is.EqualTo(1));

		// Composed: inner InnerJoinOne drops occupier-less desks (11, 20); the outer InnerJoinMany
		// then drops Room B (its only desk dropped) and Room C (no desks). Only Room A survives.
		var composed = rooms.Query()
			.InnerJoinMany(desks, deskByRoom, db => db.InnerJoinOne(occ))
			.Execute();
		Assert.That(composed.Select(x => x.Left.Id).OrderBy(x => x), Is.EqualTo(new[] { 1 }),
			"only Room A survives — desk 10 has an occupier");
		var cByRoom = composed.ToDictionary(x => x.Left.Id);
		Assert.That(cByRoom[1].Right.Count, Is.EqualTo(1));
		Assert.That(cByRoom[1].Right.ToList()[0].Left.Id, Is.EqualTo(10));
		Assert.That(cByRoom[1].Right.ToList()[0].Right!.Name, Is.EqualTo("Alice"));
	}

	[Test]
	public void NestedInnerJoinMany_ExecutePooled_MatchesExecute() {
		var expected = _rooms.Query()
			.InnerJoinMany(_desks, _deskByRoomId, db => db.JoinOne(_occupiers))
			.Execute();
		var expIds = expected.Select(x => x.Left.Id).OrderBy(x => x).ToArray();

		using var pooled = _rooms.Query()
			.InnerJoinMany(_desks, _deskByRoomId, db => db.JoinOne(_occupiers))
			.ExecutePooled();

		// Room 3 (no desks) is dropped → only rooms 1 and 2 remain.
		Assert.That(pooled.Count, Is.EqualTo(expected.Count));
		Assert.That(pooled.Select(x => x.Left.Id).OrderBy(x => x).ToArray(), Is.EqualTo(expIds));
		Assert.That(expIds, Is.EqualTo(new[] { 1, 2 }));
		foreach (var roomRow in pooled)
			Assert.That(roomRow.Right.Count, Is.GreaterThan(0), "inner-joined rooms are non-empty");
	}

	[Test]
	public void NestedJoinMany_Pooled_ReusesBuffers_NoLeak() {
		// Larger dataset so the partition + inner buffers dominate allocation. A leak (buffers
		// never returned) would force the pool to re-rent fresh each iteration, keeping pooled
		// allocation ~= execute; correct return/reuse keeps pooled ≪ execute.
		var rooms = new InMemoryDataCache<int, NRoom>();
		var desks = new InMemoryDataCache<int, NDesk>();
		var occ = new InMemoryDataCache<int, NOccupier>();
		var deskByRoom = desks.CacheKeyValueListIndex<int>((_, v) => v.RoomId);
		var deskId = 1;
		for (var r = 1; r <= 50; r++) {
			rooms.AddOrUpdate(r, new NRoom { Id = r, Name = "R" });
			for (var d = 0; d < 20; d++, deskId++) {
				desks.AddOrUpdate(deskId, new NDesk { Id = deskId, RoomId = r });
				occ.AddOrUpdate(deskId, new NOccupier { Id = deskId, Name = "O" });
			}
		}

		const int iterations = 100;
		var sink = 0L;
		for (var i = 0; i < 5; i++) {
			using var w = rooms.Query().JoinMany(desks, deskByRoom, db => db.JoinOne(occ)).ExecutePooled();
			sink += w.Count;
		}

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < iterations; i++) {
			using var r = rooms.Query().JoinMany(desks, deskByRoom, db => db.JoinOne(occ)).ExecutePooled();
			sink += r.Count;
		}
		var pooledAlloc = GC.GetAllocatedBytesForCurrentThread() - before;

		before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < iterations; i++) {
			var r = rooms.Query().JoinMany(desks, deskByRoom, db => db.JoinOne(occ)).Execute();
			sink += r.Count;
		}
		var executeAlloc = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(sink, Is.GreaterThan(0), "queries did not run");
		Assert.That(pooledAlloc, Is.LessThan(executeAlloc / 2),
			$"pooled nested join should reuse buffers (no leak): pooled={pooledAlloc} bytes, execute={executeAlloc} bytes over {iterations} iters");
	}
}
