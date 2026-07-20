namespace Prague.Core.Tests.Infrastructure;

using System.Buffers;
using Prague.Core.Collections;

/// <summary>
///   ArrayPool decorator that ledgers every Rent/Return in <see cref="LeakTracker"/>.
///   A Return of an array this pool did not hand out (double-return or foreign array)
///   is recorded as a violation and NOT forwarded, so the shared pool can never be
///   corrupted by the very bug the tests exist to catch.
/// </summary>
internal sealed class TrackingArrayPool<T> : ArrayPool<T> {
	internal static readonly TrackingArrayPool<T> Instance = new();

	private readonly ArrayPool<T> _inner = Shared;

	private TrackingArrayPool() {
	}

	public override T[] Rent(int minimumLength) {
		var array = _inner.Rent(minimumLength);
		LeakTracker.Register(array);
		return array;
	}

	public override void Return(T[] array, bool clearArray = false) {
		if (!LeakTracker.Unregister(array)) {
			LeakTracker.ReportViolation($"double- or foreign return of {typeof(T).Name}[{array.Length}] at:\n{Environment.StackTrace}");
			return;
		}

		_inner.Return(array, clearArray);
	}
}

internal sealed class TrackingArrayPoolProvider : IArrayPoolProvider {
	public ArrayPool<T> Get<T>() => TrackingArrayPool<T>.Instance;
}
