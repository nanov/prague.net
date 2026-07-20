namespace Prague.Benchmarks;

using Prague.Core.Utils;
using BenchmarkDotNet.Attributes;

/// <summary>
///   The ValueSet intersect sweep: after marking survivors, walk every UNMARKED slot and
///   remove it. Driven by repeated FindFirstUnmarked(prev + 1), so the scan cost is paid on
///   every multi-lane query.
///
///   Baseline replicates the previous bit-at-a-time implementation verbatim; the benchmark
///   under test calls the word-at-a-time BitHelper. MarkedPercent is the survivor rate — a
///   selective intersect leaves most slots marked, which is where word-skipping pays.
/// </summary>
[MemoryDiagnoser]
public class BitHelperScanBenchmarks {
	private int[] _bits = null!;
	private int _bitCount;

	// The bitmap is ceil(_lastIndex / 32) words, and _lastIndex is however many keys the FIRST
	// lane inserted — so this is really "how broad was the leading WithXxx".
	//   64    — candidate set inside ValueSet's 47-slot inline capacity.
	//   3200  — the stackalloc ceiling (100 words); beyond this the bitmap is pooled.
	//   4096  — kept as a regression guard: the bit-at-a-time scan beat the word scan here
	//           until the startPosition fast path landed.
	//   65536 — a broad leading lane.
	[Params(64, 3200, 4096, 65536)] public int BitCount { get; set; }

	[Params(50, 95, 100)] public int MarkedPercent { get; set; }

	[GlobalSetup]
	public void Setup() {
		_bitCount = BitCount;
		_bits = new int[(BitCount + 31) / 32];

		// Deterministic layout — the same slots are marked for both implementations.
		var random = new Random(20260720);
		for (var i = 0; i < BitCount; i++) {
			if (random.Next(100) >= MarkedPercent)
				continue;

			_bits[i >> 5] |= 1 << (i & 31);
		}
	}

	// Verbatim copy of the previous implementation, kept here as the comparison baseline.
	private static int ScalarFindFirstUnmarked(ReadOnlySpan<int> span, int startPosition) {
		var i = startPosition;
		for (var bi = i / 32; (uint)bi < (uint)span.Length; bi = ++i / 32)
			if ((span[bi] & (1 << (i % 32))) == 0)
				return i;

		return -1;
	}

	[Benchmark(Baseline = true)]
	public long Sweep_BitAtATime() {
		var span = _bits.AsSpan();
		long sum = 0;
		for (var i = ScalarFindFirstUnmarked(span, 0);
		     (uint)i < (uint)_bitCount;
		     i = ScalarFindFirstUnmarked(span, i + 1))
			sum += i;

		return sum;
	}

	[Benchmark]
	public long Sweep_WordAtATime() {
		var helper = new BitHelper(_bits, false);
		long sum = 0;
		for (var i = helper.FindFirstUnmarked();
		     (uint)i < (uint)_bitCount;
		     i = helper.FindFirstUnmarked(i + 1))
			sum += i;

		return sum;
	}
}
