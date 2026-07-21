namespace Prague.Baseline.Harness.Tests;

using System;
using System.Diagnostics;
using System.Threading;
using Prague.Baseline.Harness.Real;
using Prague.Baseline.Scenario;
using Prague.Core;

/// <summary>
///   Full-real ingest throughput: produces the pre-encoded dataset to a Testcontainers Kafka broker
///   and consumes it through the REAL Prague consumer (<see cref="RealIngestPipeline" />), timing the
///   load until the caches reach 500 / 500 / 10 000. Broker start + produce happen BEFORE
///   <see cref="ThroughputSessionContext.Start" /> (setup, not measured); only the consumer load is timed.
///   Requires Docker — this config is opt-in.
/// </summary>
public sealed class RealIngestThroughputTest : IThroughputTest {
	private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(60);

	public int RequiredProcessorCount => 2;

	public long Run(ThroughputSessionContext ctx) {
		var enc = Payloads.Encode(DatasetFactory.Build());
		var pipeline = new RealIngestPipeline();
		try {
			// Setup (not measured): start the broker and produce every payload.
			var bootstrap = pipeline.StartBrokerAndProduceAsync(enc).GetAwaiter().GetResult();

			ctx.Start();
			var (products, infos, offers) = pipeline
				.StartConsumerAsync(bootstrap, CancellationToken.None).GetAwaiter().GetResult();
			WaitForLoad(products, infos, offers);
			ctx.Stop();
			return ScenarioSpec.TotalEntities;
		} finally {
			pipeline.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}
	}

	private static void WaitForLoad(
		BaselineProductCache products, BaselineProductInfoCache infos, BaselineOfferCache offers) {
		var deadline = Stopwatch.StartNew();
		while (!(products.Query().Count() == ScenarioSpec.ProductCount &&
			infos.Query().Count() == ScenarioSpec.ProductCount &&
			offers.Query().Count() == ScenarioSpec.TotalOffers)) {
			if (deadline.Elapsed > LoadTimeout)
				throw new TimeoutException(
					$"full-real ingest did not complete within {LoadTimeout.TotalSeconds:0}s " +
					$"(products={products.Query().Count()}/{ScenarioSpec.ProductCount}, " +
					$"infos={infos.Query().Count()}/{ScenarioSpec.ProductCount}, " +
					$"offers={offers.Query().Count()}/{ScenarioSpec.TotalOffers}).");
			Thread.Sleep(25);
		}
	}
}
