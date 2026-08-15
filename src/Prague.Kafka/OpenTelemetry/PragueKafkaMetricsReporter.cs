namespace Prague.Kafka.OpenTelemetry;

using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Prague.Core.Collections;
using Prague.Kafka.Health;
using static Prague.Kafka.Health.KafkaCachesHealthPredicates;

internal sealed class PragueKafkaMetricsReporter : IDisposable {
	internal const string MeterName = "Prague.Kafka";

	private static readonly string MeterVersion =
		typeof(PragueKafkaMetricsReporter).Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? typeof(PragueKafkaMetricsReporter).Assembly.GetName().Version?.ToString()
		?? "0.0.0";

	private static readonly Func<string, byte, KeyValuePair<string, object?>[]> s_consumerTagBuilder =
		static (c, _) => new KeyValuePair<string, object?>[] {
			new("consumer", c)
		};

	private static readonly Func<(string, string), byte, KeyValuePair<string, object?>[]> s_cacheTagBuilder =
		static (k, _) => new KeyValuePair<string, object?>[] {
			new("consumer", k.Item1), new("cache", k.Item2)
		};

	private static readonly Func<(string, string, string, bool), byte, KeyValuePair<string, object?>[]>
		s_indexTagBuilder =
			static (k, _) => new KeyValuePair<string, object?>[] {
				new("consumer", k.Item1), new("cache", k.Item2),
				new("index", k.Item3), new("kind", k.Item4 ? "keys" : "values")
			};

	// Capacity is one number per index (no keys/values split), so no "kind" tag.
	private static readonly Func<(string, string, string), byte, KeyValuePair<string, object?>[]>
		s_indexCapacityTagBuilder =
			static (k, _) => new KeyValuePair<string, object?>[] {
				new("consumer", k.Item1), new("cache", k.Item2), new("index", k.Item3)
			};

	private readonly KafkaCachesStatistics _statistics;
	private readonly KafkaCachesHealthOptions _healthOptions;
	private readonly Meter _meter;

	private readonly QuickLookupCache<string, KeyValuePair<string, object?>[]> _consumerTags = new();
	private readonly QuickLookupCache<(string, string), KeyValuePair<string, object?>[]> _cacheTags = new();
	private readonly QuickLookupCache<(string, string, string, bool), KeyValuePair<string, object?>[]>
		_indexTags = new();
	private readonly QuickLookupCache<(string, string, string), KeyValuePair<string, object?>[]>
		_indexCapacityTags = new();

	private readonly ReusableMeasurementSource<long> _consumerPartitions = new();
	private readonly ReusableMeasurementSource<long> _consumerRtt = new();
	private readonly ReusableMeasurementSource<int> _consumerHealth = new();
	private readonly ReusableMeasurementSource<long> _cacheSize = new();
	private readonly ReusableMeasurementSource<long> _cacheMessages = new();
	private readonly ReusableMeasurementSource<int> _cacheHealth = new();
	private readonly ReusableMeasurementSource<long> _indexSize = new();
	private readonly ReusableMeasurementSource<long> _indexCapacity = new();

	internal Meter Meter => _meter;

	public PragueKafkaMetricsReporter(
		KafkaCachesStatistics statistics,
		KafkaCachesHealthOptions healthOptions,
		string prefix) {

		_statistics = statistics;
		_healthOptions = healthOptions;
		_meter = new Meter(MeterName, MeterVersion);

		if (prefix.Length > 0 && !prefix.EndsWith('.'))
			prefix += ".";

		_meter.CreateObservableUpDownCounter($"{prefix}prague.kafka.consumer.partitions.assigned",
			ObserveConsumerPartitionsAssigned, "{partition}",
			"Number of partitions currently assigned to this consumer");

		_meter.CreateObservableGauge($"{prefix}prague.kafka.consumer.broker.rtt.p99",
			ObserveConsumerBrokerRttP99, "ms",
			"Max-of-p99 broker round-trip time across brokers");

		_meter.CreateObservableGauge($"{prefix}prague.kafka.consumer.health",
			ObserveConsumerHealth, null,
			"1 when consumer is healthy, 0 otherwise");

		_meter.CreateObservableUpDownCounter($"{prefix}prague.kafka.cache.size",
			ObserveCacheSize, "{item}",
			"Current row count of the cache");

		_meter.CreateObservableCounter($"{prefix}prague.kafka.cache.messages.received",
			ObserveCacheMessagesReceived, "{message}",
			"Cumulative messages received from Kafka for this cache's topic");

		_meter.CreateObservableGauge($"{prefix}prague.kafka.cache.health",
			ObserveCacheHealth, null,
			"1 when cache is healthy, 0 otherwise");

		_meter.CreateObservableUpDownCounter($"{prefix}prague.kafka.cache.index.size",
			ObserveIndexSize, "{item}",
			"Current key/value count of the index");

		_meter.CreateObservableUpDownCounter($"{prefix}prague.kafka.cache.index.capacity",
			ObserveIndexCapacity, "{slot}",
			"Slots currently rented by the index's backing sets (utilization = size/capacity)");
	}

	private IEnumerable<Measurement<long>> ObserveConsumerPartitionsAssigned() {
		var consumers = _statistics.Consumers;
		var buffer = _consumerPartitions.Prepare(consumers.Count);
		var i = 0;
		foreach (var (cName, cStats) in consumers) {
			var tags = _consumerTags.GetOrAdd(cName, s_consumerTagBuilder, default);
			buffer[i++] = CreateMeasurement((long)cStats.AssignedPartitions, tags);
		}
		return _consumerPartitions;
	}

