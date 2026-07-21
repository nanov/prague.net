namespace Prague.Baseline.Scenario;

public static class ScenarioSpec {
	public const int ProductCount = 500;
	public const int OffersPerProduct = 20;
	public const int TotalOffers = ProductCount * OffersPerProduct;
	public const int TotalEntities = ProductCount + ProductCount + TotalOffers;
	public const int Seed = 42;
	public const int ReaderThreads = 16;
	public const int WriterUpdatesPerSecond = 2;
	public const int SteadyStateSeconds = 5;
	public const int RingCapacity = 4096;
}
