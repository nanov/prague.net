namespace Prague.Core.Tests.Collections;

using Prague.Core.Collections;

// ValueDictionary is fixed-capacity: its working window is exactly the expected count while the rented
// arrays are usually longer, so an overrun would silently write into pool slack. Both insert paths must
// fail loudly in every build configuration: Debug trips the capacity Debug.Assert, Release the always-on
// guard. Same message either way, which is what these tests pin.
[TestFixture]
public class ValueDictionaryCapacityTests {
	[Test]
	public void Add_PastExpectedCount_Throws() {
		var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(true, 2);
		try {
			dict.Add(1, "a");
			dict.Add(2, "b");

			AssertCapacityGuardTrips(() => dict.Add(3, "c"));

			Assert.That(dict.Count, Is.EqualTo(2));
			Assert.That(dict.TryGetValue(3, out _), Is.False);
		} finally {
			dict.Dispose(withValues: true);
		}
	}

	[Test]
	public void GetValueRefOrAddDefault_PastExpectedCount_Throws() {
		var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(true, 2);
		try {
			dict.GetValueRefOrAddDefault(1, out _) = "a";
			dict.GetValueRefOrAddDefault(2, out _) = "b";

			// An existing key is a lookup, not an insert: the guard never trips on it.
			Assert.That(dict.GetValueRefOrAddDefault(2, out var exists), Is.EqualTo("b"));
			Assert.That(exists, Is.True);

			AssertCapacityGuardTrips(() => { dict.GetValueRefOrAddDefault(3, out _); });

			Assert.That(dict.Count, Is.EqualTo(2));
		} finally {
			dict.Dispose(withValues: true);
		}
	}

	[Test]
	public void FillingExactlyToExpectedCount_Succeeds() {
		var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(true, 3);
		try {
			dict.Add(1, "a");
			dict.GetValueRefOrAddDefault(2, out _) = "b";
			dict.Add(3, "c");

			Assert.That(dict.Count, Is.EqualTo(3));
			Assert.That(dict.TryGetValue(2, out var value), Is.True);
			Assert.That(value, Is.EqualTo("b"));
		} finally {
			dict.Dispose(withValues: true);
		}
	}

	private static void AssertCapacityGuardTrips(Action insert) =>
		Assert.That(Assert.Catch(insert)?.Message, Does.Contain("ValueDictionary capacity exceeded"));
}
