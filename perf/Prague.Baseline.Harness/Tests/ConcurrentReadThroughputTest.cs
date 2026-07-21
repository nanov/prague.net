namespace Prague.Baseline.Harness.Tests;

using System;
using System.Threading;
using Prague.Baseline.Scenario;
using Prague.Core;

/// <summary>
///   Concurrent read-under-write throughput (Phase B): a pool of <see cref="ScenarioSpec.ReaderThreads" />
///   reader threads runs the heavy pooled multi-join (range scan -> join info -> join-many offers) in a
///   tight loop over a random range slice, while a single writer thread churns ProductInfo/Offer updates
///   at <see cref="ScenarioSpec.WriterUpdatesPerSecond" /> over a <see cref="ScenarioSpec.SteadyStateSeconds" />
///   steady-state window. Each reader accumulates its result-row count into a PER-THREAD long (no shared
///   counter in the hot loop); the summed rows / elapsed seconds drives the read.throughput metric.
/// </summary>
public sealed class ConcurrentReadThroughputTest : IThroughputTest {
	// 16 reader threads oversubscribe a ~4-core CI box by design; require only the 2 physical CPUs
	// (readers + writer) so ValidateTest never skips the contended scenario.
	public int RequiredProcessorCount => 2;

	private volatile bool _running;

	public long Run(ThroughputSessionContext ctx) {
		var data = DatasetFactory.Build();
		var registry = new DataCacheRegistryBuilder()
			.Register<BaselineProductCache>().Register<BaselineProductInfoCache>()
			.Register<BaselineOfferCache>().Build();
		var products = registry.GetCache<BaselineProductCache>();
		var infos = registry.GetCache<BaselineProductInfoCache>();
		var offers = registry.GetCache<BaselineOfferCache>();
		foreach (var p in data.Products) products.AddOrUpdate(p);
		foreach (var i in data.Infos) infos.AddOrUpdate(i);
		foreach (var o in data.Offers) offers.AddOrUpdate(o);

		var infoUpdates = BuildInfoUpdates(data.Infos);
		var offerUpdates = BuildOfferUpdates(data.Offers);

		var readerCount = ScenarioSpec.ReaderThreads;
		var perThreadRows = new long[readerCount];
		var readers = new Thread[readerCount];
		for (var t = 0; t < readerCount; t++) {
			var idx = t;
			readers[t] = new Thread(() => {
				var rng = new Random(ScenarioSpec.Seed + idx);
				while (!_running) Thread.SpinWait(1);
				long rows = 0;
				while (_running) {
					var pivot = rng.Next(1, ScenarioSpec.ProductCount + 1);
					using var r = products.Query()
						.WithRange(static (q, a) => q.Gte(a), pivot)
						.JoinWithBaselineProductInfo()
						.JoinMany(offers.Cache, offers.ProductIdIndex)
						.ExecutePooled();
					rows += r.Count;
				}

				perThreadRows[idx] = rows;
			}) { IsBackground = true, Name = "reader-" + idx };
		}

		var sleepMs = 1000 / ScenarioSpec.WriterUpdatesPerSecond;
		var writer = new Thread(() => {
			while (!_running) Thread.SpinWait(1);
			var i = 0;
			var o = 0;
			while (_running) {
				infos.AddOrUpdate(infoUpdates[i]);
				i = i + 1 == infoUpdates.Length ? 0 : i + 1;
				offers.AddOrUpdate(offerUpdates[o]);
				o = o + 1 == offerUpdates.Length ? 0 : o + 1;
				Thread.Sleep(sleepMs);
			}
		}) { IsBackground = true, Name = "writer" };

		for (var t = 0; t < readerCount; t++) readers[t].Start();
		writer.Start();

		ctx.Start();
		_running = true;
		Thread.Sleep(ScenarioSpec.SteadyStateSeconds * 1000);
		ctx.Stop();
		_running = false;

		for (var t = 0; t < readerCount; t++) readers[t].Join();
		writer.Join();

		long total = 0;
		for (var t = 0; t < perThreadRows.Length; t++) total += perThreadRows[t];
		return total;
	}

	// Pre-built update payloads so the writer never allocates inside the steady-state window: one
	// churned variant per info (StockCount + LastUpdated bumped).
	private static BaselineProductInfo[] BuildInfoUpdates(BaselineProductInfo[] source) {
		var updates = new BaselineProductInfo[source.Length];
		for (var i = 0; i < source.Length; i++) {
			var s = source[i];
			updates[i] = new BaselineProductInfo {
				Id = s.Id,
				ProductId = s.ProductId,
				Warehouse = s.Warehouse,
				StockCount = s.StockCount + 1,
				LastUpdated = s.LastUpdated + 1,
			};
		}

		return updates;
	}

	// One churned variant per offer (BasePrice + LastUpdated bumped).
	private static BaselineOffer[] BuildOfferUpdates(BaselineOffer[] source) {
		var updates = new BaselineOffer[source.Length];
		for (var i = 0; i < source.Length; i++) {
			var s = source[i];
			updates[i] = new BaselineOffer {
				Id = s.Id,
				ProductId = s.ProductId,
				IsActive = s.IsActive,
				BasePrice = s.BasePrice + 1,
				DisplayOrder = s.DisplayOrder,
				LastUpdated = s.LastUpdated + 1,
			};
		}

		return updates;
	}
}
