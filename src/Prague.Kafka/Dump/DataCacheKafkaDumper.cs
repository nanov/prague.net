namespace Prague.Kafka.Dump;

using Prague.Core;

public static class DataCacheKafkaDumper {
	public static void CreateDumps(this IDataCacheRegistry registry, string path) {
		Directory.CreateDirectory(path);
		foreach (var cache in registry.GetCachesAs<IKafkaCache>()) {
			var filePath = Path.Combine(path, $"{cache.Topic}.pkd");
			cache.CreateDump(filePath);
		}
	}
}
