namespace Prague.Core.Tests.DataStructures;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class PooledSetPreHashedTests {
	[Test]
	public void PreHashedAddRemoveIsEquivalentToPublicPath() {
		var set = new PooledSet<int, DefaultKeyComparer<int>>();
		var comparer = default(DefaultKeyComparer<int>);
		for (var i = 0; i < 1000; i++)
			Assert.That(set.Add(i, comparer.GetHashCode(i)), Is.True);
		Assert.That(set.Count, Is.EqualTo(1000));
		for (var i = 0; i < 1000; i++)
			Assert.That(set.Contains(i), Is.True, $"missing {i}");
		Assert.That(set.Add(500, comparer.GetHashCode(500)), Is.False); // duplicate rejected
		for (var i = 0; i < 1000; i += 2)
			Assert.That(set.Remove(i, comparer.GetHashCode(i)), Is.True);
		Assert.That(set.Count, Is.EqualTo(500));
		for (var i = 1; i < 1000; i += 2)
			Assert.That(set.Contains(i), Is.True);
		set.Dispose();
	}
}
