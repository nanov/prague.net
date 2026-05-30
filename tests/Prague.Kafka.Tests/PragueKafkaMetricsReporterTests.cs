namespace Prague.Kafka.Tests;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Prague.Kafka.Health;
using Prague.Kafka.OpenTelemetry;
using Core;
using Core.Collections;
using Microsoft.Extensions.DependencyInjection;
using global::OpenTelemetry.Metrics;

public sealed class PragueKafkaMetricsReporterTests {
	private sealed class StubCountableCacheIndex : ICountableCacheIndex {
		private readonly ulong _keys;
		private readonly ulong _values;

		public StubCountableCacheIndex(ulong keys, ulong values) {
			_keys = keys;
			_values = values;
		}

		public ulong GetCounters(out ulong values) {
			values = _values;
			return _keys;
		}
	}

	private sealed class SizedCollector : DataCacheStatisticsCollector {
		private ulong _size;

		public SizedCollector(ulong initial) => _size = initial;

		public override ulong CurrentSize => _size;
		public override void Performed(AddOrUpdateOperation operation) {
		}
		public override void Added() => _size++;
		public override void Updated() {
		}
		public override void Removed() => _size--;
		public override ulong Collect() => _size;
	}

	private sealed record Captured(double Value, KeyValuePair<string, object?>[] Tags);

	private sealed class Capture {
		public Dictionary<string, List<Captured>> ByInstrument { get; } = new();

