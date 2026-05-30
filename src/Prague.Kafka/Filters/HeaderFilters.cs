namespace Prague.Kafka.Filters;

using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using MessagePack;
using SerDe;

internal sealed class KafkaHeaderFilters {
	private static readonly KafkaHeaderFilters _empty = new(new Dictionary<string, List<KafkaHeaderFilterExecutor>>());

	private readonly FrozenDictionary<string, KafkaHeaderFilterExecutor> _filters;

	internal readonly bool InitialState;

	private KafkaHeaderFilters(Dictionary<string, List<KafkaHeaderFilterExecutor>> filters) {
		var initialStateisFalse = false;
		_filters = filters.ToFrozenDictionary(x => x.Key,
			x => {
				if (x.Value.Count == 0)
					throw new UnreachableException();
				var filter = x.Value.Count switch {
					1 => x.Value[0],
					_ => new KafkaCombinedHeaderFilter(x.Value)
				};

				initialStateisFalse = initialStateisFalse || filter.IsInitialFalse;
				return filter;
			}, StringComparer.Ordinal);
		InitialState = !initialStateisFalse;
	}

	internal static KafkaHeaderFilters Create(Dictionary<string, List<KafkaHeaderFilterExecutor>>? filters) {
		if (filters is null || filters.Count == 0)
			return _empty;
		return new KafkaHeaderFilters(filters);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool ShouldProcess(ref bool searchState, string headerName, byte[] headersBytes) {
		return !_filters.TryGetValue(headerName, out var filter) || filter.ShouldProcess(ref searchState, headersBytes);
	}
}

internal abstract class KafkaHeaderFilterExecutor {
	public abstract bool IsInitialFalse { get; }
	public abstract bool ShouldProcess(ref bool searchState, byte[] headersBytes);
}

internal abstract class KafkaHeaderFilter : KafkaHeaderFilterExecutor {
	public override bool IsInitialFalse => false;

	public sealed override bool ShouldProcess(ref bool searchState, byte[] headersBytes) {
		return ShouldProcess(headersBytes);
	}

	public abstract bool ShouldProcess(byte[] headersBytes);
}

internal sealed class KafkaCombinedHeaderFilter : KafkaHeaderFilterExecutor {
	private readonly KafkaHeaderFilterExecutor[] _filters;

	public KafkaCombinedHeaderFilter(List<KafkaHeaderFilterExecutor> filters) {
		_filters = new KafkaHeaderFilterExecutor[filters.Count];
		var isInitialFalse = true;
		for (var i = 0; i < filters.Count; i++) {
			isInitialFalse = isInitialFalse && filters[i].IsInitialFalse;
			_filters[i] = filters[i];
		}

		IsInitialFalse = isInitialFalse;
		_filters = filters.ToArray();
	}

	public override bool IsInitialFalse { get; }

	public override bool ShouldProcess(ref bool searchState, byte[] headersBytes) {
		foreach (var filter in _filters)
			if (!filter.ShouldProcess(ref searchState, headersBytes))
				return false;
		return true;
	}
}

internal sealed class KafkaHeaderExistsFilter : KafkaHeaderFilterExecutor {
	public override bool IsInitialFalse => true;

	public override bool ShouldProcess(ref bool searchState, byte[] headersBytes) {
		searchState = true;
		return true;
	}
}

internal sealed class KafkaHeaderNotExistsFilter : KafkaHeaderFilter {
	public override bool ShouldProcess(byte[] headersBytes) {
		return false;
	}
}

internal class KafkaHeaderEqualsFilter<T> : KafkaHeaderFilter {
	private readonly T _value;

	public KafkaHeaderEqualsFilter(T value) {
		_value = value;
	}

