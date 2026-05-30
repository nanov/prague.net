namespace Prague.Generated.Tests.Polymorphic;
using Prague.Generated.Tests.Models;

using NUnit.Framework;

[TestFixture]
public class InterfacePolymorphicTypesTests {
	// ========== Equality Tests ==========

	[Test]
	public void CacheEquals_DifferentInterfaceImplementations_ReturnsFalse() {
		// Arrange
		var cacheWithTypeA = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Test", ValueA = 10 } };
		var cacheWithTypeB = new InterfaceCache { Id = 1, Item = new ItemTypeB { Name = "Test", ValueB = "10" } };

		// Act & Assert
		Assert.That(cacheWithTypeA.CacheEquals(cacheWithTypeB), Is.False);
	}

	[Test]
	public void CacheEquals_SameInterfaceImplementation_SameValues_ReturnsTrue() {
		// Arrange
		var cache1 = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Test", ValueA = 10 } };
		var cache2 = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Test", ValueA = 10 } };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.True);
	}

	[Test]
	public void CacheEquals_SameInterfaceImplementation_DifferentValues_ReturnsFalse() {
		// Arrange
		var cache1 = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Test", ValueA = 10 } };
		var cache2 = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Test", ValueA = 20 } };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.False);
	}

	[Test]
	public void CacheEquals_InterfaceProperty_Checked() {
		// Arrange - Same implementation but different Name property
		var cache1 = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Name1", ValueA = 10 } };
		var cache2 = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Name2", ValueA = 10 } };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.False);
	}

	[Test]
	public void CacheEquals_NullInterfaceItems_ReturnsTrue() {
		// Arrange
		var cache1 = new InterfaceCache { Id = 1, Item = null! };
		var cache2 = new InterfaceCache { Id = 1, Item = null! };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.True);
	}

	[Test]
	public void CacheEquals_OneNullInterfaceItem_ReturnsFalse() {
		// Arrange
		var cache1 = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Test", ValueA = 10 } };
		var cache2 = new InterfaceCache { Id = 1, Item = null! };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.False);
	}

	// ========== Clone Tests ==========

	[Test]
	public void Clone_InterfaceTypeA_CreatesDeepCopy() {
		// Arrange
		var original = new InterfaceCache {
			Id = 1,
			Item = new ItemTypeA { Name = "Test", ValueA = 10 }
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.Item, Is.Not.SameAs(original.Item));
		Assert.That(cloned.Item, Is.InstanceOf<ItemTypeA>());

		var clonedItem = (ItemTypeA)cloned.Item;
		var originalItem = (ItemTypeA)original.Item;
		Assert.That(clonedItem.Name, Is.EqualTo(originalItem.Name));
		Assert.That(clonedItem.ValueA, Is.EqualTo(originalItem.ValueA));
	}

	[Test]
	public void Clone_InterfaceTypeB_CreatesDeepCopy() {
		// Arrange
		var original = new InterfaceCache {
			Id = 1,
			Item = new ItemTypeB { Name = "Test", ValueB = "Data" }
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.Item, Is.Not.SameAs(original.Item));
		Assert.That(cloned.Item, Is.InstanceOf<ItemTypeB>());

		var clonedItem = (ItemTypeB)cloned.Item;
		var originalItem = (ItemTypeB)original.Item;
		Assert.That(clonedItem.Name, Is.EqualTo(originalItem.Name));
		Assert.That(clonedItem.ValueB, Is.EqualTo(originalItem.ValueB));
	}

	[Test]
	public void Clone_NullInterfaceItem_HandlesNullCorrectly() {
		// Arrange
		var original = new InterfaceCache { Id = 1, Item = null! };

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.Item, Is.Null);
	}

	[Test]
	public void Clone_ModifyClonedInterfaceItem_DoesNotAffectOriginal() {
		// Arrange
		var original = new InterfaceCache {
			Id = 1,
			Item = new ItemTypeA { Name = "Original", ValueA = 10 }
		};

		// Act
		var cloned = original.Clone();
		var clonedItem = (ItemTypeA)cloned.Item;
		clonedItem.Name = "Modified";
		clonedItem.ValueA = 999;

		// Assert
		var originalItem = (ItemTypeA)original.Item;
		Assert.That(originalItem.Name, Is.EqualTo("Original"));
		Assert.That(originalItem.ValueA, Is.EqualTo(10));
	}

	[Test]
	public void Clone_PreservesConcreteTypeOfInterfaceImplementation() {
		// Arrange
		var original = new InterfaceCache {
			Id = 1,
			Item = new ItemTypeA { Name = "Test", ValueA = 30 }
		};

		// Act
		var cloned = original.Clone();

		// Assert - Runtime type should be preserved exactly
		Assert.That(cloned.Item.GetType(), Is.EqualTo(typeof(ItemTypeA)));
		Assert.That(cloned.Item, Is.InstanceOf<ItemTypeA>());
		Assert.That(cloned.Item, Is.InstanceOf<IItem>());
	}

	// ========== Mixed Tests (Interface + Abstract) ==========

	[Test]
	public void CacheEquals_MixedPolymorphicTypes_WorksCorrectly() {
		// Arrange - Testing that both BaseNode and IItem polymorphism work together
		var abstractCache1 = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 10 } };
		var abstractCache2 = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 10 } };

		var interfaceCache1 = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Test", ValueA = 10 } };
		var interfaceCache2 = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Test", ValueA = 10 } };

		// Act & Assert
		Assert.That(abstractCache1.CacheEquals(abstractCache2), Is.True);
		Assert.That(interfaceCache1.CacheEquals(interfaceCache2), Is.True);
	}

	[Test]
	public void Clone_MixedPolymorphicTypes_WorksCorrectly() {
		// Arrange - Testing that both BaseNode and IItem polymorphism work together
		var abstractCache = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 10 } };
		var interfaceCache = new InterfaceCache { Id = 1, Item = new ItemTypeA { Name = "Test", ValueA = 10 } };

		// Act
		var clonedAbstract = abstractCache.Clone();
		var clonedInterface = interfaceCache.Clone();

		// Assert
		Assert.That(clonedAbstract.Node, Is.Not.SameAs(abstractCache.Node));
		Assert.That(clonedAbstract.Node, Is.InstanceOf<NodeA>());

		Assert.That(clonedInterface.Item, Is.Not.SameAs(interfaceCache.Item));
		Assert.That(clonedInterface.Item, Is.InstanceOf<ItemTypeA>());
	}
}