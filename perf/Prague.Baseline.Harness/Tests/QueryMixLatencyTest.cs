namespace Prague.Baseline.Harness.Tests;

using HdrHistogram;
using Prague.Baseline.Scenario;
using Prague.Baseline.Harness.Support;
using Prague.Core;

/// <summary>
///   Query latency: builds and populates the generated caches, then times the heaviest multi-join query
///   (range scan -> join info -> join-many offers) per-op, recording nanoseconds into the histogram.
/// </summary>
public sealed class QueryMixLatencyTest : ILatencyTest {
	public int RequiredProcessorCount => 1;

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

		const int iterations = 200_000;
		ctx.Start();
		for (var i = 0; i < iterations; i++) {
			var id = (i % ScenarioSpec.ProductCount) + 1;
			var t0 = StopwatchUtil.GetTimestamp();
			using var r = products.Query().WithRange(q => q.Gte(id))
				.JoinWithBaselineProductInfo()
				.JoinMany(offers.Cache, offers.ProductIdIndex)
				.ExecutePooled();
			_ = r.Count;
			var ns = StopwatchUtil.ToNanoseconds(StopwatchUtil.GetTimestamp() - t0);
			ctx.Histogram.RecordValue(ns);
		}
		ctx.Stop();
	}
}
