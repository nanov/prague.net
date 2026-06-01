namespace Prague.Benchmarks;

using System.Collections.Concurrent;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Core;
using Models;

/// <summary>
///   Real-world concurrent-load benchmark for the heavy 3-level join
///   (Product → ProductInfo (1:1) → Offers (1:N)). A pool of reader threads runs the
///   heavy join in a tight loop over a random range slice, while writer threads
///   continuously update ProductInfo and Offers — exactly the rent/return churn the
///   pooled path is designed for. Two variants are measured: the allocating
///   <c>Execute()</c> path vs. the pooled <c>ExecutePooled()</c> + <c>Dispose()</c> path.
///   [MemoryDiagnoser] surfaces the Gen0/1/2 + Allocated difference; the custom columns
///   report query throughput (queries + queries/sec), total writes and the measured window.
///
///   Run with: dotnet run -c Release -- --filter "*HeavyJoinPooled*"
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[HideColumns(Column.Error, Column.StdDev)]
[Config(typeof(Config))]
public class HeavyJoinPooledBenchmarks {
	private const int ProductCount = 500;
	private const int OffersPerProduct = 20;
	private const int WriterThreads = 2;
	public static readonly ConcurrentDictionary<string, (long Queries, long Writes, long DurationMs)> Results = new();
	private BenchmarkProductCache _productCache = null!;
	private BenchmarkProductInfoCache _productInfoCache = null!;
	private BenchmarkOfferCache _offerCache = null!;
	private BenchmarkProductInfo[] _productInfoUpdates = null!;
	private BenchmarkOffer[] _productOfferUpdates = null!;

	private DataCacheRegistry _registry = null!;

	[Params(10)] public int ReaderThreads { get; set; }

	[Params(3000)] public int DurationMs { get; set; }

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
		var writerRandom = new Random(123);

