namespace Prague.Kafka;

using Confluent.Kafka;

public interface IKafkaCacheBuilderProvider {
	internal ConsumerBuilder<TKey, TValue> NewConsumerBuilder<TKey, TValue>(
		IEnumerable<KeyValuePair<string, string>> config);

	internal ProducerBuilder<TKey, TValue> NewProducerBuilder<TKey, TValue>(
		IEnumerable<KeyValuePair<string, string>> config);
}

internal sealed class KafkaCacheBuilderProvider : IKafkaCacheBuilderProvider {
	public ConsumerBuilder<TKey, TValue> NewConsumerBuilder<TKey, TValue>(
		IEnumerable<KeyValuePair<string, string>> config) {
		return new ConsumerBuilder<TKey, TValue>(config);
	}

	public ProducerBuilder<TKey, TValue> NewProducerBuilder<TKey, TValue>(
		IEnumerable<KeyValuePair<string, string>> config) {
		return new ProducerBuilder<TKey, TValue>(config);
	}
}