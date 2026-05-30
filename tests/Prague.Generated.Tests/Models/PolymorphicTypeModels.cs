namespace Prague.Generated.Tests.Models;

using Prague.Core;

// ========== Polymorphic Type Support Models ==========

// Test cache entity with polymorphic property
[DataCache]
public partial class PolymorphicCache : IDataCacheItem<int, PolymorphicCache> {
	[DataCacheKey] public int Id { get; set; }

	public BaseNode Node { get; set; }

	public int GetKey() {
		return Id;
	}
}

// Abstract base class for polymorphic types
public abstract class BaseNode {
	public string Name { get; set; } = string.Empty;
}

// Sealed derived type A (direct inheritance from BaseNode)
public sealed class NodeA : BaseNode {
	public int ValueA { get; set; }
}

// Sealed derived type B (direct inheritance from BaseNode)
public sealed class NodeB : BaseNode {
	public int ValueB { get; set; }
}

// Non-sealed intermediate class (for multi-level inheritance testing)
public class IntermediateNode : BaseNode {
	public string IntermediateProperty { get; set; } = string.Empty;
}

// Sealed derived type C (multi-level inheritance: NodeC -> IntermediateNode -> BaseNode)
public sealed class NodeC : IntermediateNode {
	public int ValueC { get; set; }
}

// ========== Interface Test Models ==========

// Interface for polymorphic types
public interface IItem {
	string Name { get; set; }
}

public sealed class ItemTypeA : IItem {
	public int ValueA { get; set; }
	public string Name { get; set; } = string.Empty;
}

public sealed class ItemTypeB : IItem {
	public string ValueB { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
}

// Cache with interface property
[DataCache]
public partial class InterfaceCache : IDataCacheItem<int, InterfaceCache> {
	[DataCacheKey] public int Id { get; set; }

	public IItem Item { get; set; }

	public int GetKey() {
		return Id;
	}
}

// ========== Non-Abstract Base Class Test Models ==========

// Non-abstract, non-sealed base class for polymorphic types
public class Vehicle {
	public string Brand { get; set; } = string.Empty;
	public int Year { get; set; }
}

// Sealed derived type Car
public sealed class Car : Vehicle {
	public int Doors { get; set; }
	public string Model { get; set; } = string.Empty;
}

// Sealed derived type Truck
public sealed class Truck : Vehicle {
	public int PayloadCapacity { get; set; }
	public bool HasTrailer { get; set; }
}

// Non-sealed intermediate class (multi-level with non-abstract base)
public class Motorcycle : Vehicle {
	public bool HasSidecar { get; set; }
}

// Sealed derived from Motorcycle
public sealed class RaceBike : Motorcycle {
	public int TopSpeed { get; set; }
}

// Cache with non-abstract base class property
[DataCache]
public partial class VehicleCache : IDataCacheItem<int, VehicleCache> {
	[DataCacheKey] public int Id { get; set; }

	public Vehicle MyVehicle { get; set; }

	public int GetKey() {
		return Id;
	}
}

/*
// This should fail with CACHE003: interface type in property
[DataCache]
public sealed partial class ContainerWithInterface
{
	[DataCacheKey]
	public int Id { get; set; }

	// ERROR: IItem is interface (abstract)
	public IItem Item { get; set; }
}
*/
/*
// This should also fail even when trying to cache the abstract type itself
[DataCache]
public abstract class AbstractCacheEntity
{
	[DataCacheKey]
	public int Id { get; set; }

	public string Name { get; set; }
}
*/
/*
// Nested abstract type should also fail
[DataCache]
public sealed class NestedContainer
{
	[DataCacheKey]
	public int Id { get; set; }

	// This contains AbstractItem property, which is abstract
	public ContainerWithAbstractProperty Container { get; set; }
}
*/