		public void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) {
			if (!ByInstrument.TryGetValue(name, out var list)) {
				list = new List<Captured>();
				ByInstrument.Add(name, list);
			}
			list.Add(new Captured(value, tags.ToArray()));
		}
	}

	private static KafkaCachesStatistics MakeStats(string consumer, int cacheCount, ulong baseSize) {
		var top = new KafkaCachesStatistics();
		var c = top.GetOrAddConsumer(consumer);
		c.LastPollTimestamp = Stopwatch.GetTimestamp();
		c.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot {
			Brokers = new Dictionary<string, LibrdkafkaBrokerStats> {
				["b1"] = new LibrdkafkaBrokerStats { State = "UP" }
			}
		});
		for (var i = 0; i < cacheCount; i++) {
			var ds = new DataCacheStatistics(new SizedCollector(baseSize + (ulong)(i * 100)));
			var cache = c.AddCache($"cache{i}", new KafkaDataCacheStatistics($"topic{i}", ds));
			cache.AssignedPartitionCount = 1;
			cache.SetInitialLoad(TimeSpan.FromMilliseconds(10));
		}
		c.SetCachesLoadingCount(0);
		return top;
	}

	private static (Capture capture, MeterListener listener) StartListener(PragueKafkaMetricsReporter reporter) {
		var capture = new Capture();
		var listener = new MeterListener();
		listener.InstrumentPublished = (inst, l) => {
			if (ReferenceEquals(inst.Meter, reporter.Meter))
				l.EnableMeasurementEvents(inst);
		};
		listener.SetMeasurementEventCallback<long>((inst, m, tags, _) => capture.Add(inst.Name, m, tags));
		listener.SetMeasurementEventCallback<int>((inst, m, tags, _) => capture.Add(inst.Name, m, tags));
		listener.Start();
		return (capture, listener);
	}

	private static string? TagValue(KeyValuePair<string, object?>[] tags, string key) {
		foreach (var t in tags)
			if (t.Key == key) return t.Value?.ToString();
		return null;
	}

	[Test]
	public void Consumer_partitions_assigned_emits_value_with_consumer_tag() {
		var stats = MakeStats("clusterA", cacheCount: 1, baseSize: 0);
		stats.Consumers["clusterA"].SetAssignedPartitions(4);

		using var reporter = new PragueKafkaMetricsReporter(stats, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		Assert.That(capture.ByInstrument, Does.ContainKey("prague.kafka.consumer.partitions.assigned"));
		var rows = capture.ByInstrument["prague.kafka.consumer.partitions.assigned"];
		Assert.That(rows, Has.Count.EqualTo(1));
		Assert.That(rows[0].Value, Is.EqualTo(4));
		Assert.That(TagValue(rows[0].Tags, "consumer"), Is.EqualTo("clusterA"));
	}

	[Test]
	public void Consumer_broker_rtt_p99_returns_max_p99_in_ms() {
		var stats = MakeStats("clusterA", cacheCount: 1, baseSize: 0);
		stats.Consumers["clusterA"].UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot {
			Brokers = new Dictionary<string, LibrdkafkaBrokerStats> {
				["b1"] = new LibrdkafkaBrokerStats {
					State = "UP",
					Rtt = new LibrdkafkaWindowStats { P99 = 1500 }
				},
				["b2"] = new LibrdkafkaBrokerStats {
					State = "UP",
					Rtt = new LibrdkafkaWindowStats { P99 = 2700 }
				}
			}
		});

		using var reporter = new PragueKafkaMetricsReporter(stats, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		var rows = capture.ByInstrument["prague.kafka.consumer.broker.rtt.p99"];
		Assert.That(rows, Has.Count.EqualTo(1));
		Assert.That(rows[0].Value, Is.EqualTo(2)); // 2700 µs / 1000 = 2 ms
	}

	[Test]
	public void Consumer_health_is_one_when_healthy() {
		var stats = MakeStats("clusterA", cacheCount: 1, baseSize: 0);

		using var reporter = new PragueKafkaMetricsReporter(stats, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		var rows = capture.ByInstrument["prague.kafka.consumer.health"];
		Assert.That(rows[0].Value, Is.EqualTo(1));
	}

	[Test]
	public void Consumer_health_is_zero_when_fatal_latched() {
		var stats = MakeStats("clusterA", cacheCount: 1, baseSize: 0);
		stats.Consumers["clusterA"].IsFatalLatched = true;

		using var reporter = new PragueKafkaMetricsReporter(stats, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		Assert.That(capture.ByInstrument["prague.kafka.consumer.health"][0].Value, Is.EqualTo(0));
	}

	[Test]
	public void Consumer_health_is_zero_when_partitions_lost() {
		var stats = MakeStats("clusterA", cacheCount: 1, baseSize: 0);
		stats.Consumers["clusterA"].HasLostPartitions = true;

		using var reporter = new PragueKafkaMetricsReporter(stats, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		Assert.That(capture.ByInstrument["prague.kafka.consumer.health"][0].Value, Is.EqualTo(0));
	}

	[Test]
	public void Consumer_health_is_zero_when_poll_stalled() {
		var stats = MakeStats("clusterA", cacheCount: 1, baseSize: 0);
		stats.Consumers["clusterA"].LastPollTimestamp =
			Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 60);

		using var reporter = new PragueKafkaMetricsReporter(stats, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		Assert.That(capture.ByInstrument["prague.kafka.consumer.health"][0].Value, Is.EqualTo(0));
	}

	[Test]
	public void Cache_size_emits_one_measurement_per_cache_with_tags() {
		var top = new KafkaCachesStatistics();
		var c = top.GetOrAddConsumer("clusterA");
		c.LastPollTimestamp = Stopwatch.GetTimestamp();
		c.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot {
			Brokers = new Dictionary<string, LibrdkafkaBrokerStats> { ["b1"] = new() { State = "UP" } }
		});
		var s1 = new DataCacheStatistics(new SizedCollector(100));
		var s2 = new DataCacheStatistics(new SizedCollector(200));
		c.AddCache("c1", new KafkaDataCacheStatistics("t1", s1)).AssignedPartitionCount = 1;
		c.AddCache("c2", new KafkaDataCacheStatistics("t2", s2)).AssignedPartitionCount = 1;
		c.SetCachesLoadingCount(0);

		using var reporter = new PragueKafkaMetricsReporter(top, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		var rows = capture.ByInstrument["prague.kafka.cache.size"];
		Assert.That(rows, Has.Count.EqualTo(2));
		var values = rows.Select(r => r.Value).OrderBy(v => v).ToArray();
		Assert.That(values, Is.EqualTo(new double[] { 100, 200 }));
		foreach (var row in rows) {
			Assert.That(TagValue(row.Tags, "consumer"), Is.EqualTo("clusterA"));
			Assert.That(TagValue(row.Tags, "cache"), Is.AnyOf("c1", "c2"));
		}
	}

	[Test]
	public void Cache_messages_received_reads_from_per_cache_field() {
		var stats = MakeStats("clusterA", cacheCount: 2, baseSize: 0);
		var caches = stats.Consumers["clusterA"].Caches;
		caches["cache0"].TotalMessagesReceived = 1000;
		caches["cache1"].TotalMessagesReceived = 2000;

		using var reporter = new PragueKafkaMetricsReporter(stats, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
			listener.RecordObservableInstruments();
		}

		var rows = capture.ByInstrument["prague.kafka.cache.messages.received"];
		// Two scrapes × two caches = 4 measurements; both scrapes see same values.
		Assert.That(rows, Has.Count.EqualTo(4));
		var firstScrape = rows.Take(2).Select(r => r.Value).OrderBy(v => v).ToArray();
		var secondScrape = rows.Skip(2).Select(r => r.Value).OrderBy(v => v).ToArray();
		Assert.That(firstScrape, Is.EqualTo(new double[] { 1000, 2000 }));
		Assert.That(secondScrape, Is.EqualTo(new double[] { 1000, 2000 }));
	}

	[Test]
	public void Cache_health_emits_one_per_cache_with_individual_health() {
		var stats = MakeStats("clusterA", cacheCount: 2, baseSize: 0);
		var caches = stats.Consumers["clusterA"].Caches;
		caches["cache1"].IsLoopFaulted = true;

		using var reporter = new PragueKafkaMetricsReporter(stats, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		var rows = capture.ByInstrument["prague.kafka.cache.health"];
		Assert.That(rows, Has.Count.EqualTo(2));
		var values = rows.Select(r => r.Value).OrderBy(v => v).ToArray();
		Assert.That(values, Is.EqualTo(new double[] { 0, 1 }));
	}

	[Test]
	public void Cache_health_is_zero_for_all_caches_when_initial_load_incomplete() {
		var stats = MakeStats("clusterA", cacheCount: 2, baseSize: 0);
		stats.Consumers["clusterA"].SetCachesLoadingCount(1);

		using var reporter = new PragueKafkaMetricsReporter(stats, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		var rows = capture.ByInstrument["prague.kafka.cache.health"];
		Assert.That(rows, Has.Count.EqualTo(2));
		Assert.That(rows.All(r => r.Value == 0), Is.True);
	}

	[Test]
	public void Cache_index_size_emits_keys_and_values_per_index() {
		var top = new KafkaCachesStatistics();
		var c = top.GetOrAddConsumer("clusterA");
		c.LastPollTimestamp = Stopwatch.GetTimestamp();
		c.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot {
			Brokers = new Dictionary<string, LibrdkafkaBrokerStats> { ["b1"] = new() { State = "UP" } }
		});
		var ds = new DataCacheStatistics(new SizedCollector(0));
		DataCacheStatisticsMarshall.AddIndex(ds, "idxA", DataCacheIndexType.Unique, new StubCountableCacheIndex(10, 15));
		DataCacheStatisticsMarshall.AddIndex(ds, "idxB", DataCacheIndexType.Unique, new StubCountableCacheIndex(20, 25));
		var kdc = new KafkaDataCacheStatistics("topic", ds);
		c.AddCache("cacheX", kdc).AssignedPartitionCount = 1;
		c.SetCachesLoadingCount(0);

		using var reporter = new PragueKafkaMetricsReporter(top, new KafkaCachesHealthOptions(), prefix: "");
		var (capture, listener) = StartListener(reporter);
		using (listener) {
			listener.RecordObservableInstruments();
		}

		var rows = capture.ByInstrument["prague.kafka.cache.index.size"];
		Assert.That(rows, Has.Count.EqualTo(4));

		var idxAKeys = rows.Single(r => TagValue(r.Tags, "index") == "idxA" && TagValue(r.Tags, "kind") == "keys");
		var idxAValues = rows.Single(r => TagValue(r.Tags, "index") == "idxA" && TagValue(r.Tags, "kind") == "values");
		var idxBKeys = rows.Single(r => TagValue(r.Tags, "index") == "idxB" && TagValue(r.Tags, "kind") == "keys");
		var idxBValues = rows.Single(r => TagValue(r.Tags, "index") == "idxB" && TagValue(r.Tags, "kind") == "values");

		Assert.That(idxAKeys.Value, Is.EqualTo(10));
		Assert.That(idxAValues.Value, Is.EqualTo(15));
		Assert.That(idxBKeys.Value, Is.EqualTo(20));
		Assert.That(idxBValues.Value, Is.EqualTo(25));

		foreach (var row in rows) {
			Assert.That(TagValue(row.Tags, "consumer"), Is.EqualTo("clusterA"));
			Assert.That(TagValue(row.Tags, "cache"), Is.EqualTo("cacheX"));
		}
	}

	[Test]
	public void Prefix_namespaces_all_instrument_names() {
		var top = new KafkaCachesStatistics();
		var c = top.GetOrAddConsumer("clusterA");
		c.LastPollTimestamp = Stopwatch.GetTimestamp();
		c.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot {
			Brokers = new Dictionary<string, LibrdkafkaBrokerStats> { ["b1"] = new() { State = "UP" } }
		});
		var ds = new DataCacheStatistics(new SizedCollector(0));
		DataCacheStatisticsMarshall.AddIndex(ds, "idx", DataCacheIndexType.Unique, new StubCountableCacheIndex(1, 1));
		c.AddCache("cache", new KafkaDataCacheStatistics("topic", ds)).AssignedPartitionCount = 1;
		c.SetCachesLoadingCount(0);

		var seenNames = new HashSet<string>();
		using var reporter = new PragueKafkaMetricsReporter(top, new KafkaCachesHealthOptions(), prefix: "custom.");
		using var listener = new MeterListener();
		listener.InstrumentPublished = (inst, l) => {
			if (ReferenceEquals(inst.Meter, reporter.Meter)) {
				seenNames.Add(inst.Name);
				l.EnableMeasurementEvents(inst);
			}
		};
		listener.SetMeasurementEventCallback<long>((_, _, _, _) => { });
		listener.SetMeasurementEventCallback<int>((_, _, _, _) => { });
		listener.Start();
		listener.RecordObservableInstruments();

		Assert.That(seenNames, Has.Count.EqualTo(7));
		foreach (var name in seenNames)
			Assert.That(name, Does.StartWith("custom.prague.kafka."));
	}

	[Test]
	public void Allocation_test_callbacks_after_warmup_under_budget() {
		var top = new KafkaCachesStatistics();
		var c = top.GetOrAddConsumer("clusterA");
		c.LastPollTimestamp = Stopwatch.GetTimestamp();
		c.UpdateFromLibrdkafkaStats(new LibrdkafkaStatsSnapshot {
			Brokers = new Dictionary<string, LibrdkafkaBrokerStats> { ["b1"] = new() { State = "UP" } }
		});
		for (var i = 0; i < 3; i++) {
			var ds = new DataCacheStatistics(new SizedCollector(100));
			DataCacheStatisticsMarshall.AddIndex(ds, $"idx{i}", DataCacheIndexType.Unique, new StubCountableCacheIndex(10, 20));
			c.AddCache($"cache{i}", new KafkaDataCacheStatistics($"t{i}", ds)).AssignedPartitionCount = 1;
		}
		c.SetCachesLoadingCount(0);

		using var reporter = new PragueKafkaMetricsReporter(top, new KafkaCachesHealthOptions(), prefix: "");
		using var listener = new MeterListener();
		listener.InstrumentPublished = (inst, l) => {
			if (ReferenceEquals(inst.Meter, reporter.Meter)) l.EnableMeasurementEvents(inst);
		};
		listener.SetMeasurementEventCallback<long>((_, _, _, _) => { });
		listener.SetMeasurementEventCallback<int>((_, _, _, _) => { });
		listener.Start();

		// Warm-up: first few calls populate TagList caches and grow buffers.
		for (var w = 0; w < 4; w++)
			listener.RecordObservableInstruments();

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 16; i++)
			listener.RecordObservableInstruments();
		var delta = GC.GetAllocatedBytesForCurrentThread() - before;

		// 7 instruments × 16 scrapes = 112 callbacks. Genuinely zero bytes per
		// callback after warm-up:
		//   * tag arrays cached per dimension combo via QuickLookupCache
		//   * Measurement<T>[] buffers pre-grown (grow-only)
		//   * Measurement<T> built via no-tags ctor + [UnsafeAccessor] _tags swap (no Clone)
		//   * the callback returns a ReusableMeasurementSource<T> instance which
		//     IS both the IEnumerable and the IEnumerator — GetEnumerator()
		//     returns `this`, so the SDK's foreach doesn't allocate an enumerator
		//   * FrozenDictionary's enumerator is a struct (no alloc)
		var perCallback = delta / 112.0;
		TestContext.Out.WriteLine($"Allocated {delta} bytes across 16 scrapes ({perCallback:F1} bytes/callback)");
		Assert.That(delta, Is.EqualTo(0),
			$"Allocated {delta} bytes across 16 scrapes ({perCallback:F1} bytes/callback)");
	}

	[Test]
	public void DI_plumbing_registers_reporter_via_AddPragueKafkaInstrumentation() {
		var services = new ServiceCollection();
		services.AddSingleton(new KafkaCachesStatistics());
		services.Configure<KafkaCachesHealthOptions>(_ => { });
		services.AddOpenTelemetry().WithMetrics(m => m.AddPragueKafkaInstrumentation());

		using var sp = services.BuildServiceProvider();
		Assert.DoesNotThrow(() => sp.GetRequiredService<MeterProvider>());
	}

	[Test]
	public void Extension_default_prefix_is_empty() {
		var services = new ServiceCollection();
		services.AddSingleton(new KafkaCachesStatistics());
		services.Configure<KafkaCachesHealthOptions>(_ => { });
		services.AddOpenTelemetry().WithMetrics(m => m.AddPragueKafkaInstrumentation());

		using var sp = services.BuildServiceProvider();
		sp.GetRequiredService<MeterProvider>(); // forces factory invocation → reporter ctor

		var names = new HashSet<string>();
		using var listener = new MeterListener();
		listener.InstrumentPublished = (inst, l) => {
			if (inst.Meter.Name == PragueKafkaMetricsReporter.MeterName)
				names.Add(inst.Name);
		};
		listener.Start();

		Assert.That(names, Has.Count.EqualTo(7), "all 7 Prague.Kafka instruments should be registered");
		Assert.That(names, Is.All.StartWith("prague.kafka."));
	}

	[Test]
	public void Extension_explicit_empty_prefix_drops_the_prefix() {
		var services = new ServiceCollection();
		services.AddSingleton(new KafkaCachesStatistics());
		services.Configure<KafkaCachesHealthOptions>(_ => { });
		services.AddOpenTelemetry().WithMetrics(m => m.AddPragueKafkaInstrumentation(prefix: ""));

		using var sp = services.BuildServiceProvider();
		sp.GetRequiredService<MeterProvider>();

		var names = new HashSet<string>();
		using var listener = new MeterListener();
		listener.InstrumentPublished = (inst, l) => {
			if (inst.Meter.Name == PragueKafkaMetricsReporter.MeterName)
				names.Add(inst.Name);
		};
		listener.Start();

		Assert.That(names, Has.Count.EqualTo(7));
		Assert.That(names, Is.All.StartWith("prague.kafka."));
		Assert.That(names, Has.None.StartWith("custom."));
	}
}