	public override bool ShouldProcess(byte[] headersBytes) {
		// MessagePack-exact first for int/long (canonical post-codegen format); raw length-check fallback for legacy.
		// Guid stays raw-only (codegen still emits raw 16-byte Guid).
		if (typeof(T) == typeof(int)) {
			if (HeadersSerDe.TryDeserializeMessagePackExact<int>(headersBytes, out var mi)) {
				return Unsafe.As<int, T>(ref mi)!.Equals(_value);
			}
			if (HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
				return Unsafe.As<int, T>(ref i)!.Equals(_value);
		}
		if (typeof(T) == typeof(long)) {
			if (HeadersSerDe.TryDeserializeMessagePackExact<long>(headersBytes, out var ml)) {
				return Unsafe.As<long, T>(ref ml)!.Equals(_value);
			}
			if (HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
				return Unsafe.As<long, T>(ref l)!.Equals(_value);
		}
		if (typeof(T) == typeof(Guid) && HeadersSerDe.TryDeserializeGuid(headersBytes, out var g))
			return Unsafe.As<Guid, T>(ref g)!.Equals(_value);

		var val = MessagePackSerializer.Deserialize<T?>(headersBytes, PragueMessagePack.Options);
		return val?.Equals(_value) ?? true;
	}
}

internal sealed class KafkaHeaderEqualsMultiFilter : KafkaHeaderFilter {
	private readonly KafkaHeaderFilter[] _filters;

	public KafkaHeaderEqualsMultiFilter(KafkaHeaderFilter[] filters) {
		_filters = filters;
	}

	public override bool ShouldProcess(byte[] headersBytes) {
		foreach (var filter in _filters)
			if (filter.ShouldProcess(headersBytes))
				return true;
		return false;
	}
}

// Specialized string version using UTF8 bytes for better performance (avoids allocation)
internal sealed class KafkaHeaderEqualsStringFilter : KafkaHeaderEqualsFilter<string> {
	private readonly byte[] _valueBytes;

	public KafkaHeaderEqualsStringFilter(string value) : base(value) {
		_valueBytes = Encoding.UTF8.GetBytes(value);
	}

	public override bool ShouldProcess(byte[] headersBytes) {
		return headersBytes.AsSpan().SequenceEqual(_valueBytes);
	}
}

internal sealed class KafkaHeaderNotEqualsFilter<T> : KafkaHeaderFilter {
	private readonly T _value;

	public KafkaHeaderNotEqualsFilter(T value) {
		_value = value;
	}

	public override bool ShouldProcess(byte[] headersBytes) {
		if (typeof(T) == typeof(int)) {
			if (HeadersSerDe.TryDeserializeMessagePackExact<int>(headersBytes, out var mi)) {
				return !Unsafe.As<int, T>(ref mi)!.Equals(_value);
			}
			if (HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
				return !Unsafe.As<int, T>(ref i)!.Equals(_value);
		}
		if (typeof(T) == typeof(long)) {
			if (HeadersSerDe.TryDeserializeMessagePackExact<long>(headersBytes, out var ml)) {
				return !Unsafe.As<long, T>(ref ml)!.Equals(_value);
			}
			if (HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
				return !Unsafe.As<long, T>(ref l)!.Equals(_value);
		}
		if (typeof(T) == typeof(Guid) && HeadersSerDe.TryDeserializeGuid(headersBytes, out var g))
			return !Unsafe.As<Guid, T>(ref g)!.Equals(_value);

		var val = MessagePackSerializer.Deserialize<T>(headersBytes, PragueMessagePack.Options);
		return val is null || !val.Equals(_value);
	}
}

// Specialized string version using UTF8 bytes for better performance (avoids allocation)
internal sealed class KafkaHeaderNotEqualsStringFilter : KafkaHeaderFilter {
	private readonly byte[] _valueBytes;

	public KafkaHeaderNotEqualsStringFilter(string value) {
		_valueBytes = Encoding.UTF8.GetBytes(value);
	}

	public override bool ShouldProcess(byte[] headersBytes) {
		return !headersBytes.AsSpan().SequenceEqual(_valueBytes);
	}
}

// Specialized numeric version — accepts raw int/long bytes and falls back to MessagePack for headers serialized manually.
internal sealed class KafkaHeaderEqualsNumericFilter : KafkaHeaderFilter {
	private readonly long _value;

	public KafkaHeaderEqualsNumericFilter(int value) {
		_value = value;
	}

	public KafkaHeaderEqualsNumericFilter(long value) {
		_value = value;
	}

	public override bool ShouldProcess(byte[] headersBytes) {
		// MessagePack-exact first (canonical), raw int/long length-check fallback for legacy.
		if (HeadersSerDe.TryDeserializeMessagePackExact<long>(headersBytes, out var j))
			return j == _value;
		if (HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
			return i == _value;
		if (HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
			return l == _value;

		return false;
	}
}

// Specialized numeric version — accepts raw int/long bytes and falls back to MessagePack for headers serialized manually.
internal sealed class KafkaHeaderNotEqualsNumericFilter : KafkaHeaderFilter {
	private readonly long _value;

	public KafkaHeaderNotEqualsNumericFilter(int value) {
		_value = value;
	}

	public KafkaHeaderNotEqualsNumericFilter(long value) {
		_value = value;
	}

	public override bool ShouldProcess(byte[] headersBytes) {
		if (HeadersSerDe.TryDeserializeMessagePackExact<long>(headersBytes, out var j))
			return j != _value;
		if (HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
			return i != _value;
		if (HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
			return l != _value;

		return true;
	}
}

internal sealed class KafkaHeaderPredicateFilter<T> : KafkaHeaderFilter
	where T : struct {
	private readonly Func<T, bool> _predicate;
	private readonly bool _passOnNull;

	public KafkaHeaderPredicateFilter(Func<T, bool> predicate, bool passOnNull = true) {
		_predicate = predicate;
		_passOnNull = passOnNull;
	}

	public override bool ShouldProcess(byte[] headersBytes) {
		// MessagePack-exact first for int/long (canonical); raw fallback for legacy.
		if (typeof(T) == typeof(int)) {
			if (HeadersSerDe.TryDeserializeMessagePackExact<int>(headersBytes, out var mi)) {
				return _predicate(Unsafe.As<int, T>(ref mi));
			}
			if (HeadersSerDe.TryDeserializeInt(headersBytes, out var i))
				return _predicate(Unsafe.As<int, T>(ref i));
		}
		if (typeof(T) == typeof(long)) {
			if (HeadersSerDe.TryDeserializeMessagePackExact<long>(headersBytes, out var ml)) {
				return _predicate(Unsafe.As<long, T>(ref ml));
			}
			if (HeadersSerDe.TryDeserializeLong(headersBytes, out var l))
				return _predicate(Unsafe.As<long, T>(ref l));
		}
		if (typeof(T) == typeof(Guid) && HeadersSerDe.TryDeserializeGuid(headersBytes, out var g))
			return _predicate(Unsafe.As<Guid, T>(ref g));

		// Fast-path for MessagePack nil (0xC0): no deserialize needed.
		if (headersBytes is { Length: 1 } && headersBytes[0] == 0xC0) {
			return _passOnNull;
		}

		if (HeadersSerDe.TryDeserializeMessagePack<T?>(headersBytes, out var j) && j != null)
			return _predicate(j.Value);

		return _passOnNull;
	}
}