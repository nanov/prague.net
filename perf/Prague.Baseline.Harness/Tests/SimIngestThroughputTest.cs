namespace Prague.Baseline.Harness.Tests;

using Prague.Baseline.Harness.Sim;
using Prague.Baseline.Scenario;
using Prague.Core;

/// <summary>
///   Full-sim ingest throughput: replays the pre-encoded dataset through the in-process managed Kafka
///   ingest tail (<see cref="SimIngestPipeline" />) into fresh generated caches, timing only the drive loop.
/// </summary>
public sealed class SimIngestThroughputTest : IThroughputTest {
	public int RequiredProcessorCount => 2;

	public long Run(ThroughputSessionContext ctx) {
		var enc = Payloads.Encode(DatasetFactory.Build());
		var registry = new DataCacheRegistryBuilder()
			.Register<BaselineProductCache>().Register<BaselineProductInfoCache>()
			.Register<BaselineOfferCache>().Build();
		var products = registry.GetCache<BaselineProductCache>();
		var infos = registry.GetCache<BaselineProductInfoCache>();
		var offers = registry.GetCache<BaselineOfferCache>();

		ctx.Start();
		var ops = SimIngestPipeline.IngestAll(enc, products, infos, offers);
		ctx.Stop();
		return ops;
	}
}
