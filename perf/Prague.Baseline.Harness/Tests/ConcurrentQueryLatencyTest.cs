namespace Prague.Baseline.Harness.Tests;

using System;
using System.Threading;
using HdrHistogram;
using Prague.Baseline.Scenario;
using Prague.Baseline.Harness.Support;
using Prague.Core;

/// <summary>
///   Contended query latency (Phase B): identical read-under-write setup to
///   <see cref="ConcurrentReadThroughputTest" />, but each reader times every heavy multi-join
///   (<see cref="StopwatchUtil" /> ticks -> nanoseconds) into its OWN <see cref="LongHistogram" /> —
///   HdrHistogram is not safe for concurrent <c>RecordValue</c>. After the window closes and all
///   threads join, every per-thread histogram is merged into <c>ctx.Histogram</c> via
///   <see cref="LongHistogram.Add" />, yielding contended p50/p99/p999.
/// </summary>
public sealed class ConcurrentQueryLatencyTest : ILatencyTest {
	// See ConcurrentReadThroughputTest: 16 readers oversubscribe by design; require only 2 physical CPUs.
	public int RequiredProcessorCount => 2;

	private volatile bool _running;

	public void Run(LatencySessionContext ctx) {
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
		var perThreadHistograms = new LongHistogram[readerCount];
		var readers = new Thread[readerCount];
		for (var t = 0; t < readerCount; t++) {
			var idx = t;
			var histogram = new LongHistogram(10_000_000_000L, 4);
			perThreadHistograms[t] = histogram;
			readers[t] = new Thread(() => {
				var rng = new Random(ScenarioSpec.Seed + idx);
				while (!_running) Thread.SpinWait(1);
				while (_running) {
					var pivot = rng.Next(1, ScenarioSpec.ProductCount + 1);
					var t0 = StopwatchUtil.GetTimestamp();
					using var r = products.Query()
						.WithRange(static (q, a) => q.Gte(a), pivot)
						.JoinWithBaselineProductInfo()
						.JoinMany(offers.Cache, offers.ProductIdIndex)
						.ExecutePooled();
					_ = r.Count;
					var ns = StopwatchUtil.ToNanoseconds(StopwatchUtil.GetTimestamp() - t0);
					histogram.RecordValue(ns);
				}
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

		for (var t = 0; t < perThreadHistograms.Length; t++)
			ctx.Histogram.Add(perThreadHistograms[t]);
	}

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
