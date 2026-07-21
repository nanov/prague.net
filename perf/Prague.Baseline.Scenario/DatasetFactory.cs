namespace Prague.Baseline.Scenario;

public readonly record struct Dataset(
	BaselineProduct[] Products,
	BaselineProductInfo[] Infos,
	BaselineOffer[] Offers);

public static class DatasetFactory {
	public static Dataset Build() {
		var rng = new Random(ScenarioSpec.Seed);
		var products = new BaselineProduct[ScenarioSpec.ProductCount];
		var infos = new BaselineProductInfo[ScenarioSpec.ProductCount];
		var offers = new BaselineOffer[ScenarioSpec.TotalOffers];

		var offerId = 1;
		for (var i = 0; i < ScenarioSpec.ProductCount; i++) {
			var id = i + 1;
			products[i] = new BaselineProduct {
				Id = id,
				Range = rng.Next(0, ScenarioSpec.ProductCount),
				Category = "Category_" + (id % 10),
				Status = (id % 3) switch { 0 => "Active", 1 => "Scheduled", _ => "Archived" },
				IsPublished = id % 2 == 0,
				PrimaryValue = rng.Next(0, 5),
				LastUpdated = id,
			};
			infos[i] = new BaselineProductInfo {
				Id = id,
				ProductId = id,
				Warehouse = "WH_" + (id % 7),
				StockCount = rng.Next(0, 1000),
				LastUpdated = id,
			};
			for (var k = 0; k < ScenarioSpec.OffersPerProduct; k++) {
				offers[offerId - 1] = new BaselineOffer {
					Id = offerId,
					ProductId = id,
					IsActive = offerId % 4 != 0,
					BasePrice = offerId % 1000,
					DisplayOrder = k,
					LastUpdated = offerId,
				};
				offerId++;
			}
		}
		return new Dataset(products, infos, offers);
	}
}
