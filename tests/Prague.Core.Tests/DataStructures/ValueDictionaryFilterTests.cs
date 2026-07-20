namespace Prague.Core.Tests.DataStructures;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class ValueDictionaryFilterTests {
	private readonly struct DropEvensAndVerifyPairing : ValueDictionary<int, string, DefaultKeyComparer<int>>.IValueDictionaryFilter<int, string> {
		public bool Keep(int key, ref string value) {
			// The value handed in must be THE value stored under `key` — after a drop has
			// occurred, a wrong-cursor implementation hands us a stale/duplicate row here.
			Assert.That(value, Is.EqualTo($"v{key}"), "filter saw a value that does not belong to its key");
			return key % 2 != 0;
		}
	}

	[Test]
	public void Filter_AfterDrops_PredicateAlwaysSeesTheValueBelongingToItsKey() {
		var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 16);
		for (var i = 0; i < 10; i++)
			dict.Add(i, $"v{i}");

		dict.Filter(new DropEvensAndVerifyPairing());

		Assert.That(dict.Count, Is.EqualTo(5));
		foreach (var key in dict.Keys.ToArray())
			Assert.That(key % 2, Is.Not.Zero);
		dict.Dispose(true);
	}
}
