namespace Prague.Core.Tests.Collections;

using Prague.Core.Collections;
using NUnit.Framework;

/// <summary>
///   Seek keys are built with <c>default!</c> halves ("from the very start of this prefix"),
///   so every component can legitimately be null for reference-typed components — in any
///   position, on either side of the compare, since the tree's searches use both the stored
///   slot and the caller's bound as the receiver. CompareTo and Equals must both tolerate
///   that AND agree with each other.
/// </summary>
[TestFixture]
public class CompoundKeyTests {
	private static readonly string?[] Components = [null, "a", "b"];

	private static CompoundKey<string, string, string> Key3(string? prefix, string? sort, string? key) =>
		new(prefix!, sort!, key!);

	private static CompoundKey<string, string, string, string> Key4(string? p1, string? p2, string? sort, string? key) =>
		new(p1!, p2!, sort!, key!);

	// ───────────────────── 3-component ─────────────────────

	[Test]
	public void Three_CompareTo_NullPrefix_SortsBeforeNonNull() {
		var seek = Key3(null, "s", "k");
		var stored = Key3("a", "s", "k");

		Assert.That(seek.CompareTo(stored), Is.LessThan(0));
		Assert.That(stored.CompareTo(seek), Is.GreaterThan(0));
	}

	[Test]
	public void Three_CompareTo_NullSort_SortsBeforeNonNull() {
		var seek = Key3("a", null, "k");
		var stored = Key3("a", "s", "k");

		Assert.That(seek.CompareTo(stored), Is.LessThan(0));
		Assert.That(stored.CompareTo(seek), Is.GreaterThan(0));
	}

	[Test]
	public void Three_CompareTo_NullKey_SortsBeforeNonNull() {
		var seek = Key3("a", "s", null);
		var stored = Key3("a", "s", "k");

		Assert.That(seek.CompareTo(stored), Is.LessThan(0));
		Assert.That(stored.CompareTo(seek), Is.GreaterThan(0));
	}

	[Test]
	public void Three_CompareTo_AllComponentsNull_IsZero() {
		var a = Key3(null, null, null);
		var b = Key3(null, null, null);

		Assert.That(a.CompareTo(b), Is.Zero);
	}

	[Test]
	public void Three_Equals_NullKeyOnBothSides_IsTrueWithoutThrowing() {
		var a = Key3("a", "s", null);
		var b = Key3("a", "s", null);

		Assert.That(a.Equals(b), Is.True);
		Assert.That(a.CompareTo(b), Is.Zero);
	}

	[Test]
	public void Three_Equals_NullKeyOnOneSide_IsFalseWithoutThrowing() {
		var a = Key3("a", "s", null);
		var b = Key3("a", "s", "k");

		Assert.That(a.Equals(b), Is.False);
		Assert.That(b.Equals(a), Is.False);
	}

	[Test]
	public void Three_Equals_NullPrefixOrSort_DoesNotThrow() {
		Assert.That(Key3(null, "s", "k").Equals(Key3(null, "s", "k")), Is.True);
		Assert.That(Key3("a", null, "k").Equals(Key3("a", null, "k")), Is.True);
		Assert.That(Key3(null, null, null).Equals(Key3(null, null, null)), Is.True);
		Assert.That(Key3(null, "s", "k").Equals(Key3("a", "s", "k")), Is.False);
		Assert.That(Key3("a", null, "k").Equals(Key3("a", "s", "k")), Is.False);
	}

	[Test]
	public void Three_CompareToZero_AgreesWithEquals_AcrossEveryNullCombination() {
		var keys = new List<CompoundKey<string, string, string>>();
		foreach (var p in Components)
			foreach (var s in Components)
				foreach (var k in Components)
					keys.Add(Key3(p, s, k));

		foreach (var x in keys)
			foreach (var y in keys)
				Assert.That(x.CompareTo(y) == 0, Is.EqualTo(x.Equals(y)),
					$"CompareTo/Equals disagree for {x} vs {y}");
	}

