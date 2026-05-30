namespace Prague.Kafka;

using Confluent.Kafka;

public interface IKafkaCacheBuilderProvider {
	internal RawConsumerBuilder NewRawConsumerBuilder(
		IEnumerable<KeyValuePair<string, string>> config);

	internal ProducerBuilder<TKey, TValue> NewProducerBuilder<TKey, TValue>(
		IEnumerable<KeyValuePair<string, string>> config);
}

internal sealed class KafkaCacheBuilderProvider : IKafkaCacheBuilderProvider {
	public RawConsumerBuilder NewRawConsumerBuilder(
		IEnumerable<KeyValuePair<string, string>> config) {
		return new RawConsumerBuilder(config);
	}

	public ProducerBuilder<TKey, TValue> NewProducerBuilder<TKey, TValue>(
		IEnumerable<KeyValuePair<string, string>> config) {
		return new ProducerBuilder<TKey, TValue>(config);
	}
}