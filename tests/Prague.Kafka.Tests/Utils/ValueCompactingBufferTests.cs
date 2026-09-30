namespace Prague.Kafka.Tests.Utils;

using Prague.Kafka.Utils;

/// <summary>
///   The load-phase compaction buffer carries the forced-write flag with the surviving slot, and the
///   flag ORs across replaces of one key: a forced write that was compacted away would have been applied
///   unconditionally on the live path, so the write that supersedes it must be too (load/live parity).
/// </summary>
[TestFixture]
public class ValueCompactingBufferTests {
	private sealed class Value {
		public string Name = "";
	}

	private static Value V(string name) => new() { Name = name };

	private static List<(Value Value, long TimestampMs, bool Forced)> Drain(ValueCompactingBuffer<int, Value> buffer) {
		var result = new List<(Value, long, bool)>();
		foreach (var entry in buffer)
			result.Add(entry);
		return result;
	}

	[Test]
	public void UnforcedWrite_SurvivesUnforced() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.That(slots[0].Forced, Is.False);
	}

	[Test]
	public void ForcedThenUnforced_SurvivorIsForced() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: true);
		buffer.AddOrReplace(1, V("b"), 20, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.Multiple(() => {
			Assert.That(slots[0].Value.Name, Is.EqualTo("b"), "last write wins");
			Assert.That(slots[0].TimestampMs, Is.EqualTo(20));
			Assert.That(slots[0].Forced, Is.True, "the compacted-away forced write must still be applied unconditionally");
		});
	}

	[Test]
	public void UnforcedThenForced_SurvivorIsForced() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: false);
		buffer.AddOrReplace(1, V("b"), 20, forced: true);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.That(slots[0].Forced, Is.True);
	}

	[Test]
	public void RemoveThenUnforced_SurvivorIsNotForced() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: true);
		buffer.Remove(1);
		buffer.AddOrReplace(1, V("b"), 20, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.That(slots[0].Forced, Is.False, "a tombstone in between makes the later write a plain Add");
	}

	[Test]
	public void OtherKeys_DoNotInheritTheFlag() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: true);
		buffer.AddOrReplace(2, V("b"), 20, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(2));
		Assert.Multiple(() => {
			Assert.That(slots.Single(s => s.Value.Name == "a").Forced, Is.True);
			Assert.That(slots.Single(s => s.Value.Name == "b").Forced, Is.False);
		});
	}

	[Test]
	public void Clear_ResetsTheFlag() {
		var buffer = new ValueCompactingBuffer<int, Value>(4);
		buffer.AddOrReplace(1, V("a"), 10, forced: true);
		buffer.Clear();
		buffer.AddOrReplace(1, V("b"), 20, forced: false);

		var slots = Drain(buffer);

		Assert.That(slots, Has.Count.EqualTo(1));
		Assert.That(slots[0].Forced, Is.False);
	}
}
