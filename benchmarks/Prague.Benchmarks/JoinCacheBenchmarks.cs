namespace Prague.Benchmarks;
#if false
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Core;
using Models;

/// <summary>
///   Benchmarks for concurrent read/write operations on joined caches.
///   Scenario:
///   - 500 products with their infos and ~20 offers per product (10,000 offers total)
///   - 1 writer thread updating ProductInfo and Offers every 500ms
///   - Multiple reader threads constantly querying with joins
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[HideColumns(Column.Error, Column.StdDev)]
[Config(typeof(Config))]
public class JoinCacheBenchmarks {
	private const int ProductCount = 500;
	private const int OffersPerProduct = 20;
	private const int TotalOffers = ProductCount * OffersPerProduct;
	private BenchmarkProductCache _productCache = null!;
	private BenchmarkProductInfoCache _productInfoCache = null!;
	private BenchmarkOfferCache _offerCache = null!;

	private DataCacheRegistry _registry = null!;

	[Params(16)] public int ReaderThreads { get; set; }

	[GlobalSetup]
	public void Setup() {
		_registry = new DataCacheRegistryBuilder()
			.Register<BenchmarkProductCache>()
			.Register<BenchmarkProductInfoCache>()
			.Register<BenchmarkOfferCache>()
			.Build();
		_productCache = _registry.GetCache<BenchmarkProductCache>();
		_productInfoCache = _registry.GetCache<BenchmarkProductInfoCache>();
		_offerCache = _registry.GetCache<BenchmarkOfferCache>();

		// Populate initial data
		PopulateData();
	}

	private void PopulateData() {
		var random = new Random(42); // Fixed seed for reproducibility

		for (var i = 1; i <= ProductCount; i++) {
			// Add product
			_productCache.AddOrUpdate(new BenchmarkProduct {
				Id = i,
				Manufacturer = $"Manufacturer_{i}",
				Supplier = $"Supplier_{i}",
				Brand = $"Brand_{i % 20}",
				Category = $"Category_{i % 10}",
				StartTime = DateTime.UtcNow.AddHours(random.Next(-24, 24)),
				Status = i % 3 == 0 ? "Active" : i % 3 == 1 ? "Scheduled" : "Archived",
				PrimaryValue = random.Next(0, 5),
				SecondaryValue = random.Next(0, 5),
				IsPublished = i % 3 == 0,
				DepartmentType = "Electronics",
				Priority = random.Next(1, 100),
				LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});

			// Add product info (one-to-one)
			_productInfoCache.AddOrUpdate(new BenchmarkProductInfo {
				Id = i,
				ProductId = i,
				Warehouse = $"Warehouse_{i}",
				Inspector = $"Inspector_{i % 50}",
				StockCount = random.Next(10000, 80000),
				Packaging = random.Next(2) == 0 ? "Boxed" : "Sealed",
				Dimensions = $"{random.Next(10, 30)}cm",
				ShippingCarrier = $"Carrier_{i % 10}",
				LeadTimeDays = random.Next(0, 90),
				Section = i % 2 == 0 ? "Front" : "Back",
				PrimaryColor = "Black",
				SecondaryColor = "White",
				PrimaryWeight = random.Next(0, 10),
				SecondaryWeight = random.Next(0, 10),
				PrimaryWarrantyMonths = random.Next(0, 4),
				SecondaryWarrantyMonths = random.Next(0, 4),
				PrimaryReturns = random.Next(0, 2),
				SecondaryReturns = random.Next(0, 2),
				LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});

			// Add offers (one-to-many, ~20 per product)
			for (var m = 1; m <= OffersPerProduct; m++) {
				var offerId = (i - 1) * OffersPerProduct + m;
				_offerCache.AddOrUpdate(new BenchmarkOffer {
					Id = offerId,
					ProductId = i,
					OfferName = $"Offer_{m}",
					OfferType = m switch {
						1 => "Standard",
						2 => "Bundle",
						3 => "Subscription",
						4 => "Clearance",
						_ => $"Special_{m}"
					},
					BasePrice = 1.5m + (decimal)(random.NextDouble() * 2),
					ListPrice = 2.5m + (decimal)(random.NextDouble() * 2),
					SalePrice = 1.8m + (decimal)(random.NextDouble() * 3),
					Tier = m % 5 == 0 ? 2.5m : 0m,
					IsActive = random.Next(10) > 1, // 90% active
					IsSuspended = random.Next(20) == 0, // 5% suspended
					DisplayOrder = m,
					Category = m <= 5 ? "Main" : "Secondary",
					OpenTime = DateTime.UtcNow.AddDays(-1),
					CloseTime = null,
					MaxQuantity = 10000m,
					MinQuantity = 1m,
					LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
				});
			}
		}
	}

