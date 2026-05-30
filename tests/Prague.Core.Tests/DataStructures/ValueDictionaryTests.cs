namespace Prague.Core.Tests.DataStructures;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class ValueDictionaryTests {
	[Test]
	public void Add_SingleItem_IncreasesCount() {
		using var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 10);

		dict.Add(1, "value1");

		Assert.That(dict.Count, Is.EqualTo(1));
	}

	[Test]
	public void Add_MultipleItems_IncreasesCount() {
		using var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 10);

		dict.Add(1, "value1");
		dict.Add(2, "value2");
		dict.Add(3, "value3");

		Assert.That(dict.Count, Is.EqualTo(3));
	}

	[Test]
	public void GetValueRef_ExistingKey_ReturnsCorrectValue() {
		using var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 10);
		dict.Add(42, "expected");

		ref var value = ref dict.GetValueRef(42);

		Assert.That(value, Is.EqualTo("expected"));
	}

	[Test]
	public void GetValueRef_ModifyValue_UpdatesInPlace() {
		using var dict = new ValueDictionary<int, int, DefaultKeyComparer<int>>(false, 10);
		dict.Add(1, 100);

		ref var value = ref dict.GetValueRef(1);
		value = 200;

		Assert.That(dict.GetValueRef(1), Is.EqualTo(200));
	}

	[Test]
	public void TryGetValue_ExistingKey_ReturnsTrueAndValue() {
		using var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 10);
		dict.Add(42, "expected");

		var found = dict.TryGetValue(42, out var value);

		Assert.That(found, Is.True);
		Assert.That(value, Is.EqualTo("expected"));
	}

	[Test]
	public void TryGetValue_NonExistingKey_ReturnsFalse() {
		using var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 10);
		dict.Add(1, "value1");

		var found = dict.TryGetValue(999, out _);

		Assert.That(found, Is.False);
	}

	[Test]
	public void Keys_ReturnsAllKeysInInsertionOrder() {
		using var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 10);
		dict.Add(3, "c");
		dict.Add(1, "a");
		dict.Add(2, "b");

		var keys = dict.Keys.ToArray();

		Assert.That(keys, Is.EqualTo(new[] { 3, 1, 2 }));
	}

	[Test]
	public void Values_ReturnsAllValuesInInsertionOrder() {
		using var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 10);
		dict.Add(3, "c");
		dict.Add(1, "a");
		dict.Add(2, "b");

		var values = dict.Values.ToArray();

		Assert.That(values, Is.EqualTo(new[] { "c", "a", "b" }));
	}

	[Test]
	public void ExtractValues_ReturnsValuesArray() {
		var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 3);
		dict.Add(1, "a");
		dict.Add(2, "b");
		dict.Add(3, "c");

		var values = dict.ExtractValues();
		dict.Dispose();

		Assert.That(values[0], Is.EqualTo("a"));
		Assert.That(values[1], Is.EqualTo("b"));
		Assert.That(values[2], Is.EqualTo("c"));
	}

	[Test]
	public void StringKey_DefaultComparer_WorksCorrectly() {
		using var dict = new ValueDictionary<string, int, DefaultKeyComparer<string>>(false, 10);
		dict.Add("hello", 1);
		dict.Add("world", 2);

		Assert.That(dict.GetValueRef("hello"), Is.EqualTo(1));
		Assert.That(dict.GetValueRef("world"), Is.EqualTo(2));
	}

	[Test]
	public void StringKey_OrdinalComparer_WorksCorrectly() {
		// Uses the same NonRandomizedStringEqualityComparer as ConcurrentDictionary
		using var dict = new ValueDictionary<string, int, CustomKeyComparer<string>>(false, 10, new CustomKeyComparer<string>(StringComparer.Ordinal));
		dict.Add("Hello", 1);
		dict.Add("hello", 2);

		Assert.That(dict.GetValueRef("Hello"), Is.EqualTo(1));
		Assert.That(dict.GetValueRef("hello"), Is.EqualTo(2));
		Assert.That(dict.Count, Is.EqualTo(2));
	}

	[Test]
	public void LargeCapacity_HandlesCollisionsCorrectly() {
		using var dict = new ValueDictionary<int, int, DefaultKeyComparer<int>>(false, 1000);

		for (var i = 0; i < 1000; i++)
			dict.Add(i, i * 10);

		Assert.That(dict.Count, Is.EqualTo(1000));

		for (var i = 0; i < 1000; i++)
			Assert.That(dict.GetValueRef(i), Is.EqualTo(i * 10));
	}

	[Test]
	public void ValuesMutable_AllowsInPlaceModification() {
		using var dict = new ValueDictionary<int, int, DefaultKeyComparer<int>>(false, 3);
		dict.Add(1, 10);
		dict.Add(2, 20);
		dict.Add(3, 30);

		var mutableValues = dict.ValuesMutable;
		mutableValues[0] = 100;
		mutableValues[1] = 200;
		mutableValues[2] = 300;

		Assert.That(dict.GetValueRef(1), Is.EqualTo(100));
		Assert.That(dict.GetValueRef(2), Is.EqualTo(200));
		Assert.That(dict.GetValueRef(3), Is.EqualTo(300));
	}

	[Test]
	public void Dispose_CanBeCalledMultipleTimes() {
		var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 10);
		dict.Add(1, "a");

		dict.Dispose();
		dict.Dispose(); // Should not throw
	}

	[Test]
	public void SmallExpectedCount_StillWorks() {
		using var dict = new ValueDictionary<int, string, DefaultKeyComparer<int>>(false, 1);
		dict.Add(42, "value");

		Assert.That(dict.Count, Is.EqualTo(1));
		Assert.That(dict.GetValueRef(42), Is.EqualTo("value"));
	}

	[Test]
	public void GuidKey_WorksCorrectly() {
		using var dict = new ValueDictionary<Guid, string, DefaultKeyComparer<Guid>>(false, 10);
		var guid1 = Guid.NewGuid();
		var guid2 = Guid.NewGuid();

		dict.Add(guid1, "first");
		dict.Add(guid2, "second");

		Assert.That(dict.GetValueRef(guid1), Is.EqualTo("first"));
		Assert.That(dict.GetValueRef(guid2), Is.EqualTo("second"));
	}

	[Test]
	public void LongKey_WorksCorrectly() {
		using var dict = new ValueDictionary<long, string, DefaultKeyComparer<long>>(false, 10);
		dict.Add(long.MaxValue, "max");
		dict.Add(long.MinValue, "min");
		dict.Add(0L, "zero");

		Assert.That(dict.GetValueRef(long.MaxValue), Is.EqualTo("max"));
		Assert.That(dict.GetValueRef(long.MinValue), Is.EqualTo("min"));
		Assert.That(dict.GetValueRef(0L), Is.EqualTo("zero"));
	}
}