namespace Prague.Core.Tests.Cache;

using Prague.Core;
using NUnit.Framework;

/// <summary>
///   Tests for CacheEquatable&lt;T&gt; and CacheComparable&lt;T&gt; wrapper structs.
/// </summary>
[TestFixture]
public class CacheEquatableComparableTests {
	[Test]
	public void CacheEquatable_Constructor_SetsValue() {
		var equatable = new CacheEquatable<int>(42);
		Assert.That(equatable.Value, Is.EqualTo(42));
	}

	[Test]
	public void CacheEquatable_CacheEquals_ReturnsTrueForEqualValues() {
		var a = new CacheEquatable<string>("test");
		var b = new CacheEquatable<string>("test");
		Assert.That(a.CacheEquals(b), Is.True);
	}

	[Test]
	public void CacheEquatable_CacheEquals_ReturnsFalseForDifferentValues() {
		var a = new CacheEquatable<string>("test1");
		var b = new CacheEquatable<string>("test2");
		Assert.That(a.CacheEquals(b), Is.False);
	}

	[Test]
	public void CacheEquatable_CacheGetHashCode_ReturnsValueHashCode() {
		var value = "test";
		var equatable = new CacheEquatable<string>(value);
		Assert.That(equatable.CacheGetHashCode(), Is.EqualTo(value.GetHashCode()));
	}

	[Test]
	public void CacheEquatable_CacheGetHashCode_ReturnsZeroForNull() {
		var equatable = new CacheEquatable<string?>(null);
		Assert.That(equatable.CacheGetHashCode(), Is.EqualTo(0));
	}

	[Test]
	public void CacheEquatable_Equals_ReturnsTrueForEqualValues() {
		var a = new CacheEquatable<int>(100);
		var b = new CacheEquatable<int>(100);
		Assert.That(a.Equals(b), Is.True);
	}

	[Test]
	public void CacheEquatable_Clone_ReturnsSameValue() {
		var original = new CacheEquatable<int>(42);
		var cloned = original.Clone();
		Assert.That(cloned.Value, Is.EqualTo(original.Value));
	}

	[Test]
	public void CacheEquatable_EqualsObject_ReturnsTrueForEqualCacheEquatable() {
		var a = new CacheEquatable<int>(42);
		object b = new CacheEquatable<int>(42);
		Assert.That(a.Equals(b), Is.True);
	}

	[Test]
	public void CacheEquatable_EqualsObject_ReturnsFalseForNonCacheEquatable() {
		var a = new CacheEquatable<int>(42);
		object b = 42;
		Assert.That(a.Equals(b), Is.False);
	}

	[Test]
	public void CacheEquatable_EqualsObject_ReturnsFalseForNull() {
		var a = new CacheEquatable<int>(42);
		Assert.That(a.Equals(null), Is.False);
	}

	[Test]
	public void CacheEquatable_GetHashCode_ReturnsCacheGetHashCode() {
		var equatable = new CacheEquatable<string>("test");
		Assert.That(equatable.GetHashCode(), Is.EqualTo(equatable.CacheGetHashCode()));
	}

	[Test]
	public void CacheEquatable_CompareTo_ReturnsZeroForEqual() {
		var a = new CacheEquatable<int>(42);
		var b = new CacheEquatable<int>(42);
		Assert.That(a.CompareTo(b), Is.EqualTo(0));
	}

	[Test]
	public void CacheEquatable_CompareTo_ReturnsNegativeForLesser() {
		var a = new CacheEquatable<int>(10);
		var b = new CacheEquatable<int>(20);
		Assert.That(a.CompareTo(b), Is.LessThan(0));
	}

	[Test]
	public void CacheEquatable_CompareTo_ReturnsPositiveForGreater() {
		var a = new CacheEquatable<int>(20);
		var b = new CacheEquatable<int>(10);
		Assert.That(a.CompareTo(b), Is.GreaterThan(0));
	}

