namespace Prague.Kafka.OpenTelemetry;

using System.Collections;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

/// <summary>
/// Reusable IEnumerable+IEnumerator for an ObservableInstrument callback. One
/// instance per instrument; returns <c>this</c> from <see cref="GetEnumerator"/>
/// so the SDK's <c>foreach</c> over the callback result allocates nothing per
/// scrape. Safe because Meter SDK serialises callbacks per Meter.
/// </summary>
internal sealed class ReusableMeasurementSource<T> : IEnumerable<Measurement<T>>, IEnumerator<Measurement<T>>
	where T : struct {
	private Measurement<T>[] _buffer = Array.Empty<Measurement<T>>();
	private int _count;
	private int _index = -1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Measurement<T>[] Prepare(int count) {
		if (_buffer.Length < count)
			Array.Resize(ref _buffer, count);
		_count = count;
		return _buffer;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IEnumerator<Measurement<T>> GetEnumerator() {
		_index = -1;
		return this;
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public Measurement<T> Current {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _buffer[_index];
	}

	object IEnumerator.Current => Current;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool MoveNext() => ++_index < _count;

	public void Reset() => _index = -1;
	public void Dispose() { }
}
