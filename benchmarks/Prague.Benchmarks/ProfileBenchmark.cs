namespace Prague.Benchmarks;

#if false
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnostics.dotMemory;
using Core;
using Models;

/// <summary>
///   Benchmark for profiling with dotMemory.
///   Run with: dotnet run -c Release -- --filter "*ProfileBenchmark*"
///   Open the .dmw file from artifacts folder in dotMemory or Rider.
/// </summary>
[MemoryDiagnoser]
[DotMemoryDiagnoser]
[SimpleJob(warmupCount: 0, iterationCount: 1)]
public class ProfileBenchmark {
	private const int ProductCount = 500;
	private const int OffersPerProduct = 20;
	private const int TotalOffers = ProductCount * OffersPerProduct;
	private BenchmarkProductCache _productCache = null!;
	private BenchmarkProductInfoCache _productInfoCache = null!;
	private BenchmarkProductInfo[] _productInfoUpdates = null!;
	private BenchmarkOffer[] _productOfferUpdates = null!;
	private BenchmarkOfferCache _offerCache = null!;

	private DataCacheRegistry _registry = null!;

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
		PopulateData();
	}

	private void PopulateData() {
		var random = new Random(42);
		_productOfferUpdates = new BenchmarkOffer[ProductCount * OffersPerProduct * 5];
		var mi = 0;
		_productInfoUpdates = new BenchmarkProductInfo[ProductCount * 5];
		var ui = 0;

		var writerRandom = new Random(123);
		for (var i = 1; i <= ProductCount; i++) {
			_productCache.AddOrUpdate(new BenchmarkProduct {
				Id = i,
				Range = i,
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

			var info = new BenchmarkProductInfo {
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
			};

			_productInfoCache.AddOrUpdate(info);

			for (var x = 0; x < 5; x++) {
				var productInfo = info.Clone();
				productInfo.LeadTimeDays = writerRandom.Next(0, 90);
				productInfo.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				_productInfoUpdates[ui++] = productInfo;
			}

			for (var m = 1; m <= OffersPerProduct; m++) {
				var offerId = (i - 1) * OffersPerProduct + m;
				var offer = new BenchmarkOffer {
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
					IsActive = random.Next(10) > 1,
					IsSuspended = random.Next(20) == 0,
					DisplayOrder = m,
					Category = m <= 5 ? "Main" : "Secondary",
					OpenTime = DateTime.UtcNow.AddDays(-1),
					CloseTime = null,
					MaxQuantity = 10000m,
					MinQuantity = 1m,
					LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
				};

				_offerCache.AddOrUpdate(offer);

				for (var x = 0; x < 5; x++) {
					var offerInfo = offer.Clone();
					offerInfo.ProductId = writerRandom.Next(1, ProductCount);
					offerInfo.BasePrice = 1.5m + (decimal)(writerRandom.NextDouble() * 2);
					offerInfo.ListPrice = 2.5m + (decimal)(writerRandom.NextDouble() * 2);
					offerInfo.SalePrice = 1.8m + (decimal)(writerRandom.NextDouble() * 3);
					offerInfo.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					_productOfferUpdates[mi++] = offerInfo;
				}
			}
		}
	}

	public int ConcurrentReadsWithWriter_Pooled(int seconds) {
		var running = true;

		var writerTaskProductInfo = Task.Run(() => {
			var writerRandom = new Random(123);
			while (running) {
				var infoUpdate = _productInfoUpdates[writerRandom.Next(0, _productInfoUpdates.Length)];
				_productInfoCache.AddOrUpdate(infoUpdate);
				for (var i = 0; i < 5; i++) {
					var offerUpdate = _productOfferUpdates[writerRandom.Next(0, _productOfferUpdates.Length)];
					_offerCache.AddOrUpdate(offerUpdate);
				}

				Thread.Sleep(10);
			}
		});

		var writerTaskOffers = Task.Run(() => {
			var writerRandom = new Random(123);
			while (running) {
				for (var i = 0; i < 5; i++) {
					var offerUpdate = _productOfferUpdates[writerRandom.Next(0, _productOfferUpdates.Length)];
					_offerCache.AddOrUpdate(offerUpdate);
					Thread.Sleep(10);
				}

				Thread.Sleep(10);
			}
		});

		var readerTasks = new Task[Environment.ProcessorCount - 2];
		var sums = new int[readerTasks.Length];
		for (var t = 0; t < readerTasks.Length; t++) {
			var r = t;
			readerTasks[t] = Task.Run(() => {
				var writerRandom = new Random(123);
				while (running) {
					var results = _productCache
						.Query()
						.WithRange(q => q.Gte(writerRandom.Next(1, ProductCount + 1)))
						.JoinWithBenchmarkProductInfo()
						.Builder.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
						.ExecutePooled();
					sums[r] += results.Count;
					results.Dispose();
				}
			});
		}

		Thread.Sleep(seconds * 1000);
		running = false;
		Task.WaitAll(readerTasks);
		Task.WaitAll(writerTaskProductInfo, writerTaskOffers);

		return sums.Sum();
	}

	public int ConcurrentReadsWithWriter_NotPooled(int seconds) {
		var running = true;

		var writerTaskProductInfo = Task.Run(() => {
			var writerRandom = new Random(123);
			while (running) {
				var infoUpdate = _productInfoUpdates[writerRandom.Next(0, _productInfoUpdates.Length)];
				_productInfoCache.AddOrUpdate(infoUpdate);
				for (var i = 0; i < 5; i++) {
					var offerUpdate = _productOfferUpdates[writerRandom.Next(0, _productOfferUpdates.Length)];
					_offerCache.AddOrUpdate(offerUpdate);
				}

				Thread.Sleep(10);
			}
		});

		var writerTaskOffers = Task.Run(() => {
			var writerRandom = new Random(123);
			while (running) {
				for (var i = 0; i < 5; i++) {
					var offerUpdate = _productOfferUpdates[writerRandom.Next(0, _productOfferUpdates.Length)];
					_offerCache.AddOrUpdate(offerUpdate);
					Thread.Sleep(10);
				}

				Thread.Sleep(10);
			}
		});


		var readerTasks = new Task[Environment.ProcessorCount - 2];
		var sums = new int[readerTasks.Length];
		for (var t = 0; t < readerTasks.Length; t++) {
			var r = t;
			readerTasks[t] = Task.Run(() => {
				var writerRandom = new Random(123);
				while (running) {
					var results = _productCache
						.Query()
						.WithRange(q => q.Gte(writerRandom.Next(1, ProductCount + 1)))
						.JoinWithBenchmarkProductInfo()
						.Builder.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
						.Execute();
					sums[r] += results.Count;
				}
			});
		}

		Thread.Sleep(seconds * 1000);
		running = false;
		Task.WaitAll(readerTasks);
		Task.WaitAll(writerTaskProductInfo, writerTaskOffers);

		return sums.Sum();
	}
}
#endif
