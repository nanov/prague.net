namespace Prague.Generated.Tests.Cache;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class PooledBTreePreHashedTests {
	[Test]
	public void PreHashedMutationsMatchPublicOnes() {
		using var viaPublic = new PooledBTree<long, int>();
		using var viaPreHashed = new PooledBTree<long, int>();
		var comparer = default(DefaultKeyComparer<int>);

		// key = i % 100 → heavy duplicate runs → composite (key, hash) mode
		for (var i = 0; i < 10_000; i++) {
			Assert.That(viaPublic.Add(i % 100, i), Is.True);
			Assert.That(viaPreHashed.Add(i % 100, i, comparer.GetHashCode(i)), Is.True);
		}
		Assert.That(viaPreHashed.Length, Is.EqualTo(viaPublic.Length));

		// duplicate rejected identically
		Assert.That(viaPreHashed.Add(5, 5, comparer.GetHashCode(5)), Is.False);

		for (var i = 0; i < 10_000; i += 3) {
			Assert.That(viaPublic.Remove(i % 100, i), Is.True);
			Assert.That(viaPreHashed.Remove(i % 100, i, comparer.GetHashCode(i)), Is.True);
		}
		Assert.That(viaPreHashed.Length, Is.EqualTo(viaPublic.Length));

		// move a pair between keys
		Assert.That(viaPreHashed.Update(5, 999, 5, comparer.GetHashCode(5)), Is.True);
		Assert.That(viaPreHashed.Contains(999, 5), Is.True);
		Assert.That(viaPreHashed.Contains(5, 5), Is.False);
	}

	[Test]
	public void PreHashedStringValuesUseThreadedMarvinHash() {
		using var tree = new PooledBTree<int, string>();
		var comparer = default(DefaultKeyComparer<string>);
		for (var i = 0; i < 1000; i++) {
			var v = $"value-{i:D8}";
			Assert.That(tree.Add(i % 10, v, comparer.GetHashCode(v)), Is.True);
		}
		Assert.That(tree.Length, Is.EqualTo(1000));
		for (var i = 0; i < 1000; i += 2) {
			var v = $"value-{i:D8}";
			Assert.That(tree.Remove(i % 10, v, comparer.GetHashCode(v)), Is.True);
		}
		Assert.That(tree.Length, Is.EqualTo(500));
	}
}
