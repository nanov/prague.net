namespace Prague.Core;

using System.Text;

/// <summary>Live per-plan state a frozen query can print from <c>Explain()</c>.</summary>
internal interface IPlanExplainable {
	void Explain(StringBuilder sb);
}

/// <summary>
///   Execution-to-execution memory of one frozen plan: how many slots the candidate set used. The
///   next execution hands the value to the eager core as its initial capacity, so a ~1k bucket is
///   rented once at the right size instead of rehashing 47 → 97 → 197 → 397 → 797 → 1597. Advisory
///   shared state: plain <see cref="int" /> fields written without synchronization — a lost update
///   costs one execution a suboptimal size, nothing more. Bounded by <see cref="MaxHint" /> and shrunk
///   once the observed size has stayed under half the hint for <see cref="ShrinkAfter" /> consecutive
///   executions, so a one-off spike cannot pin an oversized rental forever.
/// </summary>
internal sealed class FrozenHints : IPlanExplainable {
	internal const int MaxHint = 1 << 20;
	internal const int ShrinkAfter = 8;

	private int _candidateCapacity;
	private int _lowStreak;

	/// <summary>The capacity to seed the next candidate set with; 0 means no hint (the eager default).</summary>
	internal int CandidateCapacity => _candidateCapacity;

	internal void Observe(int highWaterMark) {
		var hint = _candidateCapacity;
		if (highWaterMark > hint) {
			_candidateCapacity = Math.Min(highWaterMark, MaxHint);
			_lowStreak = 0;
			return;
		}

		if (highWaterMark < hint >> 1) {
			if (++_lowStreak < ShrinkAfter)
				return;
			_candidateCapacity = highWaterMark;
			_lowStreak = 0;
			return;
		}

		_lowStreak = 0;
	}

	public void Explain(StringBuilder sb)
		=> sb.Append("capacity hint: ").Append(_candidateCapacity).AppendLine();
}
