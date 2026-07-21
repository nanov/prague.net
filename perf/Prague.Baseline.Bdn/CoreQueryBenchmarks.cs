namespace Prague.Baseline.Bdn;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using BenchmarkDotNet.Configs;
using Prague.Baseline.Scenario;
using Prague.Core;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class CoreQueryBenchmarks {
	private BaselineProductCache _products = null!;
	private BaselineProductInfoCache _infos = null!;
	private BaselineOfferCache _offers = null!;
	private int _cursor;

	private sealed class Config : ManualConfig {
		public Config() => AddJob(Job.Default
			.WithToolchain(InProcessNoEmitToolchain.Instance)
			.WithWarmupCount(1).WithIterationCount(5));
	}

	[GlobalSetup]
	public void Setup() {
		var data = DatasetFactory.Build();
		var registry = new DataCacheRegistryBuilder()
			.Register<BaselineProductCache>()
			.Register<BaselineProductInfoCache>()
			.Register<BaselineOfferCache>()
			.Build();
		_products = registry.GetCache<BaselineProductCache>();
		_infos = registry.GetCache<BaselineProductInfoCache>();
		_offers = registry.GetCache<BaselineOfferCache>();
		foreach (var p in data.Products) _products.AddOrUpdate(p);
		foreach (var i in data.Infos) _infos.AddOrUpdate(i);
		foreach (var o in data.Offers) _offers.AddOrUpdate(o);
	}

	private int NextId() {
		_cursor = (_cursor + 1) % ScenarioSpec.ProductCount;
		return _cursor + 1;
	}

	[Benchmark] public int UniqueLookup() {
		_products.Cache.TryGet(NextId(), out var _);
		return 1;
	}

	[Benchmark] public int RangeScan() {
		using var r = _products.Query().WithRange(q => q.Gte(NextId())).ExecutePooled();
		return r.Count;
	}

	[Benchmark] public int JoinOne() {
		using var r = _products.Query().WithRange(q => q.Gte(NextId()))
			.JoinWithBaselineProductInfo().ExecutePooled();
		return r.Count;
	}

	[Benchmark] public int JoinMany() {
		using var r = _products.Query().WithRange(q => q.Gte(NextId()))
			.JoinMany(_offers.Cache, _offers.ProductIdIndex).ExecutePooled();
		return r.Count;
	}

	[Benchmark] public int MultiJoin() {
		using var r = _products.Query().WithRange(q => q.Gte(NextId()))
			.JoinWithBaselineProductInfo()
			.JoinMany(_offers.Cache, _offers.ProductIdIndex)
			.ExecutePooled();
		return r.Count;
	}
}
