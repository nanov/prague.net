namespace Prague.Core.Utils;

public static class DataCacheRegistryMarshall {
	public static void SetLoaded(IDataCacheRegistry registry, Exception? error) {
		registry.CompleteLoading(error);
	}
}