namespace Prague.Generated.Tests.Polymorphic;
using Prague.Generated.Tests.Models;

using Prague.Tests.Models;
using NUnit.Framework;

/// <summary>
///   Tests for polymorphic types defined in a separate/external assembly.
///   Verifies that the code generator correctly finds derived types from referenced assemblies.
/// </summary>
[TestFixture]
public class ExternalPolymorphicTypesTests {
	[Test]
	public void Clone_ExternalInterface_ElectronicsType_CreatesDeepCopy() {
		// Arrange
		var original = new ExternalPolymorphicCacheItem {
			EventId = 1,
			LiveData = new ExternalElectronicsTelemetry {
				Score = 3,
				ListingId = "MATCH-001",
				PrimaryValue = 2,
				SecondaryValue = 1,
				CurrentPhase = "2nd"
			}
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.LiveData, Is.Not.SameAs(original.LiveData));
		Assert.That(cloned.LiveData, Is.InstanceOf<ExternalElectronicsTelemetry>());

		var clonedElectronics = (ExternalElectronicsTelemetry)cloned.LiveData!;
		var originalElectronics = (ExternalElectronicsTelemetry)original.LiveData!;
		Assert.That(clonedElectronics.DeviceKind, Is.EqualTo(originalElectronics.DeviceKind));
		Assert.That(clonedElectronics.Score, Is.EqualTo(originalElectronics.Score));
		Assert.That(clonedElectronics.ListingId, Is.EqualTo(originalElectronics.ListingId));
		Assert.That(clonedElectronics.PrimaryValue, Is.EqualTo(originalElectronics.PrimaryValue));
		Assert.That(clonedElectronics.SecondaryValue, Is.EqualTo(originalElectronics.SecondaryValue));
		Assert.That(clonedElectronics.CurrentPhase, Is.EqualTo(originalElectronics.CurrentPhase));
	}

	[Test]
	public void Clone_ExternalInterface_SensorType_CreatesDeepCopy() {
		// Arrange
		var original = new ExternalPolymorphicCacheItem {
			EventId = 2,
			LiveData = new ExternalSensorTelemetry {
				Score = 6,
				ListingId = "MATCH-002",
				Tiers = 2,
				Cycles = 5,
				ActiveNode = "Player1"
			}
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.LiveData, Is.Not.SameAs(original.LiveData));
		Assert.That(cloned.LiveData, Is.InstanceOf<ExternalSensorTelemetry>());

		var clonedSensor = (ExternalSensorTelemetry)cloned.LiveData!;
		var originalSensor = (ExternalSensorTelemetry)original.LiveData!;
		Assert.That(clonedSensor.DeviceKind, Is.EqualTo(originalSensor.DeviceKind));
		Assert.That(clonedSensor.Tiers, Is.EqualTo(originalSensor.Tiers));
		Assert.That(clonedSensor.Cycles, Is.EqualTo(originalSensor.Cycles));
		Assert.That(clonedSensor.ActiveNode, Is.EqualTo(originalSensor.ActiveNode));
	}

	[Test]
	public void Clone_ExternalInterface_AudioType_CreatesDeepCopy() {
		// Arrange
		var original = new ExternalPolymorphicCacheItem {
			EventId = 3,
			LiveData = new ExternalAudioTelemetry {
				Score = 100,
				ListingId = "MATCH-003",
				Segment = 4,
				PrimaryPoints = 55,
				SecondaryPoints = 45
			}
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.LiveData, Is.Not.SameAs(original.LiveData));
		Assert.That(cloned.LiveData, Is.InstanceOf<ExternalAudioTelemetry>());

		var clonedAudio = (ExternalAudioTelemetry)cloned.LiveData!;
		Assert.That(clonedAudio.Segment, Is.EqualTo(4));
		Assert.That(clonedAudio.PrimaryPoints, Is.EqualTo(55));
		Assert.That(clonedAudio.SecondaryPoints, Is.EqualTo(45));
	}

	[Test]
	public void Clone_ExternalBaseClass_PreservesRuntimeType() {
		// Arrange
		var original = new ExternalPolymorphicBaseClassCacheItem {
			EventId = 4,
			LiveData = new ExternalElectronicsTelemetry {
				Score = 5,
				PrimaryValue = 3,
				SecondaryValue = 2
			}
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned.LiveData, Is.Not.SameAs(original.LiveData));
		Assert.That(cloned.LiveData!.GetType(), Is.EqualTo(typeof(ExternalElectronicsTelemetry)));
	}

	[Test]
	public void Clone_ExternalInterface_NullValue_HandlesCorrectly() {
		// Arrange
		var original = new ExternalPolymorphicCacheItem {
			EventId = 5,
			LiveData = null
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.LiveData, Is.Null);
	}

	[Test]
	public void Clone_ModifyCloned_DoesNotAffectOriginal() {
		// Arrange
		var original = new ExternalPolymorphicCacheItem {
			EventId = 6,
			LiveData = new ExternalElectronicsTelemetry {
				Score = 2,
				PrimaryValue = 1,
				SecondaryValue = 1
			}
		};

		// Act
		var cloned = original.Clone();
		var clonedElectronics = (ExternalElectronicsTelemetry)cloned.LiveData!;
		clonedElectronics.PrimaryValue = 5;
		clonedElectronics.Score = 10;

		// Assert
		var originalElectronics = (ExternalElectronicsTelemetry)original.LiveData!;
		Assert.That(originalElectronics.PrimaryValue, Is.EqualTo(1));
		Assert.That(originalElectronics.Score, Is.EqualTo(2));
	}
}
