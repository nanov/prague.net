namespace Prague.Baseline.Harness.Sim;

using System.Runtime.InteropServices;
using Confluent.Kafka;
using Prague.Baseline.Scenario;
using Prague.Kafka.Internal;
using Prague.Kafka.SerDe;

/// <summary>
///   In-process replay of the REAL managed Kafka ingest tail — no broker, no librdkafka. Pre-encoded
///   MessagePack value bytes are pinned in native memory (mirroring librdkafka's buffer), deserialized
///   zero-copy via <see cref="CacheSerde{T}.DeserializeFromSpan"/>, enriched from empty raw headers +
///   the message timestamp, then published into a <see cref="AsyncValueBufferedWorker{T}"/> ring whose
///   worker thread applies <c>AddOrUpdate</c> (with indexing) to the generated cache.
///   <para>
///     Each phase (products, infos, offers) is an explicit, delegate-free typed loop: the payloads are
///     pinned once before the loop, the enricher is cached in a local, and no per-iteration allocation
///     occurs on the measured path. The native block is freed in a <c>finally</c>; the worker is drained
///     (<see cref="ValueBufferedWorkerBase{T}.TryComplete"/> + <c>Completion</c>) before the phase returns.
///   </para>
/// </summary>
internal static class SimIngestPipeline {
	private const long BaseTimestampMs = 1_700_000_000_000L;

	public static long IngestAll(
		EncodedSet enc,
		BaselineProductCache products, BaselineProductInfoCache infos, BaselineOfferCache offers) {
		long ops = 0;
		ops += DriveProducts(enc.Products, products);
		ops += DriveInfos(enc.Infos, infos);
		ops += DriveOffers(enc.Offers, offers);
		return ops;
	}

	private static unsafe long DriveProducts(EncodedEntity[] items, BaselineProductCache cache) {
		var count = items.Length;
		var worker = new ProductApplyWorker(cache, ScenarioSpec.RingCapacity);
		worker.Start();

		var block = (byte*)NativeMemory.Alloc((nuint)TotalValueBytes(items));
		try {
			var offsets = PinValues(items, block);
			var enricher = BaselineProduct.GetEnricher();
			for (var i = 0; i < count; i++) {
				var span = new ReadOnlySpan<byte>(block + offsets[i], items[i].Value.Length);
				var value = CacheSerde<BaselineProduct>.DeserializeFromSpan(span);
				var ts = BaseTimestampMs + i;
				enricher.Enrich(value, default(RawHeaders), new Timestamp(ts, TimestampType.CreateTime));

				using var scope = worker.Publish();
				if (scope.IsOpen) {
					ref var slot = ref scope.Event();
					slot.Value = value;
					slot.TimestampMs = ts;
				}
			}
		}
		finally {
			NativeMemory.Free(block);
		}

		Drain(worker);
		return count;
	}

	private static unsafe long DriveInfos(EncodedEntity[] items, BaselineProductInfoCache cache) {
		var count = items.Length;
		var worker = new InfoApplyWorker(cache, ScenarioSpec.RingCapacity);
		worker.Start();

		var block = (byte*)NativeMemory.Alloc((nuint)TotalValueBytes(items));
		try {
			var offsets = PinValues(items, block);
			var enricher = BaselineProductInfo.GetEnricher();
			for (var i = 0; i < count; i++) {
				var span = new ReadOnlySpan<byte>(block + offsets[i], items[i].Value.Length);
				var value = CacheSerde<BaselineProductInfo>.DeserializeFromSpan(span);
				var ts = BaseTimestampMs + i;
				enricher.Enrich(value, default(RawHeaders), new Timestamp(ts, TimestampType.CreateTime));

				using var scope = worker.Publish();
				if (scope.IsOpen) {
					ref var slot = ref scope.Event();
					slot.Value = value;
					slot.TimestampMs = ts;
				}
			}
		}
		finally {
			NativeMemory.Free(block);
		}

		Drain(worker);
		return count;
	}

	private static unsafe long DriveOffers(EncodedEntity[] items, BaselineOfferCache cache) {
		var count = items.Length;
		var worker = new OfferApplyWorker(cache, ScenarioSpec.RingCapacity);
		worker.Start();

		var block = (byte*)NativeMemory.Alloc((nuint)TotalValueBytes(items));
		try {
			var offsets = PinValues(items, block);
			var enricher = BaselineOffer.GetEnricher();
			for (var i = 0; i < count; i++) {
				var span = new ReadOnlySpan<byte>(block + offsets[i], items[i].Value.Length);
				var value = CacheSerde<BaselineOffer>.DeserializeFromSpan(span);
				var ts = BaseTimestampMs + i;
				enricher.Enrich(value, default(RawHeaders), new Timestamp(ts, TimestampType.CreateTime));

				using var scope = worker.Publish();
				if (scope.IsOpen) {
					ref var slot = ref scope.Event();
					slot.Value = value;
					slot.TimestampMs = ts;
				}
			}
		}
		finally {
			NativeMemory.Free(block);
		}

		Drain(worker);
		return count;
	}

	private static long TotalValueBytes(EncodedEntity[] items) {
		long total = 0;
		for (var i = 0; i < items.Length; i++)
			total += items[i].Value.Length;
		return total;
	}

	/// <summary>Copies every payload's value bytes into the pinned native block once and returns their offsets.</summary>
	private static unsafe int[] PinValues(EncodedEntity[] items, byte* block) {
		var offsets = new int[items.Length];
		var off = 0;
		for (var i = 0; i < items.Length; i++) {
			var value = items[i].Value;
			offsets[i] = off;
			value.AsSpan().CopyTo(new Span<byte>(block + off, value.Length));
			off += value.Length;
		}
		return offsets;
	}

	private static void Drain<T>(AsyncValueBufferedWorker<T> worker) where T : struct {
		worker.TryComplete(null);
		worker.Completion.GetAwaiter().GetResult();
		worker.Dispose();
	}
}
