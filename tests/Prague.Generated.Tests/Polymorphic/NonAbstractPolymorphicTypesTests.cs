namespace Prague.Generated.Tests.Polymorphic;
using Prague.Generated.Tests.Models;

using NUnit.Framework;

[TestFixture]
public class NonAbstractPolymorphicTypesTests {
	// ========== Equality Tests ==========

	[Test]
	public void CacheEquals_DifferentSealedTypes_ReturnsFalse() {
		// Arrange
		var cacheWithCar = new VehicleCache
			{ Id = 1, MyVehicle = new Car { Brand = "Toyota", Year = 2020, Doors = 4, Model = "Camry" } };
		var cacheWithTruck = new VehicleCache
			{ Id = 1, MyVehicle = new Truck { Brand = "Toyota", Year = 2020, PayloadCapacity = 1000 } };

		// Act & Assert
		Assert.That(cacheWithCar.CacheEquals(cacheWithTruck), Is.False);
	}

	[Test]
	public void CacheEquals_SameSealedType_SameValues_ReturnsTrue() {
		// Arrange
		var cache1 = new VehicleCache
			{ Id = 1, MyVehicle = new Car { Brand = "Toyota", Year = 2020, Doors = 4, Model = "Camry" } };
		var cache2 = new VehicleCache
			{ Id = 1, MyVehicle = new Car { Brand = "Toyota", Year = 2020, Doors = 4, Model = "Camry" } };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.True);
	}

	[Test]
	public void CacheEquals_SameSealedType_DifferentValues_ReturnsFalse() {
		// Arrange
		var cache1 = new VehicleCache
			{ Id = 1, MyVehicle = new Car { Brand = "Toyota", Year = 2020, Doors = 4, Model = "Camry" } };
		var cache2 = new VehicleCache
			{ Id = 1, MyVehicle = new Car { Brand = "Toyota", Year = 2020, Doors = 2, Model = "Camry" } };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.False);
	}

	[Test]
	public void CacheEquals_MultiLevelInheritance_DifferentTypes_ReturnsFalse() {
		// Arrange - Car vs RaceBike (RaceBike -> Motorcycle -> Vehicle)
		var cacheWithCar = new VehicleCache { Id = 1, MyVehicle = new Car { Brand = "Toyota", Year = 2020, Doors = 4 } };
		var cacheWithBike = new VehicleCache
			{ Id = 1, MyVehicle = new RaceBike { Brand = "Ducati", Year = 2020, TopSpeed = 300 } };

		// Act & Assert
		Assert.That(cacheWithCar.CacheEquals(cacheWithBike), Is.False);
	}

	[Test]
	public void CacheEquals_MultiLevelInheritance_SameType_ReturnsTrue() {
		// Arrange
		var cache1 = new VehicleCache {
			Id = 1,
			MyVehicle = new RaceBike {
				Brand = "Ducati",
				Year = 2020,
				HasSidecar = false,
				TopSpeed = 300
			}
		};
		var cache2 = new VehicleCache {
			Id = 1,
			MyVehicle = new RaceBike {
				Brand = "Ducati",
				Year = 2020,
				HasSidecar = false,
				TopSpeed = 300
			}
		};

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.True);
	}

	[Test]
	public void CacheEquals_InheritedProperty_Checked() {
		// Arrange - Same type but different inherited Brand property
		var cache1 = new VehicleCache { Id = 1, MyVehicle = new Car { Brand = "Toyota", Year = 2020, Doors = 4 } };
		var cache2 = new VehicleCache { Id = 1, MyVehicle = new Car { Brand = "Honda", Year = 2020, Doors = 4 } };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.False);
	}

	[Test]
	public void CacheEquals_NullVehicles_ReturnsTrue() {
		// Arrange
		var cache1 = new VehicleCache { Id = 1, MyVehicle = null! };
		var cache2 = new VehicleCache { Id = 1, MyVehicle = null! };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.True);
	}

	[Test]
	public void CacheEquals_OneNullVehicle_ReturnsFalse() {
		// Arrange
		var cache1 = new VehicleCache { Id = 1, MyVehicle = new Car { Brand = "Toyota", Year = 2020, Doors = 4 } };
		var cache2 = new VehicleCache { Id = 1, MyVehicle = null! };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.False);
	}

	[Test]
	public void CacheEquals_BaseClassVsDerivedClass_ReturnsFalse() {
		// Arrange - Direct Vehicle instance vs Car instance
		var cacheWithBase = new VehicleCache { Id = 1, MyVehicle = new Vehicle { Brand = "Generic", Year = 2020 } };
		var cacheWithDerived = new VehicleCache
			{ Id = 1, MyVehicle = new Car { Brand = "Generic", Year = 2020, Doors = 4 } };

		// Act & Assert
		Assert.That(cacheWithBase.CacheEquals(cacheWithDerived), Is.False);
	}

	// ========== Clone Tests ==========

	[Test]
	public void Clone_SealedTypeCar_CreatesDeepCopy() {
		// Arrange
		var original = new VehicleCache {
			Id = 1,
			MyVehicle = new Car { Brand = "Toyota", Year = 2020, Doors = 4, Model = "Camry" }
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.MyVehicle, Is.Not.SameAs(original.MyVehicle));
		Assert.That(cloned.MyVehicle, Is.InstanceOf<Car>());

		var clonedCar = (Car)cloned.MyVehicle;
		var originalCar = (Car)original.MyVehicle;
		Assert.That(clonedCar.Brand, Is.EqualTo(originalCar.Brand));
		Assert.That(clonedCar.Year, Is.EqualTo(originalCar.Year));
		Assert.That(clonedCar.Doors, Is.EqualTo(originalCar.Doors));
		Assert.That(clonedCar.Model, Is.EqualTo(originalCar.Model));
	}

	[Test]
	public void Clone_SealedTypeTruck_CreatesDeepCopy() {
		// Arrange
		var original = new VehicleCache {
			Id = 1,
			MyVehicle = new Truck { Brand = "Ford", Year = 2021, PayloadCapacity = 2000, HasTrailer = true }
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.MyVehicle, Is.Not.SameAs(original.MyVehicle));
		Assert.That(cloned.MyVehicle, Is.InstanceOf<Truck>());

		var clonedTruck = (Truck)cloned.MyVehicle;
		var originalTruck = (Truck)original.MyVehicle;
		Assert.That(clonedTruck.Brand, Is.EqualTo(originalTruck.Brand));
		Assert.That(clonedTruck.Year, Is.EqualTo(originalTruck.Year));
		Assert.That(clonedTruck.PayloadCapacity, Is.EqualTo(originalTruck.PayloadCapacity));
		Assert.That(clonedTruck.HasTrailer, Is.EqualTo(originalTruck.HasTrailer));
	}

	[Test]
	public void Clone_MultiLevelInheritance_CreatesDeepCopy() {
		// Arrange
		var original = new VehicleCache {
			Id = 1,
			MyVehicle = new RaceBike {
				Brand = "Ducati",
				Year = 2020,
				HasSidecar = false,
				TopSpeed = 300
			}
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.MyVehicle, Is.Not.SameAs(original.MyVehicle));
		Assert.That(cloned.MyVehicle, Is.InstanceOf<RaceBike>());

		var clonedBike = (RaceBike)cloned.MyVehicle;
		var originalBike = (RaceBike)original.MyVehicle;
		Assert.That(clonedBike.Brand, Is.EqualTo(originalBike.Brand));
		Assert.That(clonedBike.Year, Is.EqualTo(originalBike.Year));
		Assert.That(clonedBike.HasSidecar, Is.EqualTo(originalBike.HasSidecar));
		Assert.That(clonedBike.TopSpeed, Is.EqualTo(originalBike.TopSpeed));
	}

	[Test]
	public void Clone_BaseClassInstance_CreatesDeepCopy() {
		// Arrange - Direct Vehicle instance (not a derived type)
		var original = new VehicleCache {
			Id = 1,
			MyVehicle = new Vehicle { Brand = "Generic", Year = 2020 }
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.MyVehicle, Is.Not.SameAs(original.MyVehicle));
		Assert.That(cloned.MyVehicle.GetType(), Is.EqualTo(typeof(Vehicle)));

		Assert.That(cloned.MyVehicle.Brand, Is.EqualTo(original.MyVehicle.Brand));
		Assert.That(cloned.MyVehicle.Year, Is.EqualTo(original.MyVehicle.Year));
	}

	[Test]
	public void Clone_NullVehicle_HandlesNullCorrectly() {
		// Arrange
		var original = new VehicleCache { Id = 1, MyVehicle = null! };

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.MyVehicle, Is.Null);
	}

	[Test]
	public void Clone_ModifyClonedVehicle_DoesNotAffectOriginal() {
		// Arrange
		var original = new VehicleCache {
			Id = 1,
			MyVehicle = new Car { Brand = "Original", Year = 2020, Doors = 4, Model = "Model" }
		};

		// Act
		var cloned = original.Clone();
		var clonedCar = (Car)cloned.MyVehicle;
		clonedCar.Brand = "Modified";
		clonedCar.Year = 2099;
		clonedCar.Doors = 999;
		clonedCar.Model = "Changed";

		// Assert
		var originalCar = (Car)original.MyVehicle;
		Assert.That(originalCar.Brand, Is.EqualTo("Original"));
		Assert.That(originalCar.Year, Is.EqualTo(2020));
		Assert.That(originalCar.Doors, Is.EqualTo(4));
		Assert.That(originalCar.Model, Is.EqualTo("Model"));
	}

	[Test]
	public void Clone_PreservesRuntimeType() {
		// Arrange
		var original = new VehicleCache {
			Id = 1,
			MyVehicle = new RaceBike { Brand = "Ducati", Year = 2020, TopSpeed = 300 }
		};

		// Act
		var cloned = original.Clone();

		// Assert - Runtime type should be preserved exactly
		Assert.That(cloned.MyVehicle.GetType(), Is.EqualTo(typeof(RaceBike)));
		Assert.That(cloned.MyVehicle, Is.InstanceOf<Motorcycle>()); // RaceBike inherits from Motorcycle
		Assert.That(cloned.MyVehicle, Is.InstanceOf<RaceBike>());
		Assert.That(cloned.MyVehicle, Is.InstanceOf<Vehicle>()); // And from Vehicle
	}

	[Test]
	public void Clone_IntermediateNonSealedType_CreatesDeepCopy() {
		// Arrange - Direct Motorcycle instance (non-sealed intermediate class)
		var original = new VehicleCache {
			Id = 1,
			MyVehicle = new Motorcycle { Brand = "Harley", Year = 2020, HasSidecar = true }
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.MyVehicle, Is.Not.SameAs(original.MyVehicle));
		Assert.That(cloned.MyVehicle.GetType(), Is.EqualTo(typeof(Motorcycle)));

		var clonedMotorcycle = (Motorcycle)cloned.MyVehicle;
		var originalMotorcycle = (Motorcycle)original.MyVehicle;
		Assert.That(clonedMotorcycle.Brand, Is.EqualTo(originalMotorcycle.Brand));
		Assert.That(clonedMotorcycle.Year, Is.EqualTo(originalMotorcycle.Year));
		Assert.That(clonedMotorcycle.HasSidecar, Is.EqualTo(originalMotorcycle.HasSidecar));
	}
}