	private IEnumerable<Measurement<long>> ObserveConsumerBrokerRttP99() {
		var consumers = _statistics.Consumers;
		var buffer = _consumerRtt.Prepare(consumers.Count);
		var i = 0;
		foreach (var (cName, cStats) in consumers) {
			var tags = _consumerTags.GetOrAdd(cName, s_consumerTagBuilder, default);
			buffer[i++] = CreateMeasurement(cStats.BrokerLatencyMs, tags);
		}
		return _consumerRtt;
	}

	private IEnumerable<Measurement<int>> ObserveConsumerHealth() {
		var consumers = _statistics.Consumers;
		var buffer = _consumerHealth.Prepare(consumers.Count);
		var i = 0;
		foreach (var (cName, cStats) in consumers) {
			var tags = _consumerTags.GetOrAdd(cName, s_consumerTagBuilder, default);
			var healthy = IsConsumerHealthy(cStats, _healthOptions) ? 1 : 0;
			buffer[i++] = CreateMeasurement(healthy, tags);
		}
		return _consumerHealth;
	}

	private IEnumerable<Measurement<long>> ObserveCacheSize() {
		var consumers = _statistics.Consumers;
		var count = 0;
		foreach (var (_, c) in consumers) count += c.Caches.Count;
		var buffer = _cacheSize.Prepare(count);
		var i = 0;
		foreach (var (cName, cStats) in consumers) {
			foreach (var (kName, kStats) in cStats.Caches) {
				var tags = _cacheTags.GetOrAdd((cName, kName), s_cacheTagBuilder, default);
				buffer[i++] = CreateMeasurement((long)kStats.LiveSize, tags);
			}
		}
		return _cacheSize;
	}

	private IEnumerable<Measurement<long>> ObserveCacheMessagesReceived() {
		var consumers = _statistics.Consumers;
		var count = 0;
		foreach (var (_, c) in consumers) count += c.Caches.Count;
		var buffer = _cacheMessages.Prepare(count);
		var i = 0;
		foreach (var (cName, cStats) in consumers) {
			foreach (var (kName, kStats) in cStats.Caches) {
				var tags = _cacheTags.GetOrAdd((cName, kName), s_cacheTagBuilder, default);
				buffer[i++] = CreateMeasurement(kStats.TotalMessagesReceived, tags);
			}
		}
		return _cacheMessages;
	}

	private IEnumerable<Measurement<int>> ObserveCacheHealth() {
		var consumers = _statistics.Consumers;
		var count = 0;
		foreach (var (_, c) in consumers) count += c.Caches.Count;
		var buffer = _cacheHealth.Prepare(count);
		var i = 0;
		foreach (var (cName, cStats) in consumers) {
			var loadingGate = cStats.CachesLoadingCount == 0;
			foreach (var (kName, kStats) in cStats.Caches) {
				var tags = _cacheTags.GetOrAdd((cName, kName), s_cacheTagBuilder, default);
				var healthy = loadingGate && IsCacheHealthy(kStats, _healthOptions) ? 1 : 0;
				buffer[i++] = CreateMeasurement(healthy, tags);
			}
		}
		return _cacheHealth;
	}

	private IEnumerable<Measurement<long>> ObserveIndexSize() {
		var consumers = _statistics.Consumers;
		var count = 0;
		foreach (var (_, c) in consumers) {
			foreach (var (_, k) in c.Caches)
				count += k.Statistics.Indexes.Count * 2;
		}
		var buffer = _indexSize.Prepare(count);
		var i = 0;
		foreach (var (cName, cStats) in consumers) {
			foreach (var (kName, kStats) in cStats.Caches) {
				foreach (var (iName, iStats) in kStats.Statistics.Indexes) {
					var keysTags = _indexTags.GetOrAdd(
						(cName, kName, iName, true), s_indexTagBuilder, default);
					buffer[i++] = CreateMeasurement((long)iStats.LiveKeysSize, keysTags);
					var valuesTags = _indexTags.GetOrAdd(
						(cName, kName, iName, false), s_indexTagBuilder, default);
					buffer[i++] = CreateMeasurement((long)iStats.LiveValuesSize, valuesTags);
				}
			}
		}
		return _indexSize;
	}

	private IEnumerable<Measurement<long>> ObserveIndexCapacity() {
		var consumers = _statistics.Consumers;
		var count = 0;
		foreach (var (_, c) in consumers) {
			foreach (var (_, k) in c.Caches)
				count += k.Statistics.Indexes.Count;
		}
		var buffer = _indexCapacity.Prepare(count);
		var i = 0;
		foreach (var (cName, cStats) in consumers) {
			foreach (var (kName, kStats) in cStats.Caches) {
				foreach (var (iName, iStats) in kStats.Statistics.Indexes) {
					var tags = _indexCapacityTags.GetOrAdd(
						(cName, kName, iName), s_indexCapacityTagBuilder, default);
					buffer[i++] = CreateMeasurement((long)iStats.LiveCapacitySlots, tags);
				}
			}
		}
		return _indexCapacity;
	}

	public void Dispose() => _meter.Dispose();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Measurement<long> CreateMeasurement(long value, KeyValuePair<string, object?>[] tags) {
		var m = new Measurement<long>(value);
		MeasurementLongTagsRef(ref m) = tags;
		return m;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Measurement<int> CreateMeasurement(int value, KeyValuePair<string, object?>[] tags) {
		var m = new Measurement<int>(value);
		MeasurementIntTagsRef(ref m) = tags;
		return m;
	}

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_tags")]
	private static extern ref KeyValuePair<string, object?>[] MeasurementLongTagsRef(ref Measurement<long> m);

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_tags")]
	private static extern ref KeyValuePair<string, object?>[] MeasurementIntTagsRef(ref Measurement<int> m);
}
