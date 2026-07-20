namespace Prague.Core.Tests.DataStructures;

using Prague.Core.Utils;
using NUnit.Framework;

/// <summary>
///   Characterization + differential tests for <c>BitHelper.FindFirstMarked</c> /
///   <c>FindFirstUnmarked</c>. These pin the exact scan semantics the ValueSet
///   intersect sweeps depend on — notably that the scan is bounded only by the
///   bitmap word count, NOT by any logical element count: positions in the tail of
///   the final word read as unmarked and ARE returned. Callers bound the loop
///   themselves. Any word-at-a-time rewrite must preserve that.
/// </summary>
[TestFixture]
public class BitHelperScanTests {
	private const int IntSize = 32;

	// Reference implementations — deliberately naive, bit at a time.
	private static int NaiveFirstUnmarked(ReadOnlySpan<int> bits, int start) {
		for (var i = start; i < bits.Length * IntSize; i++)
			if ((bits[i >> 5] & (1 << (i & 31))) == 0)
				return i;

		return -1;
	}

	private static int NaiveFirstMarked(ReadOnlySpan<int> bits, int start) {
		for (var i = start; i < bits.Length * IntSize; i++)
			if ((bits[i >> 5] & (1 << (i & 31))) != 0)
				return i;

		return -1;
	}

	// --- Empty ---

	[Test]
	public void EmptySpan_BothScans_ReturnMinusOne() {
		var helper = new BitHelper(Span<int>.Empty, false);

		Assert.That(helper.FindFirstUnmarked(), Is.EqualTo(-1));
		Assert.That(helper.FindFirstMarked(), Is.EqualTo(-1));
	}

	// --- All clear / all set ---

	[Test]
	public void AllBitsClear_FindsUnmarkedAtZero_AndNoMarked() {
		Span<int> bits = stackalloc int[4];
		var helper = new BitHelper(bits, true);

		Assert.That(helper.FindFirstUnmarked(), Is.EqualTo(0));
		Assert.That(helper.FindFirstMarked(), Is.EqualTo(-1));
	}

	[Test]
	public void AllBitsSet_FindsMarkedAtZero_AndNoUnmarked() {
		Span<int> bits = stackalloc int[4];
		bits.Fill(-1);
		var helper = new BitHelper(bits, false);

		Assert.That(helper.FindFirstMarked(), Is.EqualTo(0));
		Assert.That(helper.FindFirstUnmarked(), Is.EqualTo(-1));
	}

