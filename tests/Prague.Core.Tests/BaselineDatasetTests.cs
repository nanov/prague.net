namespace Prague.Core.Tests;

using NUnit.Framework;
using Prague.Baseline.Scenario;

[TestFixture]
public class BaselineDatasetTests {
	[Test]
	public void Build_IsDeterministic_AndHasExpectedCounts() {
		var a = DatasetFactory.Build();
		var b = DatasetFactory.Build();

		Assert.That(a.Products.Length, Is.EqualTo(ScenarioSpec.ProductCount));
		Assert.That(a.Infos.Length, Is.EqualTo(ScenarioSpec.ProductCount));
		Assert.That(a.Offers.Length, Is.EqualTo(ScenarioSpec.TotalOffers));

		// Determinism: same seed -> identical field values.
		Assert.That(a.Products[0].Range, Is.EqualTo(b.Products[0].Range));
		Assert.That(a.Infos[^1].StockCount, Is.EqualTo(b.Infos[^1].StockCount));
		Assert.That(a.Products[^1].PrimaryValue, Is.EqualTo(b.Products[^1].PrimaryValue));
	}
}