	[Test]
	public void Three_GetHashCode_MatchesForEqualNullBearingKeys() {
		var allNullA = Key3(null, null, null);
		var allNullB = Key3(null, null, null);
		var partialA = Key3("a", null, null);
		var partialB = Key3("a", null, null);

		Assert.That(allNullA.GetHashCode(), Is.EqualTo(allNullB.GetHashCode()));
		Assert.That(partialA.GetHashCode(), Is.EqualTo(partialB.GetHashCode()));
	}

	// ───────────────────── 4-component ─────────────────────

	[Test]
	public void Four_CompareTo_NullPrefix1_SortsBeforeNonNull() {
		Assert.That(Key4(null, "p2", "s", "k").CompareTo(Key4("p1", "p2", "s", "k")), Is.LessThan(0));
		Assert.That(Key4("p1", "p2", "s", "k").CompareTo(Key4(null, "p2", "s", "k")), Is.GreaterThan(0));
	}

	[Test]
	public void Four_CompareTo_NullPrefix2_SortsBeforeNonNull() {
		Assert.That(Key4("p1", null, "s", "k").CompareTo(Key4("p1", "p2", "s", "k")), Is.LessThan(0));
		Assert.That(Key4("p1", "p2", "s", "k").CompareTo(Key4("p1", null, "s", "k")), Is.GreaterThan(0));
	}

	[Test]
	public void Four_CompareTo_NullSort_SortsBeforeNonNull() {
		Assert.That(Key4("p1", "p2", null, "k").CompareTo(Key4("p1", "p2", "s", "k")), Is.LessThan(0));
		Assert.That(Key4("p1", "p2", "s", "k").CompareTo(Key4("p1", "p2", null, "k")), Is.GreaterThan(0));
	}

	[Test]
	public void Four_CompareTo_NullKey_SortsBeforeNonNull() {
		Assert.That(Key4("p1", "p2", "s", null).CompareTo(Key4("p1", "p2", "s", "k")), Is.LessThan(0));
		Assert.That(Key4("p1", "p2", "s", "k").CompareTo(Key4("p1", "p2", "s", null)), Is.GreaterThan(0));
	}

	[Test]
	public void Four_CompareTo_AllComponentsNull_IsZero() {
		Assert.That(Key4(null, null, null, null).CompareTo(Key4(null, null, null, null)), Is.Zero);
	}

	[Test]
	public void Four_Equals_NullComponents_DoNotThrow() {
		Assert.That(Key4(null, null, null, null).Equals(Key4(null, null, null, null)), Is.True);
		Assert.That(Key4("p1", "p2", "s", null).Equals(Key4("p1", "p2", "s", null)), Is.True);
		Assert.That(Key4("p1", "p2", "s", null).Equals(Key4("p1", "p2", "s", "k")), Is.False);
		Assert.That(Key4("p1", "p2", "s", "k").Equals(Key4("p1", "p2", "s", null)), Is.False);
	}

	[Test]
	public void Four_CompareToZero_AgreesWithEquals_AcrossEveryNullCombination() {
		var keys = new List<CompoundKey<string, string, string, string>>();
		foreach (var p1 in Components)
			foreach (var p2 in Components)
				foreach (var s in Components)
					foreach (var k in Components)
						keys.Add(Key4(p1, p2, s, k));

		foreach (var x in keys)
			foreach (var y in keys)
				Assert.That(x.CompareTo(y) == 0, Is.EqualTo(x.Equals(y)),
					$"CompareTo/Equals disagree for {x} vs {y}");
	}

	[Test]
	public void Four_GetHashCode_MatchesForEqualNullBearingKeys() {
		var allNullA = Key4(null, null, null, null);
		var allNullB = Key4(null, null, null, null);
		var partialA = Key4("p1", null, "s", null);
		var partialB = Key4("p1", null, "s", null);

		Assert.That(allNullA.GetHashCode(), Is.EqualTo(allNullB.GetHashCode()));
		Assert.That(partialA.GetHashCode(), Is.EqualTo(partialB.GetHashCode()));
	}

	// ───────────────────── Boxed Equals(object) ─────────────────────

	[Test]
	public void Equals_Object_NullBearingKeys_DoesNotThrow() {
		Assert.That(Key3(null, null, null).Equals((object)Key3(null, null, null)), Is.True);
		Assert.That(Key4(null, null, null, null).Equals((object)Key4(null, null, null, null)), Is.True);
	}
}