		// Pre-built update payloads so the writer loop never allocates while measuring.
		_productInfoUpdates = new BenchmarkProductInfo[ProductCount * 5];
		_productOfferUpdates = new BenchmarkOffer[ProductCount * OffersPerProduct * 5];
		var ui = 0;
		var mi = 0;

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
				var infoUpdate = info.Clone();
				infoUpdate.LeadTimeDays = writerRandom.Next(0, 90);
				infoUpdate.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				_productInfoUpdates[ui++] = infoUpdate;
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
					IsActive = random.Next(10) > 1, // 90% active
					IsSuspended = random.Next(20) == 0, // 5% suspended
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
					var offerUpdate = offer.Clone();
					offerUpdate.BasePrice = 1.5m + (decimal)(writerRandom.NextDouble() * 2);
					offerUpdate.ListPrice = 2.5m + (decimal)(writerRandom.NextDouble() * 2);
					offerUpdate.SalePrice = 1.8m + (decimal)(writerRandom.NextDouble() * 3);
					offerUpdate.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					_productOfferUpdates[mi++] = offerUpdate;
				}
			}
		}
	}

	/// <summary>
	///   Heavy join under concurrent writers, allocating Execute() path.
	/// </summary>
	[Benchmark(Baseline = true, Description = "Concurrent heavy join — Execute() (allocating)")]
	public string ConcurrentHeavyJoin_Execute() {
		return Run(nameof(ConcurrentHeavyJoin_Execute), pooled: false);
	}

	/// <summary>
	///   Heavy join under concurrent writers, pooled ExecutePooled() + Dispose() path.
	/// </summary>
	[Benchmark(Description = "Concurrent heavy join — ExecutePooled() (pooled, disposed)")]
	public string ConcurrentHeavyJoin_Pooled() {
		return Run(nameof(ConcurrentHeavyJoin_Pooled), pooled: true);
	}

	private string Run(string key, bool pooled) {
		var running = true;
		var writes = new long[WriterThreads];
		var queries = new long[ReaderThreads];
		var stopwatch = Stopwatch.StartNew();

		var writerTasks = new Task[WriterThreads];
		for (var w = 0; w < WriterThreads; w++) {
			var wi = w;
			writerTasks[w] = Task.Run(() => {
				var writerRandom = new Random(123 + wi);
				while (running) {
					_productInfoCache.AddOrUpdate(_productInfoUpdates[writerRandom.Next(0, _productInfoUpdates.Length)]);
					writes[wi]++;
					for (var i = 0; i < 5; i++) {
						_offerCache.AddOrUpdate(_productOfferUpdates[writerRandom.Next(0, _productOfferUpdates.Length)]);
						writes[wi]++;
					}

					Thread.Sleep(1);
				}
			});
		}

		var readerTasks = new Task[ReaderThreads];
		for (var t = 0; t < ReaderThreads; t++) {
			var r = t;
			readerTasks[t] = Task.Run(() => {
				var readerRandom = new Random(123 + r);
				if (pooled)
					while (running) {
						var pivot = readerRandom.Next(1, ProductCount + 1);
						var results = _productCache.Query()
							.WithRange(static (q, a) => q.Gte(a), pivot)
							.JoinWithBenchmarkProductInfo()
							.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
							.ExecutePooled();
						queries[r]++;
						results.Dispose();
					}
				else
					while (running) {
						var pivot = readerRandom.Next(1, ProductCount + 1);
						var results = _productCache.Query()
							.WithRange(static (q, a) => q.Gte(a), pivot)
							.JoinWithBenchmarkProductInfo()
							.JoinMany(_offerCache.Cache, _offerCache.ProductIdIndex)
							.Execute();
						if (results.Count >= 0)
							queries[r]++;
					}
			});
		}

		Thread.Sleep(DurationMs);
		running = false;

		Task.WaitAll(readerTasks);
		Task.WaitAll(writerTasks);
		stopwatch.Stop();

		long totalQueries = 0;
		for (var i = 0; i < queries.Length; i++)
			totalQueries += queries[i];
		long totalWrites = 0;
		for (var i = 0; i < writes.Length; i++)
			totalWrites += writes[i];

		Results[key] = (totalQueries, totalWrites, stopwatch.ElapsedMilliseconds);
		return $"Q:{totalQueries} W:{totalWrites} T:{stopwatch.ElapsedMilliseconds}ms";
	}

	private class Config : ManualConfig {
		public Config() {
			AddJob(Job.Default
				.WithToolchain(InProcessNoEmitToolchain.Instance)
				.WithWarmupCount(1)
				.WithIterationCount(5));
			AddColumn(new ThroughputColumn("Queries", 0, "Total heavy-join queries executed",
				static r => r.Queries.ToString("N0")));
			AddColumn(new ThroughputColumn("Queries/s", 1, "Heavy-join queries per second",
				static r => r.DurationMs > 0 ? (r.Queries * 1000.0 / r.DurationMs).ToString("N0") : "N/A"));
			AddColumn(new ThroughputColumn("Writes", 2, "Total write operations",
				static r => r.Writes.ToString("N0")));
			AddColumn(new ThroughputColumn("Window", 3, "Measured window in ms",
				static r => $"{r.DurationMs} ms"));
		}
	}

	private sealed class ThroughputColumn : IColumn {
		private readonly Func<(long Queries, long Writes, long DurationMs), string> _format;

		public ThroughputColumn(string name, int priority, string legend,
			Func<(long Queries, long Writes, long DurationMs), string> format) {
			ColumnName = name;
			PriorityInCategory = priority;
			Legend = legend;
			_format = format;
		}

		public string Id => ColumnName;
		public string ColumnName { get; }
		public bool AlwaysShow => true;
		public ColumnCategory Category => ColumnCategory.Custom;
		public int PriorityInCategory { get; }
		public bool IsNumeric => true;
		public UnitType UnitType => UnitType.Dimensionless;
		public string Legend { get; }

		public string GetValue(Summary summary, BenchmarkCase benchmarkCase) {
			var key = benchmarkCase.Descriptor.WorkloadMethod.Name;
			return Results.TryGetValue(key, out var result) ? _format(result) : "N/A";
		}

		public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
			GetValue(summary, benchmarkCase);

		public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
		public bool IsAvailable(Summary summary) => true;
	}
}