	/// <summary>
	///   Benchmark: Simple read of all products (no joins).
	/// </summary>
	[Benchmark(Baseline = true)]
	public int ReadAllProducts() {
		var results = _productCache.Query().Execute();
		return results.Count;
	}

	/// <summary>
	///   Benchmark: Read products with ProductInfo join (one-to-one).
	/// </summary>
	[Benchmark]
	public int ReadProductsWithInfo() {
		var results = _productCache.Query()
			.JoinWithBenchmarkProductInfo()
			.Execute();
		return results.Count;
	}

	/// <summary>
	///   Benchmark: Read products with Offers join (one-to-many).
	/// </summary>
	[Benchmark]
	public int ReadProductsWithOffers() {
		var results = _productCache.Query()
			.JoinWithBenchmarkOffer()
			.Execute();
		return results.Count;
	}

	/// <summary>
	///   Benchmark: Read products with both ProductInfo and Offers joins.
	/// </summary>
	[Benchmark]
	public int ReadProductsWithInfoAndOffers() {
		var results = _productCache.Query()
			.JoinWithBenchmarkProductInfo()
			.Builder.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
			.Execute();
		return results.Count;
	}

	/// <summary>
	///   Benchmark: Read products with filter and joins.
	/// </summary>
	[Benchmark]
	public int ReadPublishedProductsWithOffers() {
		var results = _productCache.Query()
			.Where(g => g.IsPublished)
			.JoinWithBenchmarkProductInfo()
			.Builder.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex, q => q.Where(m => m.IsActive))
			.Execute();
		return results.Count;
	}

	/// <summary>
	///   Benchmark: Concurrent reads with joins.
	/// </summary>
	[Benchmark]
	public int ConcurrentReadsWithJoins() {
		var totalReads = 0;
		var tasks = new Task[ReaderThreads];

		for (var t = 0; t < ReaderThreads; t++)
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < 100; i++) {
					var results = _productCache.Query()
						.JoinWithBenchmarkProductInfo()
						.Builder.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
						.Execute();
					Interlocked.Add(ref totalReads, results.Count);
				}
			});

		Task.WaitAll(tasks);
		return totalReads;
	}

	/// <summary>
	///   Benchmark: Concurrent reads while writer is updating.
	///   Simulates real-world scenario where data is being updated while reads occur.
	/// </summary>
	[Benchmark]
	public int ConcurrentReadsWithWriter() {
		var totalReads = 0;
		var writerRunning = true;
		var random = new Random(42);

		// Writer task - updates ProductInfo and Offers every ~10ms (faster for benchmark)
		var writerTask = Task.Run(() => {
			var updateCount = 0;
			while (writerRunning && updateCount < 50) {
				// Limit updates for benchmark
				// Update random ProductInfo
				var productId = random.Next(1, ProductCount + 1);
				if (_productInfoCache.TryGet(productId, out var existingInfo)) {
					existingInfo.LeadTimeDays = random.Next(0, 90);
					existingInfo.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					_productInfoCache.AddOrUpdate(existingInfo);
				}

				// Update random Offers
				for (var i = 0; i < 5; i++) {
					var offerId = random.Next(1, TotalOffers + 1);
					if (_offerCache.TryGet(offerId, out var existingOffer)) {
						existingOffer.BasePrice = 1.5m + (decimal)(random.NextDouble() * 2);
						existingOffer.ListPrice = 2.5m + (decimal)(random.NextDouble() * 2);
						existingOffer.SalePrice = 1.8m + (decimal)(random.NextDouble() * 3);
						existingOffer.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
						_offerCache.AddOrUpdate(existingOffer);
					}
				}

				updateCount++;
				Thread.Sleep(10);
			}
		});

		// Reader tasks
		var readerTasks = new Task[ReaderThreads];
		for (var t = 0; t < ReaderThreads; t++)
			readerTasks[t] = Task.Run(() => {
				for (var i = 0; i < 100; i++) {
					var results = _productCache.Query()
						.JoinWithBenchmarkProductInfo()
						.Builder.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
						.Execute();
					Interlocked.Add(ref totalReads, results.Count);
				}
			});

		Task.WaitAll(readerTasks);
		writerRunning = false;
		writerTask.Wait();

		return totalReads;
	}

	/// <summary>
	///   Benchmark: Single product lookup with joins (by key).
	/// </summary>
	[Benchmark]
	public int SingleProductLookupWithJoins() {
		var count = 0;
		for (var i = 1; i <= 100; i++) {
			var productId = i % ProductCount + 1;
			var results = _productCache.Query()
				.Where(g => g.Id == productId)
				.JoinWithBenchmarkProductInfo()
				.Builder.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
				.Execute();
			count += results.Count;
		}

		return count;
	}

	private class Config : ManualConfig {
		public Config() {
			AddColumn(StatisticColumn.OperationsPerSecond);
		}
	}
}

#endif
