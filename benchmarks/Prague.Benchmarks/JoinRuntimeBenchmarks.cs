namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Core;
using Models;

/// <summary>
///   Cross-runtime (net9.0 + net10.0) single-query-per-op benchmark contrasting the non-join path
///   with the heavy 3-level join (Product → ProductInfo (1:1) → Offers (1:N)), each via the
///   allocating <c>Execute()</c> and the pooled <c>ExecutePooled()</c> path. Runs out-of-process so
///   BenchmarkDotNet builds and executes on both runtimes; [MemoryDiagnoser] surfaces the GC/Allocated
///   difference and the Config adds an operations/sec column.
///
///   The non-join path has no per-left child buffer, so pooled ≈ allocating there; the heavy join is
///   where pooling collapses allocation (see PR #8 — pooled child buffers are now returned).
///
///   Run with: dotnet run -c Release --project benchmarks/Prague.Benchmarks -- --filter "*JoinRuntimeBenchmarks*"
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[HideColumns(Column.Error, Column.StdDev)]
[Config(typeof(Config))]
public class JoinRuntimeBenchmarks {
	private const int ProductCount = 500;
	private const int OffersPerProduct = 20;
	private BenchmarkProductCache _productCache = null!;
	private BenchmarkProductInfoCache _productInfoCache = null!;
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
		var random = new Random(42); // Fixed seed for reproducibility

		for (var i = 1; i <= ProductCount; i++) {
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
					IsActive = random.Next(10) > 1,
					IsSuspended = random.Next(20) == 0,
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

	/// <summary>Baseline: plain query, no joins — allocating Execute().</summary>
	[Benchmark(Baseline = true, Description = "Non-join — Execute() (allocating)")]
	public int NonJoin_Execute() {
		var results = _productCache.Query().Execute();
		return results.Count;
	}

	/// <summary>Plain query, no joins — pooled. No child buffer, so ≈ the allocating path.</summary>
	[Benchmark(Description = "Non-join — ExecutePooled() (pooled, disposed)")]
	public int NonJoin_ExecutePooled() {
		var results = _productCache.Query().ExecutePooled();
		var count = results.Count;
		results.Dispose();
		return count;
	}

	/// <summary>Heavy 3-level join — allocating Execute().</summary>
	[Benchmark(Description = "Heavy join — Execute() (allocating)")]
	public int HeavyJoin_Execute() {
		var results = _productCache.Query()
			.JoinWithBenchmarkProductInfo()
			.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
			.Execute();
		return results.Count;
	}

	/// <summary>Heavy 3-level join — pooled; Dispose() returns the outer + child (Offers) buffers.</summary>
	[Benchmark(Description = "Heavy join — ExecutePooled() (pooled, disposed)")]
	public int HeavyJoin_ExecutePooled() {
		var results = _productCache.Query()
			.JoinWithBenchmarkProductInfo()
			.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
			.ExecutePooled();
		var count = results.Count;
		results.Dispose();
		return count;
	}

	private class Config : ManualConfig {
		public Config() {
			// BenchmarkDotNet 0.15.8 has no built-in .NET 10 runtime moniker, so drive each TFM via the
			// CsProj toolchain directly — it builds + runs the benchmark out-of-process per framework.
			AddJob(Job.Default
				.WithToolchain(CsProjCoreToolchain.From(new NetCoreAppSettings("net9.0", null, ".NET 9.0")))
				.WithId("net9.0"));
			AddJob(Job.Default
				.WithToolchain(CsProjCoreToolchain.From(new NetCoreAppSettings("net10.0", null, ".NET 10.0")))
				.WithId("net10.0"));
			AddColumn(StatisticColumn.OperationsPerSecond);
		}
	}
}