	[Test]
	public void CacheEquatable_CompareToObject_ReturnsPositiveForNull() {
		var a = new CacheEquatable<int>(42);
		Assert.That(a.CompareTo(null), Is.EqualTo(1));
	}

	[Test]
	public void CacheEquatable_CompareToObject_ComparesWithCacheEquatable() {
		var a = new CacheEquatable<int>(42);
		object b = new CacheEquatable<int>(42);
		Assert.That(a.CompareTo(b), Is.EqualTo(0));
	}

	[Test]
	public void CacheEquatable_CompareToObject_ThrowsForInvalidType() {
		var a = new CacheEquatable<int>(42);
		Assert.Throws<ArgumentException>(() => a.CompareTo("invalid"));
	}

	[Test]
	public void CacheEquatable_ImplicitConversionFromT_CreatesEquatable() {
		CacheEquatable<int> equatable = 42;
		Assert.That(equatable.Value, Is.EqualTo(42));
	}

	[Test]
	public void CacheEquatable_ImplicitConversionToT_ReturnsValue() {
		var equatable = new CacheEquatable<int>(42);
		int value = equatable;
		Assert.That(value, Is.EqualTo(42));
	}

	[Test]
	public void CacheEquatable_ToString_ReturnsValueString() {
		var equatable = new CacheEquatable<int>(42);
		Assert.That(equatable.ToString(), Is.EqualTo("42"));
	}

	[Test]
	public void CacheEquatable_ToString_ReturnsEmptyForNull() {
		var equatable = new CacheEquatable<string?>(null);
		Assert.That(equatable.ToString(), Is.EqualTo(string.Empty));
	}

	[Test]
	public void CacheComparable_Constructor_SetsValue() {
		var comparable = new CacheComparable<int>(42);
		Assert.That(comparable.Value, Is.EqualTo(42));
	}

	[Test]
	public void CacheComparable_CacheEquals_ReturnsTrueForEqualValues() {
		var a = new CacheComparable<int>(42);
		var b = new CacheComparable<int>(42);
		Assert.That(a.CacheEquals(b), Is.True);
	}

	[Test]
	public void CacheComparable_CacheEquals_ReturnsFalseForDifferentValues() {
		var a = new CacheComparable<int>(42);
		var b = new CacheComparable<int>(43);
		Assert.That(a.CacheEquals(b), Is.False);
	}

	[Test]
	public void CacheComparable_CacheGetHashCode_ReturnsValueHashCode() {
		var value = 42;
		var comparable = new CacheComparable<int>(value);
		Assert.That(comparable.CacheGetHashCode(), Is.EqualTo(value.GetHashCode()));
	}

	[Test]
	public void CacheComparable_Equals_ReturnsTrueForEqualValues() {
		var a = new CacheComparable<int>(100);
		var b = new CacheComparable<int>(100);
		Assert.That(a.Equals(b), Is.True);
	}

	[Test]
	public void CacheComparable_EqualsObject_ReturnsTrueForEqualCacheComparable() {
		var a = new CacheComparable<int>(42);
		object b = new CacheComparable<int>(42);
		Assert.That(a.Equals(b), Is.True);
	}

	[Test]
	public void CacheComparable_EqualsObject_ReturnsFalseForNonCacheComparable() {
		var a = new CacheComparable<int>(42);
		object b = 42;
		Assert.That(a.Equals(b), Is.False);
	}

	[Test]
	public void CacheComparable_EqualsObject_ReturnsFalseForNull() {
		var a = new CacheComparable<int>(42);
		Assert.That(a.Equals(null), Is.False);
	}

	[Test]
	public void CacheComparable_GetHashCode_ReturnsCacheGetHashCode() {
		var comparable = new CacheComparable<int>(42);
		Assert.That(comparable.GetHashCode(), Is.EqualTo(comparable.CacheGetHashCode()));
	}

