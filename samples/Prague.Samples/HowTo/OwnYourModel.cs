// docs: every code block of www/docs/articles/how-to/own-your-model.md — a consuming service keeps its own
// [DataCache] copy of a topic's contract (a subset, its own indexes) and reads the producer's bytes unchanged.
namespace Prague.Samples.HowTo {
	using MessagePack;
	using NUnit.Framework;
	using Prague.Core;
	using Prague.Kafka;
	using Upstream.Contracts;

	#region own-model-copy
	// The consumer's copy of Upstream.Contracts.Shipment: its own namespace, type name and enum, only the
	// properties this service reads, and the indexes this service queries by. Property names match the wire.
	public enum ParcelStatus {
		Created,
		InTransit,
		Delivered,
	}

	[DataCache]
	[DataCacheTopic("shipments.[v:env]")]
	public partial class Parcel {
		[DataCacheKey]
		public required string ShipmentId { get; init; }

		[DataCacheIndex(DataCacheIndexType.Many)]
		public required string Carrier { get; init; }

		[DataCacheIndex(DataCacheIndexType.Many)]
		public required ParcelStatus Status { get; init; }

		public long? DeliveredAtUnixMs { get; init; }
	}
	#endregion

	[TestFixture]
	public class OwnYourModel {
		[Test]
		public void TheCopyReadsTheProducersBytes() {
			#region own-model-wire
			var sent = new Shipment {
				ShipmentId = "SHP-1",
				Carrier = "DHL",
				Status = ShipmentStatus.InTransit,
				WeightGrams = 1_250,                                          // not in Parcel: skipped on read
				Recipient = new Address { Name = "Ada", City = "London" },    // not in Parcel: skipped on read
			};
			var bytes = MessagePackSerializer.Serialize(sent, PragueMessagePack.Options);   // what the producer puts on the topic

			var parcel = MessagePackSerializer.Deserialize<Parcel>(bytes, PragueMessagePack.Options);

			Assert.That(parcel.ShipmentId, Is.EqualTo("SHP-1"));
			Assert.That(parcel.Carrier, Is.EqualTo("DHL"));
			Assert.That(parcel.Status, Is.EqualTo(ParcelStatus.InTransit));   // enums travel as their numeric value
			Assert.That(parcel.DeliveredAtUnixMs, Is.Null);                    // nil on the wire; a property the wire lacks reads as default too
			#endregion
		}

		[Test]
		public void FieldsYouDoNotHoldNeverChangeEquality() {
			var parcels = new ParcelCache();
			var first = new Shipment { ShipmentId = "SHP-1", Carrier = "DHL", Status = ShipmentStatus.InTransit, WeightGrams = 1_250 };
			var reweighed = new Shipment { ShipmentId = "SHP-1", Carrier = "DHL", Status = ShipmentStatus.InTransit, WeightGrams = 1_300 };

			#region own-model-equality
			parcels.AddOrUpdate(Read(first));
			var changed = parcels.AddOrUpdate(Read(reweighed), out _);   // only WeightGrams differs, and Parcel does not hold it

			Assert.That(changed, Is.False);                              // Same: no index write, no after-handler Update
			#endregion
		}

		[Test]
		public void ARenamedPropertyReadsAsDefault() {
			#region own-model-rename-pitfall
			var bytes = MessagePackSerializer.Serialize(new Shipment { ShipmentId = "SHP-1", Carrier = "DHL" }, PragueMessagePack.Options);

			var typo = MessagePackSerializer.Deserialize<ParcelWithTypo>(bytes, PragueMessagePack.Options);

			Assert.That(typo.CarrierName, Is.Null);                      // no error: the wire has "Carrier", the copy asks for "CarrierName"
			#endregion
		}

		private static Parcel Read(Shipment s) =>
			MessagePackSerializer.Deserialize<Parcel>(MessagePackSerializer.Serialize(s, PragueMessagePack.Options), PragueMessagePack.Options);

		public sealed class ParcelWithTypo {
			public string ShipmentId { get; init; } = "";
			public string? CarrierName { get; init; }
		}
	}
}

// Stand-in for the producing team's contract. In a real system this lives in their repository, not yours.
namespace Upstream.Contracts {
	public enum ShipmentStatus {
		Created,
		InTransit,
		Delivered,
	}

	public sealed class Address {
		public string Name { get; init; } = "";
		public string City { get; init; } = "";
	}

	public sealed class Shipment {
		public string ShipmentId { get; init; } = "";
		public string Carrier { get; init; } = "";
		public ShipmentStatus Status { get; init; }
		public int WeightGrams { get; init; }
		public Address? Recipient { get; init; }
		public long? DeliveredAtUnixMs { get; init; }
	}
}
