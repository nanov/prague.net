namespace Prague.Baseline.Bdn;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using BenchmarkDotNet.Configs;
using Prague.Baseline.Scenario;
using Prague.Core;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class CoreIngestBenchmarks {
	private Dataset _data;

	private sealed class Config : ManualConfig {
		public Config() => AddJob(Job.Default
			.WithToolchain(InProcessNoEmitToolchain.Instance)
			.WithWarmupCount(1).WithIterationCount(5));
	}

	[GlobalSetup]
	public void Setup() => _data = DatasetFactory.Build();

	// One op = load the whole dataset into fresh caches (throughput = TotalEntities / mean).
	[Benchmark]
	public int IngestAll() {
		var registry = new DataCacheRegistryBuilder()
			.Register<BaselineProductCache>()
			.Register<BaselineProductInfoCache>()
			.Register<BaselineOfferCache>()
			.Build();
		var products = registry.GetCache<BaselineProductCache>();
		var infos = registry.GetCache<BaselineProductInfoCache>();
		var offers = registry.GetCache<BaselineOfferCache>();

		for (var i = 0; i < _data.Products.Length; i++) products.AddOrUpdate(_data.Products[i]);
		for (var i = 0; i < _data.Infos.Length; i++) infos.AddOrUpdate(_data.Infos[i]);
		for (var i = 0; i < _data.Offers.Length; i++) offers.AddOrUpdate(_data.Offers[i]);
		return ScenarioSpec.TotalEntities;
	}
}
