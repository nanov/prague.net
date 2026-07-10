namespace Prague.Core.Tests.DataStructures;

	using Prague.Core.Collections;
	using NUnit.Framework;

/// <summary>
///   Single writer churning structure state (forcing node retirement / generation
///   turnover) against parallel lock-free readers. Values carry a high-bit marker so
///   any recycled/foreign memory served to a reader is detected immediately: writers
///   only ever store marker-tagged values, so an untagged value can only come from a
///   pool-recycled array. Reads may be STALE (that is the documented model) — the
///   assertions check integrity, never freshness.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ConcurrentReclamationStressTests {
	private const long Marker = 0x5AA5_0000_0000_0000;
	private const int Items = 512;

	private static long Encode(int i) => Marker | (uint)i;

	private static bool IsValid(long v)
		=> (v & unchecked((long)0xFFFF_0000_0000_0000)) == Marker && (v & 0xFFFF_FFFF) < Items;

	private struct CollectingAggregator : PooledBTree<long, long>.IResultAggregator {
		public List<(long Index, long Value)> Items;

		public void Add(long index, long value) => Items.Add((index, value));

		public void Dispose() { }
	}

	private static TimeSpan Duration => TimeSpan.FromSeconds(2);

	[Test]
	public void BTree_WriterChurn_ConcurrentReaders_NeverServeCorruptPairs() {
		var tree = new PooledBTree<long, long>();
		var current = new long[Items];
		for (var i = 0; i < Items; i++) {
			current[i] = 1000;
			tree.Add(1000, Encode(i));
		}

		var stop = false;
		Exception? writerError = null;
		var writer = new Thread(() => {
			try {
				var round = 1;
				while (!Volatile.Read(ref stop)) {
					// The incident pattern: shared keys migrate, leaves drain and
					// retire, splits rent new nodes — maximum reclamation churn.
					var newKey = 1000 + round;
					for (var i = 0; i < Items; i++) {
						tree.Add(newKey, Encode(i));
						if (!tree.Remove(current[i], Encode(i)))
							throw new InvalidOperationException($"leak: round {round}, item {i}");
						current[i] = newKey;
					}

					round++;
				}
			}
			catch (Exception ex) {
				writerError = ex;
			}
		});

		var readerErrors = new List<Exception>();
		var readers = new Thread[4];
		for (var r = 0; r < readers.Length; r++) {
			readers[r] = new Thread(() => {
				try {
					var agg = new CollectingAggregator { Items = new List<(long, long)>(Items * 4) };
					while (!Volatile.Read(ref stop)) {
						agg.Items.Clear();
						tree.RangeFrom(0, ref agg);
						for (var i = 0; i < agg.Items.Count; i++) {
							var (key, value) = agg.Items[i];
							if (!IsValid(value)) {
								// DIAGNOSTIC: neighborhood + persistence probe
								var lo = Math.Max(0, i - 3);
								var hi = Math.Min(agg.Items.Count - 1, i + 3);
								var neighborhood = string.Join(" | ",
									agg.Items.GetRange(lo, hi - lo + 1)
										.ConvertAll(p => $"({p.Index}, 0x{p.Value:X})"));
								var reAgg = new CollectingAggregator { Items = new List<(long, long)>(Items * 4) };
								tree.RangeFrom(0, ref reAgg);
								var persistent = "transient";
								foreach (var (k2, v2) in reAgg.Items) {
									if (!IsValid(v2)) {
										persistent = $"persistent as ({k2}, 0x{v2:X})";
										break;
									}
								}

								throw new InvalidOperationException(
									$"foreign pair ({key}, 0x{value:X}) at [{i}]/{agg.Items.Count}; " +
									$"neighborhood: {neighborhood}; rescan: {persistent}");
							}
						}

						tree.Contains(1000, Encode(0));
						tree.TryGetMin(out _, out _);
						tree.TryGetMax(out _, out _);
					}
				}
				catch (Exception ex) {
					lock (readerErrors) {
						readerErrors.Add(ex);
					}
				}
			});
		}

		writer.Start();
		foreach (var t in readers)
			t.Start();
		Thread.Sleep(Duration);
		Volatile.Write(ref stop, true);
		writer.Join();
		foreach (var t in readers)
			t.Join();

		Assert.That(writerError, Is.Null, writerError?.ToString());
		Assert.That(readerErrors, Is.Empty, readerErrors.Count > 0 ? readerErrors[0].ToString() : null);
		Assert.That(tree.Length, Is.EqualTo(Items), "every round is add-then-remove balanced");
		tree.Dispose();
		ReaderGate.TryDrain();
	}

	[Test]
	public void PooledSet_ChurnGrowDispose_ConcurrentReaders_NeverServeForeignValues() {
		var published = new PooledSet<long, DefaultKeyComparer<long>>();
		for (var i = 0; i < Items; i++)
			published.Add(Encode(i));

		var stop = false;
		Exception? writerError = null;
		var writer = new Thread(() => {
			try {
				while (!Volatile.Read(ref stop)) {
					// Bucket lifecycle churn: fill past DefaultCapacity (forces Grow),
					// drain to zero, dispose (retires the generation), replace.
					var replacement = new PooledSet<long, DefaultKeyComparer<long>>();
					for (var i = 0; i < Items; i++)
						replacement.Add(Encode(i));

					var old = Interlocked.Exchange(ref published, replacement);
					for (var i = 0; i < Items; i++) {
						if (!old.Remove(Encode(i)))
							throw new InvalidOperationException($"lost value {i}");
					}

					old.Dispose();
				}
			}
			catch (Exception ex) {
				writerError = ex;
			}
		});

		var readerErrors = new List<Exception>();
		var readers = new Thread[4];
		for (var r = 0; r < readers.Length; r++) {
			readers[r] = new Thread(() => {
				try {
					while (!Volatile.Read(ref stop)) {
						var set = Volatile.Read(ref published);

						// struct enumerator (pinned generation)
						foreach (var value in set) {
							if (!IsValid(value))
								throw new InvalidOperationException($"foreign value 0x{value:X} (struct enum)");
						}

						// boxed enumerator — the GetValues path that corrupted upstream
						IEnumerable<long> view = set;
						foreach (var value in view) {
							if (!IsValid(value))
								throw new InvalidOperationException($"foreign value 0x{value:X} (boxed enum)");
						}

						// gate-protected scoped reader
						set.Contains(Encode(7));
					}
				}
				catch (Exception ex) {
					lock (readerErrors) {
						readerErrors.Add(ex);
					}
				}
			});
		}

		writer.Start();
		foreach (var t in readers)
			t.Start();
		Thread.Sleep(Duration);
		Volatile.Write(ref stop, true);
		writer.Join();
		foreach (var t in readers)
			t.Join();

		Assert.That(writerError, Is.Null, writerError?.ToString());
		Assert.That(readerErrors, Is.Empty, readerErrors.Count > 0 ? readerErrors[0].ToString() : null);
		published.Dispose();
		ReaderGate.TryDrain();
	}
}
