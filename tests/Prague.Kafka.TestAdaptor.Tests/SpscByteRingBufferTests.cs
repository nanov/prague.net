namespace Prague.Kafka.TestAdaptor.Tests;

using System.Threading.Channels;
using Prague.Kafka.Utils;
using NUnit.Framework;

[TestFixture]
public class SpscByteRingBufferTests {
	[Test]
	public void Rent_SingleAllocation_Pooled() {
		using var ring = new SpscByteRingBuffer(256);

		var mem = ring.Rent(10, out var pooled);

		Assert.That(pooled, Is.True);
		Assert.That(mem.Length, Is.EqualTo(10));
	}

	[Test]
	public void Rent_DataIntegrity() {
		using var ring = new SpscByteRingBuffer(256);
		var data = new byte[] { 1, 2, 3, 4, 5 };

		var mem = ring.Rent(data.Length, out var pooled);
		Assert.That(pooled, Is.True);
		data.CopyTo(mem.Span);

		Assert.That(mem.ToArray(), Is.EqualTo(data));
	}

	[Test]
	public void Rent_MultipleAllocations_IndependentMemory() {
		using var ring = new SpscByteRingBuffer(256);

		var mem1 = ring.Rent(10, out _);
		var mem2 = ring.Rent(10, out _);

		mem1.Span.Fill(1);
		mem2.Span.Fill(2);

		Assert.That(mem1.Span[0], Is.EqualTo(1));
		Assert.That(mem2.Span[0], Is.EqualTo(2));
	}

	[Test]
	public void Rent_ExceedsCapacity_FallsBackToNewArray() {
		using var ring = new SpscByteRingBuffer(32);

		ring.Rent(20, out var pooled1);
		Assert.That(pooled1, Is.True);

		var mem2 = ring.Rent(20, out var pooled2);
		Assert.That(pooled2, Is.False);
		Assert.That(mem2.Length, Is.EqualTo(20));
	}

	[Test]
	public void Return_FreesSpace_AllowsNewRent() {
		using var ring = new SpscByteRingBuffer(25);

		var m1 = ring.Rent(10, out _);
		var m2 = ring.Rent(10, out _);

		// Current state: head=20, tail=0. Space left=5.
		ring.Rent(10, out var pooled3);
		Assert.That(pooled3, Is.False, "Buffer should be full (only 5 bytes left)");

		ring.Return(m1); // tail=10
		ring.Return(m2); // tail=20

		// Wrap check: head is 20, endSpace is 5. size 9.
		// Wrap to 0 is possible because tail (20) > size (9).
		var m4 = ring.Rent(9, out var pooled4);
		Assert.That(pooled4, Is.True);
		Assert.That(m4.Length, Is.EqualTo(9));
	}

	[Test]
	public void Rent_ExactFit_FallsBack_WhenFullEqualsEmpty() {
		// Capacity 16. Renting 16 when tail is 0 would set head to 0.
		// Logic must prevent this so head=0/tail=0 remains meaning "Empty".
		using var ring = new SpscByteRingBuffer(16);

		var mem = ring.Rent(16, out var pooled);
		Assert.That(pooled, Is.False);
	}

	[Test]
	public void Rent_ExactFit_Pooled_WhenTailNonZero() {
		using var ring = new SpscByteRingBuffer(17);

		var mem = ring.Rent(16, out var pooled);
		Assert.That(pooled, Is.True);
		Assert.That(mem.Length, Is.EqualTo(16));
	}

	[Test]
	public void RentReturn_FifoOrder_MultipleRoundsWork() {
		using var ring = new SpscByteRingBuffer(64);

		for (var round = 0; round < 100; round++) {
			var mem = ring.Rent(8, out var pooled);
			Assert.That(pooled, Is.True, $"Rent fell back on round {round}");
			mem.Span[0] = (byte)round;
			ring.Return(mem);
		}
	}

	[Test]
	public void WrapAround_Continuous_Stress() {
		// Use a size that doesn't divide evenly into capacity to force frequent wraps
		using var ring = new SpscByteRingBuffer(100);
		var rented = new Queue<Memory<byte>>();

		for (var i = 0; i < 1000; i++) {
			var mem = ring.Rent(30, out var pooled);
			Assert.That(pooled, Is.True, $"Failed at iteration {i}");
			rented.Enqueue(mem);

			if (rented.Count > 1) {
				ring.Return(rented.Dequeue());
			}
		}
	}

	[Test]
	public async Task Concurrent_ProducerConsumer_DataIntegrity() {
		const int messageCount = 100_000;
		const int payloadSize = 32;
		using var ring = new SpscByteRingBuffer(4096);

		var channel = Channel.CreateBounded<(Memory<byte> Memory, bool Pooled, int Index)>(
			new BoundedChannelOptions(16) { SingleReader = true, SingleWriter = true });

		var corrupted = -1;

		var producer = Task.Run(async () => {
			var writer = channel.Writer;
			for (var i = 0; i < messageCount; i++) {
				var mem = ring.Rent(payloadSize, out var pooled);
				BitConverter.TryWriteBytes(mem.Span, i);
				mem.Span[4..].Fill((byte)(i % 251));
				await writer.WriteAsync((mem, pooled, i));
			}
			writer.Complete();
		});

		var consumer = Task.Run(async () => {
			var reader = channel.Reader;
			var received = 0;
			while (await reader.WaitToReadAsync()) {
				while (reader.TryRead(out var item)) {
					var (memory, pooled, expectedIndex) = item;

					if (Volatile.Read(ref corrupted) >= 0) {
						if (pooled) ring.Return(memory);
						continue;
					}

					if (memory.Length != payloadSize || BitConverter.ToInt32(memory.Span) != expectedIndex) {
						Volatile.Write(ref corrupted, received);
					} else {
						var expectedFill = (byte)(expectedIndex % 251);
						var span = memory.Span[4..];
						for (var j = 0; j < span.Length; j++) {
							if (span[j] != expectedFill) {
								Volatile.Write(ref corrupted, received);
								break;
							}
						}
					}

					if (pooled) ring.Return(memory);
					received++;
				}
			}
		});

		await Task.WhenAll(producer, consumer);
		Assert.That(Volatile.Read(ref corrupted), Is.EqualTo(-1), "Data corruption detected");
	}

}