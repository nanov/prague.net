namespace Prague.Kafka.Tests;

using NUnit.Framework;
using Prague.Baseline.Scenario;
using Prague.Kafka.SerDe;

[TestFixture]
public class BaselinePayloadTests {
	[Test]
	public void Encode_ValueBytes_RoundTripDeserialize() {
		var data = DatasetFactory.Build();
		var encoded = Payloads.Encode(data);

		Assert.That(encoded.Products.Length, Is.EqualTo(ScenarioSpec.ProductCount));
		var back = CacheSerde<BaselineProduct>.DeserializeFromSpan(encoded.Products[0].Value);
		Assert.That(back.Id, Is.EqualTo(data.Products[0].Id));
		Assert.That(back.Range, Is.EqualTo(data.Products[0].Range));
	}
}
