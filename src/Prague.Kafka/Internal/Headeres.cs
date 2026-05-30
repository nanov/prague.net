namespace Prague.Kafka.Internal;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

public class HeadersCollection {
	private Dictionary<string, HeaderValues>? _headers;

	public void Add(string key, byte[] value) {
		_headers ??= new Dictionary<string, HeaderValues>(StringComparer.Ordinal);
		var headerValues = CollectionsMarshal.GetValueRefOrAddDefault(_headers, key, out var exists);
		if (exists)
			headerValues.AddValue(value);
		else
			headerValues.Initialize(value);
	}

	public void Remove(string key) {
		_headers?.Remove(key);
	}

	public bool TryGetLastBytes(string key, [MaybeNullWhen(false)] out byte[] value) {
		if (_headers?.TryGetValue(key, out var headerValues) is true) {
			value = headerValues.GetLastValueBytes();
			return true;
		}

		value = null;
		return false;
	}

	public bool TryGetFirstBytes(string key, [MaybeNullWhen(false)] out byte[] value) {
		if (_headers?.TryGetValue(key, out var headerValues) is true) {
			value = headerValues.GetFirstValueBytes();
			return true;
		}

		value = null;
		return false;
	}
}

// Abstraction to support one or more headers with minimizing allocations
public struct HeaderValues {
	private List<byte[]>? _restValues;
	private byte[] _firstValue;

	public uint Length => (uint)(_restValues?.Count ?? 0) + 1;

	public HeaderValues(byte[] firstValue) {
		_firstValue = firstValue;
	}

	public void AddValue(byte[] value) {
		_restValues ??= new List<byte[]>(1);
		_restValues.Add(value);
	}

	public bool HasValue(ReadOnlySpan<byte> value) {
		if (_firstValue.AsSpan().SequenceEqual(value))
			return true;

		if (_restValues is null)
			return false;

		foreach (var v in CollectionsMarshal.AsSpan(_restValues))
			if (v.AsSpan().SequenceEqual(value))
				return true;

		return false;
	}

	public byte[] GetFirstValueBytes() {
		return _firstValue;
	}

	public byte[] GetLastValueBytes() {
		return _restValues == null ? _firstValue : _restValues[^1];
	}


	// use only in when you really need to, it's allocating
	public IReadOnlyList<byte[]> GetValuesBytes() {
		if (_restValues == null) return [_firstValue];
		var arr = new byte[_restValues.Count + 1][];
		arr[0] = _firstValue;
		_restValues.CopyTo(arr, 1);
		return arr;
	}

	internal void Initialize(byte[] firstValue) {
		_firstValue = firstValue;
	}
}