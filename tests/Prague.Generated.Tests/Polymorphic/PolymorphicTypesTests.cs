namespace Prague.Generated.Tests.Polymorphic;
using Prague.Generated.Tests.Models;

using NUnit.Framework;

[TestFixture]
public class PolymorphicTypesTests {
	[Test]
	public void CacheEquals_DifferentSealedTypes_ReturnsFalse() {
		// Arrange
		var cacheWithNodeA = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 10 } };
		var cacheWithNodeB = new PolymorphicCache { Id = 1, Node = new NodeB { Name = "Test", ValueB = 10 } };

		// Act & Assert
		Assert.That(cacheWithNodeA.CacheEquals(cacheWithNodeB), Is.False);
	}

	[Test]
	public void CacheEquals_SameSealedType_SameValues_ReturnsTrue() {
		// Arrange
		var cache1 = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 10 } };
		var cache2 = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 10 } };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.True);
	}

	[Test]
	public void CacheEquals_SameSealedType_DifferentValues_ReturnsFalse() {
		// Arrange
		var cache1 = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 10 } };
		var cache2 = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 20 } };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.False);
	}

	[Test]
	public void CacheEquals_MultiLevelInheritance_DifferentTypes_ReturnsFalse() {
		// Arrange
		var cacheWithNodeA = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 10 } };
		var cacheWithNodeC = new PolymorphicCache { Id = 1, Node = new NodeC { Name = "Test", ValueC = 10 } };

		// Act & Assert
		Assert.That(cacheWithNodeA.CacheEquals(cacheWithNodeC), Is.False);
	}

	[Test]
	public void CacheEquals_MultiLevelInheritance_SameType_ReturnsTrue() {
		// Arrange
		var cache1 = new PolymorphicCache {
			Id = 1,
			Node = new NodeC {
				Name = "Test",
				IntermediateProperty = "Intermediate",
				ValueC = 10
			}
		};
		var cache2 = new PolymorphicCache {
			Id = 1,
			Node = new NodeC {
				Name = "Test",
				IntermediateProperty = "Intermediate",
				ValueC = 10
			}
		};

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.True);
	}

	[Test]
	public void CacheEquals_InheritedProperty_Checked() {
		// Arrange - Same type but different inherited Name property
		var cache1 = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Name1", ValueA = 10 } };
		var cache2 = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Name2", ValueA = 10 } };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.False);
	}

	[Test]
	public void CacheEquals_NullNodes_ReturnsTrue() {
		// Arrange
		var cache1 = new PolymorphicCache { Id = 1, Node = null! };
		var cache2 = new PolymorphicCache { Id = 1, Node = null! };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.True);
	}

	[Test]
	public void CacheEquals_OneNullNode_ReturnsFalse() {
		// Arrange
		var cache1 = new PolymorphicCache { Id = 1, Node = new NodeA { Name = "Test", ValueA = 10 } };
		var cache2 = new PolymorphicCache { Id = 1, Node = null! };

		// Act & Assert
		Assert.That(cache1.CacheEquals(cache2), Is.False);
	}

	// ========== Clone Tests ==========

	[Test]
	public void Clone_SealedTypeNodeA_CreatesDeepCopy() {
		// Arrange
		var original = new PolymorphicCache {
			Id = 1,
			Node = new NodeA { Name = "Test", ValueA = 10 }
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.Node, Is.Not.SameAs(original.Node));
		Assert.That(cloned.Node, Is.InstanceOf<NodeA>());

		var clonedNodeA = (NodeA)cloned.Node;
		var originalNodeA = (NodeA)original.Node;
		Assert.That(clonedNodeA.Name, Is.EqualTo(originalNodeA.Name));
		Assert.That(clonedNodeA.ValueA, Is.EqualTo(originalNodeA.ValueA));
	}

	[Test]
	public void Clone_SealedTypeNodeB_CreatesDeepCopy() {
		// Arrange
		var original = new PolymorphicCache {
			Id = 1,
			Node = new NodeB { Name = "Test", ValueB = 20 }
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.Node, Is.Not.SameAs(original.Node));
		Assert.That(cloned.Node, Is.InstanceOf<NodeB>());

		var clonedNodeB = (NodeB)cloned.Node;
		var originalNodeB = (NodeB)original.Node;
		Assert.That(clonedNodeB.Name, Is.EqualTo(originalNodeB.Name));
		Assert.That(clonedNodeB.ValueB, Is.EqualTo(originalNodeB.ValueB));
	}

	[Test]
	public void Clone_MultiLevelInheritance_CreatesDeepCopy() {
		// Arrange
		var original = new PolymorphicCache {
			Id = 1,
			Node = new NodeC {
				Name = "Test",
				IntermediateProperty = "Intermediate",
				ValueC = 30
			}
		};

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.Node, Is.Not.SameAs(original.Node));
		Assert.That(cloned.Node, Is.InstanceOf<NodeC>());

		var clonedNodeC = (NodeC)cloned.Node;
		var originalNodeC = (NodeC)original.Node;
		Assert.That(clonedNodeC.Name, Is.EqualTo(originalNodeC.Name));
		Assert.That(clonedNodeC.IntermediateProperty, Is.EqualTo(originalNodeC.IntermediateProperty));
		Assert.That(clonedNodeC.ValueC, Is.EqualTo(originalNodeC.ValueC));
	}

	[Test]
	public void Clone_NullNode_HandlesNullCorrectly() {
		// Arrange
		var original = new PolymorphicCache { Id = 1, Node = null! };

		// Act
		var cloned = original.Clone();

		// Assert
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.Node, Is.Null);
	}

	[Test]
	public void Clone_ModifyClonedNode_DoesNotAffectOriginal() {
		// Arrange
		var original = new PolymorphicCache {
			Id = 1,
			Node = new NodeA { Name = "Original", ValueA = 10 }
		};

		// Act
		var cloned = original.Clone();
		var clonedNodeA = (NodeA)cloned.Node;
		clonedNodeA.Name = "Modified";
		clonedNodeA.ValueA = 999;

		// Assert
		var originalNodeA = (NodeA)original.Node;
		Assert.That(originalNodeA.Name, Is.EqualTo("Original"));
		Assert.That(originalNodeA.ValueA, Is.EqualTo(10));
	}

	[Test]
	public void Clone_PreservesRuntimeType() {
		// Arrange
		var original = new PolymorphicCache {
			Id = 1,
			Node = new NodeC { Name = "Test", ValueC = 30 }
		};

		// Act
		var cloned = original.Clone();

		// Assert - Runtime type should be preserved exactly
		Assert.That(cloned.Node.GetType(), Is.EqualTo(typeof(NodeC)));
		Assert.That(cloned.Node, Is.InstanceOf<IntermediateNode>()); // NodeC inherits from IntermediateNode
		Assert.That(cloned.Node, Is.InstanceOf<NodeC>());
		Assert.That(cloned.Node, Is.InstanceOf<BaseNode>()); // And from BaseNode
	}

	[Test]
	public void Interface_Clone() {
		// Arrange
		var original = new LiveObjectCacheItem {
			LiveObject = new GaugeTelemetry()
		}; // new LiveObjectWithInterfaceCache() { };


		// Act
		var cloned = original.Clone();
		Assert.That(cloned, Is.Not.SameAs(original));
		Assert.That(cloned.CacheEquals(original), Is.True);
		;
	}
}