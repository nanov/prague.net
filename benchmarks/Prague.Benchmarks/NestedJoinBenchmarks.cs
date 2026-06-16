namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Core;

/// <summary>
///   Nested-join benchmark. Contrasts the flat (non-nested) JoinMany — the "if not used" path,
///   to show nesting adds no tax there — against the true nested join
///   (Room → JoinMany(Desk) → each Desk JoinOne(Occupier)) via both the allocating <c>Execute()</c>
///   and the pooled <c>ExecutePooled()</c> path. [MemoryDiagnoser] surfaces the real ns/bytes:
///   the nested pooled path reuses its partition + inner buffers (bounded, not zero), while
///   nested Execute allocates them fresh.
///
///   Run: dotnet run -c Release --project benchmarks/Prague.Benchmarks -- --filter "*NestedJoinBenchmarks*"
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class NestedJoinBenchmarks {
	private const int RoomCount = 200;
	private const int DesksPerRoom = 16;

	private InMemoryDataCache<int, BmRoom> _rooms = null!;
	private InMemoryDataCache<int, BmDesk> _desks = null!;
	private InMemoryDataCache<int, BmOccupier> _occupiers = null!;
	private CacheKeyValueListIndex<int, BmDesk, int> _deskByRoom = null!;

	[GlobalSetup]
	public void Setup() {
		_rooms = new InMemoryDataCache<int, BmRoom>();
		_desks = new InMemoryDataCache<int, BmDesk>();
		_occupiers = new InMemoryDataCache<int, BmOccupier>();
		_deskByRoom = _desks.CacheKeyValueListIndex<int>((_, v) => v.RoomId);

		var deskId = 1;
		for (var r = 1; r <= RoomCount; r++) {
			_rooms.AddOrUpdate(r, new BmRoom { Id = r, Name = "R" });
			for (var d = 0; d < DesksPerRoom; d++, deskId++) {
				_desks.AddOrUpdate(deskId, new BmDesk { Id = deskId, RoomId = r });
				if (deskId % 2 == 0) // half the desks are occupied
					_occupiers.AddOrUpdate(deskId, new BmOccupier { Id = deskId, Name = "O" });
			}
		}
	}

	// Baseline — flat JoinMany (the "if not used" path): Room → QueryResults<Desk>.
	[Benchmark(Baseline = true)]
	public int FlatJoinMany_Execute() {
		var r = _rooms.Query().JoinMany(_desks, _deskByRoom).Execute();
		return r.Count;
	}

	// Nested — Room → JoinMany(Desk) → each Desk JoinOne(Occupier), allocating.
	[Benchmark]
	public int NestedJoin_Execute() {
		var r = _rooms.Query().JoinMany(_desks, _deskByRoom, db => db.JoinOne(_occupiers)).Execute();
		return r.Count;
	}

	// Nested — pooled: partition + inner buffers reused across calls.
	[Benchmark]
	public int NestedJoin_Pooled() {
		using var r = _rooms.Query().JoinMany(_desks, _deskByRoom, db => db.JoinOne(_occupiers)).ExecutePooled();
		return r.Count;
	}

	internal sealed class BmRoom : ICacheEquatable<BmRoom>, ICacheClonable<BmRoom> {
		public int Id { get; init; }
		public string Name { get; init; } = "";
		public bool CacheEquals(BmRoom? other) => other is not null && other.Id == Id && other.Name == Name;
		public int CacheGetHashCode() => HashCode.Combine(Id, Name);
		public BmRoom Clone() => new() { Id = Id, Name = Name };
	}

	internal sealed class BmDesk : ICacheEquatable<BmDesk>, ICacheClonable<BmDesk> {
		public int Id { get; init; }
		public int RoomId { get; init; }
		public bool CacheEquals(BmDesk? other) => other is not null && other.Id == Id && other.RoomId == RoomId;
		public int CacheGetHashCode() => HashCode.Combine(Id, RoomId);
		public BmDesk Clone() => new() { Id = Id, RoomId = RoomId };
	}

	internal sealed class BmOccupier : ICacheEquatable<BmOccupier>, ICacheClonable<BmOccupier> {
		public int Id { get; init; }
		public string Name { get; init; } = "";
		public bool CacheEquals(BmOccupier? other) => other is not null && other.Id == Id && other.Name == Name;
		public int CacheGetHashCode() => HashCode.Combine(Id, Name);
		public BmOccupier Clone() => new() { Id = Id, Name = Name };
	}
}
