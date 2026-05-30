namespace Prague.Core.Tests.Join;

using Prague.Core;
using NUnit.Framework;

// Phase B-4 chained selector tests: LeftSym Shape A + key selector at chained levels.
// Shape A: CacheSymmetricKeyValueListIndex on left, no rightIndex — selector maps
// TLookupKey → TRightKey. At chained hops we use two distinct symmetric indexes on the
// outer cache. Selector maps long → int at both hops.

internal sealed class LsASelChainOrder : ICacheEquatable<LsASelChainOrder>, ICacheClonable<LsASelChainOrder> {
	public int Id { get; init; }
	public string? Code { get; init; }
	public long CustomerKey { get; init; }
	public long ProductKey { get; init; }
	public bool CacheEquals(LsASelChainOrder? o) => o is not null && o.Id == Id && o.Code == Code && o.CustomerKey == CustomerKey && o.ProductKey == ProductKey;
	public int CacheGetHashCode() => HashCode.Combine(Id, Code, CustomerKey, ProductKey);
	public LsASelChainOrder Clone() => new() { Id = Id, Code = Code, CustomerKey = CustomerKey, ProductKey = ProductKey };
}

internal sealed class LsASelChainCustomer : ICacheEquatable<LsASelChainCustomer>, ICacheClonable<LsASelChainCustomer> {
	public int Id { get; init; }
	public string? Name { get; init; }
	public bool CacheEquals(LsASelChainCustomer? o) => o is not null && o.Id == Id && o.Name == Name;
	public int CacheGetHashCode() => HashCode.Combine(Id, Name);
	public LsASelChainCustomer Clone() => new() { Id = Id, Name = Name };
}

internal sealed class LsASelChainProduct : ICacheEquatable<LsASelChainProduct>, ICacheClonable<LsASelChainProduct> {
	public int Id { get; init; }
	public string? Sku { get; init; }
	public int Price { get; init; }
	public bool CacheEquals(LsASelChainProduct? o) => o is not null && o.Id == Id && o.Sku == Sku && o.Price == Price;
	public int CacheGetHashCode() => HashCode.Combine(Id, Sku, Price);
	public LsASelChainProduct Clone() => new() { Id = Id, Sku = Sku, Price = Price };
}

[TestFixture]
public class JoinOneLeftSymShapeASelectorChainedCoreTests {
	private InMemoryDataCache<int, LsASelChainOrder> _orderCache = null!;
	private InMemoryDataCache<int, LsASelChainCustomer> _customerCache = null!;
	private InMemoryDataCache<int, LsASelChainProduct> _productCache = null!;
	private CacheSymmetricKeyValueListIndex<int, LsASelChainOrder, long> _orderByCustomerKeyIndex = null!;
	private CacheSymmetricKeyValueListIndex<int, LsASelChainOrder, long> _orderByProductKeyIndex = null!;

	[SetUp]
	public void SetUp() {
		_orderCache = new InMemoryDataCache<int, LsASelChainOrder>();
		_customerCache = new InMemoryDataCache<int, LsASelChainCustomer>();
		_productCache = new InMemoryDataCache<int, LsASelChainProduct>();
		_orderByCustomerKeyIndex = _orderCache.CacheSymmetricKeyValueListIndex<long>(static (_, v) => v.CustomerKey);
		_orderByProductKeyIndex = _orderCache.CacheSymmetricKeyValueListIndex<long>(static (_, v) => v.ProductKey);
	}

	private void SeedFullChain() {
		_orderCache.AddOrUpdate(1, new LsASelChainOrder { Id = 1, Code = "O1", CustomerKey = 101L, ProductKey = 201L });
		_orderCache.AddOrUpdate(2, new LsASelChainOrder { Id = 2, Code = "O2", CustomerKey = 102L, ProductKey = 202L });
		_orderCache.AddOrUpdate(3, new LsASelChainOrder { Id = 3, Code = "O3", CustomerKey = 103L, ProductKey = 203L });
		_customerCache.AddOrUpdate(101, new LsASelChainCustomer { Id = 101, Name = "Alice" });
		_customerCache.AddOrUpdate(102, new LsASelChainCustomer { Id = 102, Name = "Bob" });
		_customerCache.AddOrUpdate(103, new LsASelChainCustomer { Id = 103, Name = "Carol" });
		_productCache.AddOrUpdate(201, new LsASelChainProduct { Id = 201, Sku = "P1", Price = 10 });
		_productCache.AddOrUpdate(202, new LsASelChainProduct { Id = 202, Sku = "P2", Price = 20 });
		_productCache.AddOrUpdate(203, new LsASelChainProduct { Id = 203, Sku = "P3", Price = 30 });
	}

	[Test]
	public void Chained_S1Outer_AllMatched() {
		SeedFullChain();
		var results = _orderCache.Query()
			.JoinOne(_orderByCustomerKeyIndex, static idx => (int)idx, _customerCache)
			.JoinOne(_orderByProductKeyIndex, static idx => (int)idx, _productCache)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(3));
		var byId = results.ToDictionary(r => r.Left.Id);
		Assert.That(byId[1].Right!.Name, Is.EqualTo("Alice"));
		Assert.That(byId[1].Right2!.Sku, Is.EqualTo("P1"));
	}

	[Test]
	public void Chained_S1Inner_DropsUnmatchedAtSecondHop() {
		SeedFullChain();
		_productCache.Remove(202);
		var results = _orderCache.Query()
			.InnerJoinOne(_orderByCustomerKeyIndex, static idx => (int)idx, _customerCache)
			.InnerJoinOne(_orderByProductKeyIndex, static idx => (int)idx, _productCache)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
	}

	[Test]
	public void Chained_S3Inner_WithFilter_DropsPredicateReject() {
		SeedFullChain();
		var results = _orderCache.Query()
			.InnerJoinOne(_orderByCustomerKeyIndex, static idx => (int)idx, _customerCache)
			.InnerJoinOne(_orderByProductKeyIndex, static idx => (int)idx, _productCache,
				static q => q.Where(p => p.Price < 30))
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
		var ids = results.Select(r => r.Left.Id).OrderBy(x => x).ToList();
		Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void Chained_S5Inner_WithFilterArg_StaticLambdaZeroAlloc() {
		SeedFullChain();
		const int cutoff = 30;
		var results = _orderCache.Query()
			.InnerJoinOne(_orderByCustomerKeyIndex, static idx => (int)idx, _customerCache)
			.InnerJoinOne(_orderByProductKeyIndex, static idx => (int)idx, _productCache,
				static (q, c) => q.Where(p => p.Price < c),
				cutoff)
			.Execute();
		Assert.That(results.Count, Is.EqualTo(2));
	}
}
