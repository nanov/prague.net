namespace Prague.Baseline.Scenario;

public static class ScenarioSpec {
	public const int ProductCount = 500;
	public const int OffersPerProduct = 20;
	public const int TotalOffers = ProductCount * OffersPerProduct;
	// 500 products + 500 infos + 10000 offers = 11000
	public const int TotalEntities = ProductCount /* products */ + ProductCount /* infos */ + TotalOffers;
	public const int Seed = 42;
	// Drives the `concurrent` config (read-under-write Phase B): N reader threads running the query mix
	// + 1 writer at WriterUpdatesPerSecond over SteadyStateSeconds, emitting read.throughput +
	// contended latency percentiles (p50/p99/p999). Consumed by ConcurrentReadThroughputTest and
	// ConcurrentQueryLatencyTest.
	public const int ReaderThreads = 16;
	public const int WriterUpdatesPerSecond = 2;
	public const int SteadyStateSeconds = 5;
	public const int RingCapacity = 4096;
}
