namespace Prague.Baseline.Scenario;

public static class ScenarioSpec {
	public const int ProductCount = 500;
	public const int OffersPerProduct = 20;
	public const int TotalOffers = ProductCount * OffersPerProduct;
	// 500 products + 500 infos + 10000 offers = 11000
	public const int TotalEntities = ProductCount /* products */ + ProductCount /* infos */ + TotalOffers;
	public const int Seed = 42;
	// RESERVED for the deferred concurrent Phase B (read-under-write) scenario:
	// N reader threads running the query mix + 1 writer at WriterUpdatesPerSecond
	// over SteadyStateSeconds, emitting read.throughput + contended latency percentiles.
	// Not dead code — intentionally kept for the tracked, user-approved follow-up
	// (the current Phase B is a single-threaded multiJoin latency loop).
	public const int ReaderThreads = 16;
	public const int WriterUpdatesPerSecond = 2;
	public const int SteadyStateSeconds = 5;
	public const int RingCapacity = 4096;
}
