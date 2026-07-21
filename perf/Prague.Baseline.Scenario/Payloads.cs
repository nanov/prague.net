namespace Prague.Baseline.Scenario;

using Prague.Kafka.SerDe;

public sealed record EncodedEntity(byte[] Key, byte[] Value);

public readonly record struct EncodedSet(
	EncodedEntity[] Products, EncodedEntity[] Infos, EncodedEntity[] Offers);

public static class Payloads {
	public static EncodedSet Encode(Dataset d) => new(
		Map(d.Products, static p => p.Id),
		Map(d.Infos, static i => i.Id),
		Map(d.Offers, static o => o.Id));

	private static EncodedEntity[] Map<T>(T[] items, Func<T, int> key) {
		var result = new EncodedEntity[items.Length];
		for (var i = 0; i < items.Length; i++) {
			result[i] = new EncodedEntity(
				CacheSerde<int>.Serialize(key(items[i])),
				CacheSerde<T>.Serialize(items[i]));
		}
		return result;
	}
}
