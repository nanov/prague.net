namespace Prague.Kafka.Utils;

using System.Runtime.CompilerServices;

internal class ValueSpinWait {
	public static bool SpinUntil<TArg>(Func<TArg, bool> condition, int millisecondsTimeout, TArg arg) {
		if (millisecondsTimeout < Timeout.Infinite)
			throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout,
				"[ValueSpingWait][SpinUntil] Wrong Timeout");

		ArgumentNullException.ThrowIfNull(condition);
		uint startTime = 0;
		if (millisecondsTimeout != 0 && millisecondsTimeout != Timeout.Infinite)
			startTime = GetTime();

		SpinWait spinner = default;
		while (!condition(arg)) {
			if (millisecondsTimeout == 0)
				return false;

			spinner.SpinOnce();

			if (millisecondsTimeout != Timeout.Infinite && spinner.NextSpinWillYield
			                                            && millisecondsTimeout <= GetTime() - startTime)
				return false;
		}

		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static uint GetTime() {
		return (uint)Environment.TickCount;
	}
}