	[Test]
	public void CacheComparable_CompareTo_ReturnsZeroForEqual() {
		var a = new CacheComparable<int>(42);
		var b = new CacheComparable<int>(42);
		Assert.That(a.CompareTo(b), Is.EqualTo(0));
	}

	[Test]
	public void CacheComparable_CompareTo_ReturnsNegativeForLesser() {
		var a = new CacheComparable<int>(10);
		var b = new CacheComparable<int>(20);
		Assert.That(a.CompareTo(b), Is.LessThan(0));
	}

	[Test]
	public void CacheComparable_CompareTo_ReturnsPositiveForGreater() {
		var a = new CacheComparable<int>(20);
		var b = new CacheComparable<int>(10);
		Assert.That(a.CompareTo(b), Is.GreaterThan(0));
	}

	[Test]
	public void CacheComparable_CompareToObject_ReturnsPositiveForNull() {
		var a = new CacheComparable<int>(42);
		Assert.That(a.CompareTo(null), Is.EqualTo(1));
	}

	[Test]
	public void CacheComparable_CompareToObject_ComparesWithCacheComparable() {
		var a = new CacheComparable<int>(42);
		object b = new CacheComparable<int>(42);
		Assert.That(a.CompareTo(b), Is.EqualTo(0));
	}

	[Test]
	public void CacheComparable_CompareToObject_ThrowsForInvalidType() {
		var a = new CacheComparable<int>(42);
		Assert.Throws<ArgumentException>(() => a.CompareTo("invalid"));
	}

	[Test]
	public void CacheComparable_ImplicitConversionFromT_CreatesComparable() {
		CacheComparable<int> comparable = 42;
		Assert.That(comparable.Value, Is.EqualTo(42));
	}

	[Test]
	public void CacheComparable_ImplicitConversionToT_ReturnsValue() {
		var comparable = new CacheComparable<int>(42);
		int value = comparable;
		Assert.That(value, Is.EqualTo(42));
	}

	[Test]
	public void CacheComparable_ToString_ReturnsValueString() {
		var comparable = new CacheComparable<int>(42);
		Assert.That(comparable.ToString(), Is.EqualTo("42"));
	}

	[Test]
	public void CacheEquatable_WithReferenceType_ComparesCorrectly() {
		var a = new CacheEquatable<string>("hello");
		var b = new CacheEquatable<string>("hello");
		var c = new CacheEquatable<string>("world");

		Assert.That(a.CacheEquals(b), Is.True);
		Assert.That(a.CacheEquals(c), Is.False);
	}

	[Test]
	public void CacheComparable_WithStrings_ComparesCorrectly() {
		var a = new CacheComparable<string>("apple");
		var b = new CacheComparable<string>("banana");

		Assert.That(a.CompareTo(b), Is.LessThan(0));
		Assert.That(b.CompareTo(a), Is.GreaterThan(0));
	}

	[Test]
	public void CacheEquatable_UsedInDictionary_WorksCorrectly() {
		var dict = new Dictionary<CacheEquatable<int>, string>();
		dict[new CacheEquatable<int>(1)] = "one";
		dict[new CacheEquatable<int>(2)] = "two";

		Assert.That(dict[new CacheEquatable<int>(1)], Is.EqualTo("one"));
		Assert.That(dict.ContainsKey(new CacheEquatable<int>(2)), Is.True);
		Assert.That(dict.ContainsKey(new CacheEquatable<int>(3)), Is.False);
	}

	[Test]
	public void CacheComparable_UsedInSortedList_SortsCorrectly() {
		var list = new List<CacheComparable<int>> {
			new(3),
			new(1),
			new(2)
		};

		list.Sort();

		Assert.That(list[0].Value, Is.EqualTo(1));
		Assert.That(list[1].Value, Is.EqualTo(2));
		Assert.That(list[2].Value, Is.EqualTo(3));
	}
}