	// --- Single bit, swept across word boundaries ---

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(30)]
	[TestCase(31)]
	[TestCase(32)]
	[TestCase(33)]
	[TestCase(63)]
	[TestCase(64)]
	[TestCase(95)]
	[TestCase(127)]
	public void SingleMarkedBit_IsFound(int position) {
		Span<int> bits = stackalloc int[4];
		var helper = new BitHelper(bits, true);
		helper.MarkBit(position);

		Assert.That(helper.FindFirstMarked(), Is.EqualTo(position));
	}

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(31)]
	[TestCase(32)]
	[TestCase(63)]
	[TestCase(64)]
	[TestCase(127)]
	public void SingleUnmarkedBit_IsFound(int position) {
		Span<int> bits = stackalloc int[4];
		bits.Fill(-1);
		var helper = new BitHelper(bits, false);
		helper.UnmarkBit(position);

		Assert.That(helper.FindFirstUnmarked(), Is.EqualTo(position));
	}

	// --- startPosition handling ---

	[Test]
	public void StartPosition_AtTheHit_ReturnsThatPosition() {
		Span<int> bits = stackalloc int[4];
		var helper = new BitHelper(bits, true);
		helper.MarkBit(40);

		Assert.That(helper.FindFirstMarked(40), Is.EqualTo(40));
	}

	[Test]
	public void StartPosition_PastTheHit_FindsTheNextOne() {
		Span<int> bits = stackalloc int[4];
		var helper = new BitHelper(bits, true);
		helper.MarkBit(40);
		helper.MarkBit(90);

		Assert.That(helper.FindFirstMarked(41), Is.EqualTo(90));
		Assert.That(helper.FindFirstMarked(91), Is.EqualTo(-1));
	}

	[Test]
	public void StartPosition_MidWord_SkipsEarlierBitsInSameWord() {
		Span<int> bits = stackalloc int[2];
		var helper = new BitHelper(bits, true);
		helper.MarkBit(3);
		helper.MarkBit(20);

		Assert.That(helper.FindFirstMarked(4), Is.EqualTo(20));
	}

	[Test]
	public void StartPosition_BeyondSpan_ReturnsMinusOne() {
		Span<int> bits = stackalloc int[2];
		var helper = new BitHelper(bits, true);

		Assert.That(helper.FindFirstUnmarked(64), Is.EqualTo(-1));
		Assert.That(helper.FindFirstUnmarked(1000), Is.EqualTo(-1));
		Assert.That(helper.FindFirstMarked(64), Is.EqualTo(-1));
	}

	[Test]
	public void StartPosition_OnLastValidBit_IsInspected() {
		Span<int> bits = stackalloc int[2];
		bits.Fill(-1);
		var helper = new BitHelper(bits, false);
		helper.UnmarkBit(63);

		Assert.That(helper.FindFirstUnmarked(63), Is.EqualTo(63));
	}

	// --- Semantics that the ValueSet sweeps rely on ---

	[Test]
	public void TailOfFinalWord_IsNotMasked_AndReadsAsUnmarked() {
		// A ValueSet with 33 live slots allocates 2 words; bits 33..63 are tail padding.
		// The scan MUST still report them (callers stop on their own count bound).
		Span<int> bits = stackalloc int[2];
		var helper = new BitHelper(bits, true);
		for (var i = 0; i < 33; i++)
			helper.MarkBit(i);

		Assert.That(helper.FindFirstUnmarked(), Is.EqualTo(33));
	}

	[Test]
	public void MarkBit_BeyondSpan_IsSilentlyIgnored() {
		Span<int> bits = stackalloc int[1];
		var helper = new BitHelper(bits, true);
		helper.MarkBit(500);

		Assert.That(helper.FindFirstMarked(), Is.EqualTo(-1));
	}

	// --- Full sweep, the way ValueSet drives it ---

	[Test]
	public void RepeatedScan_EnumeratesEveryUnmarkedPosition_InOrder() {
		Span<int> bits = stackalloc int[3];
		bits.Fill(-1);
		var helper = new BitHelper(bits, false);
		int[] expected = [0, 31, 32, 47, 64, 95];
		foreach (var p in expected)
			helper.UnmarkBit(p);

		var found = new List<int>();
		for (var i = helper.FindFirstUnmarked(); i >= 0; i = helper.FindFirstUnmarked(i + 1))
			found.Add(i);

		Assert.That(found, Is.EqualTo(expected));
	}

	[Test]
	public void RepeatedScan_EnumeratesEveryMarkedPosition_InOrder() {
		Span<int> bits = stackalloc int[3];
		var helper = new BitHelper(bits, true);
		int[] expected = [0, 31, 32, 47, 64, 95];
		foreach (var p in expected)
			helper.MarkBit(p);

		var found = new List<int>();
		for (var i = helper.FindFirstMarked(); i >= 0; i = helper.FindFirstMarked(i + 1))
			found.Add(i);

		Assert.That(found, Is.EqualTo(expected));
	}

	// --- Differential fuzz against the naive reference ---

	[Test]
	public void Fuzz_MatchesNaiveReference_ForEveryStartPosition() {
		var random = new Random(20260720);

		for (var wordCount = 0; wordCount <= 6; wordCount++) {
			for (var iteration = 0; iteration < 40; iteration++) {
				var raw = new int[wordCount];
				for (var w = 0; w < wordCount; w++)
					raw[w] = iteration switch {
						0 => 0,          // all clear
						1 => -1,         // all set
						_ => random.Next(int.MinValue, int.MaxValue),
					};

				var helper = new BitHelper(raw, false);
				var totalBits = wordCount * IntSize;

				for (var start = 0; start <= totalBits + 2; start++) {
					Assert.That(helper.FindFirstUnmarked(start), Is.EqualTo(NaiveFirstUnmarked(raw, start)),
						$"FindFirstUnmarked words={wordCount} iter={iteration} start={start}");
					Assert.That(helper.FindFirstMarked(start), Is.EqualTo(NaiveFirstMarked(raw, start)),
						$"FindFirstMarked words={wordCount} iter={iteration} start={start}");
				}
			}
		}
	}
}
