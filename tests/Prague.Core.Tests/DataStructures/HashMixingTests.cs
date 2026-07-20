namespace Prague.Core.Tests.DataStructures;

	using Prague.Core.Collections;
	using Prague.Core.Utils;

[TestFixture]
public class HashMixingTests {
	[Test]
	public void UnmixInvertsMixOnBoundaryValues() {
		int[] values = [0, 1, -1, 2, 3, int.MaxValue, int.MinValue, 12345678, unchecked((int)0xDEADBEEF)];
		foreach (var v in values)
			Assert.That(HashMixing.Unmix(HashMixing.Mix(v)), Is.EqualTo(v));
	}

	[Test]
	public void UnmixInvertsMixOnRandomSweep() {
		var rng = new Random(42);
		for (var i = 0; i < 100_000; i++) {
			var v = rng.Next(int.MinValue, int.MaxValue);
			if (HashMixing.Unmix(HashMixing.Mix(v)) != v)
				Assert.Fail($"roundtrip failed for {v}");
		}
		Assert.Pass();
	}

	[Test]
	public void MixMatchesDefaultKeyComparerValueTypePath() {
		var comparer = default(DefaultKeyComparer<int>);
		int[] values = [0, 1, -1, 42, int.MaxValue, int.MinValue];
		foreach (var v in values)
			Assert.That(comparer.GetHashCode(v), Is.EqualTo(HashMixing.Mix(v.GetHashCode())));
	}
}
