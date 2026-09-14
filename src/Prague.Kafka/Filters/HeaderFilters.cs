namespace Prague.Kafka.Filters;

using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using SerDe;

/// <summary>
///   A header name that some filter requires to be present carries a bit; a message must collect every required
///   bit to pass. The requirement belongs to the header NAME, which is the dictionary key, so the bit lives on the
///   entry rather than on the filter object — the builder owns those and hands the same instances over on every
///   container build.
/// </summary>
internal readonly record struct HeaderFilterEntry(KafkaHeaderFilterExecutor Filter, ulong RequiredBit);

internal sealed class KafkaHeaderFilters {
	/// <summary>One bit per required header name, so at most 64 distinct <c>WithHeaderExistsFilter</c> names.</summary>
	internal const int MAX_REQUIRED_HEADERS = 64;

	private static readonly KafkaHeaderFilters _empty = new(new Dictionary<string, List<KafkaHeaderFilterExecutor>>());

	private readonly FrozenDictionary<string, HeaderFilterEntry> _filters;
	private readonly FrozenDictionary<string, HeaderFilterEntry>.AlternateLookup<ReadOnlySpan<char>> _byName;

	/// <summary>
	///   Every required header's bit OR-ed together. A message passes the presence check when the bits it collected
	///   equal this. Zero when nothing is required, which is the common case and short-circuits to accept.
	/// </summary>
	internal readonly ulong RequiredMask;

	/// <summary>
	///   UTF-8 byte length of the longest configured header name, and the bound that keeps the raw path's
	///   <c>stackalloc</c> from being sized by the wire. See <see cref="ShouldProcess" />.
	/// </summary>
	private readonly int _maxNameUtf8Length;

	private KafkaHeaderFilters(Dictionary<string, List<KafkaHeaderFilterExecutor>> filters) {
		var requiredMask = 0UL;
		var requiredCount = 0;
		var maxNameUtf8Length = 0;
		_filters = filters.ToFrozenDictionary(x => x.Key,
			x => {
				if (x.Value.Count == 0)
					throw new UnreachableException();
				var filter = x.Value.Count switch {
					1 => x.Value[0],
					_ => new KafkaCombinedHeaderFilter(x.Value)
				};

				var nameUtf8Length = Encoding.UTF8.GetByteCount(x.Key);
				if (nameUtf8Length > maxNameUtf8Length)
					maxNameUtf8Length = nameUtf8Length;

				if (!filter.RequiresHeader)
					return new HeaderFilterEntry(filter, 0);

				if (requiredCount == MAX_REQUIRED_HEADERS)
					throw new InvalidOperationException(
						$"[Prague] A cache may require at most {MAX_REQUIRED_HEADERS} distinct headers via "
						+ $"WithHeaderExistsFilter; '{x.Key}' is one too many.");

				var bit = 1UL << requiredCount++;
				requiredMask |= bit;
				return new HeaderFilterEntry(filter, bit);
			}, StringComparer.Ordinal);
		// Ordinal comparer supports span-keyed lookup — lets the raw consume path resolve a filter
		// from a UTF-8 header-name span with no string allocation.
		_byName = _filters.GetAlternateLookup<ReadOnlySpan<char>>();
		RequiredMask = requiredMask;
		_maxNameUtf8Length = maxNameUtf8Length;
	}

	internal static KafkaHeaderFilters Create(Dictionary<string, List<KafkaHeaderFilterExecutor>>? filters) {
		if (filters is null || filters.Count == 0)
			return _empty;
		return new KafkaHeaderFilters(filters);
	}

	/// <summary>
	///   Raw consume-path evaluation — resolves the filter from the UTF-8 header-name span (no string allocation)
	///   and evaluates it against the value span. On a hit, the entry's requirement bit is OR-ed into
	///   <paramref name="seen" />; the caller compares the result with <see cref="RequiredMask" /> once, after the
	///   last header.
	///   <para>
	///   The length gate is a safety bound, not an optimisation. A header name is raw wire data and Kafka bounds it
	///   only by <c>message.max.bytes</c>, so sizing a <c>stackalloc</c> by it let a producer overflow the stack —
	///   uncatchable, fatal to the whole host, and replayed from the log on restart. The gate is exact rather than
	///   merely conservative because <c>GetByteCount(GetString(b)) &gt;= b.Length</c> for <i>every</i> byte sequence,
	///   well-formed or not: a well-formed sequence round-trips byte for byte, and an ill-formed one decodes to
	///   U+FFFD, which re-encodes to three bytes and so never shrinks. A name longer than the longest configured key
	///   therefore cannot decode to any key, and no filter can match it.
	///   </para>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool ShouldProcess(ref ulong seen, ReadOnlySpan<byte> headerName, ReadOnlySpan<byte> headerValue) {
		if (headerName.Length > _maxNameUtf8Length)
			return true;
		return Resolve(ref seen, headerName, headerValue);
	}

	// Split out so the localloc lives in a callee: a stackalloc in the body blocks inlining of the whole method,
	// and the bound check above is what the caller wants inlined.
	private bool Resolve(ref ulong seen, ReadOnlySpan<byte> headerName, ReadOnlySpan<byte> headerValue) {
		Span<char> nameChars = stackalloc char[headerName.Length];
		var charCount = Encoding.UTF8.GetChars(headerName, nameChars);
		if (!_byName.TryGetValue(nameChars[..charCount], out var entry))
			return true;
		seen |= entry.RequiredBit;
		return entry.Filter.ShouldProcess(headerValue);
	}
}

internal abstract class KafkaHeaderFilterExecutor {
	/// <summary>
	///   Whether this filter requires its header to be PRESENT, as opposed to judging a value it saw. Only
	///   <see cref="KafkaHeaderExistsFilter" /> does; everything else passes a message that omits the header
	///   entirely. This is what <see cref="HeaderGate.MissingRequiredHeader" /> — and therefore the tombstone
	///   waiver — means, so a new executor answering true here changes that contract.
	/// </summary>
	public abstract bool RequiresHeader { get; }

	public abstract bool ShouldProcess(ReadOnlySpan<byte> headersBytes);
}

internal abstract class KafkaHeaderFilter : KafkaHeaderFilterExecutor {
	public override bool RequiresHeader => false;
}

internal sealed class KafkaCombinedHeaderFilter : KafkaHeaderFilterExecutor {
	private readonly KafkaHeaderFilterExecutor[] _filters;

	public KafkaCombinedHeaderFilter(List<KafkaHeaderFilterExecutor> filters) {
		_filters = new KafkaHeaderFilterExecutor[filters.Count];
		var requiresHeader = false;
		for (var i = 0; i < filters.Count; i++) {
			// OR, not AND: if ANY member requires its header to be present, so does the combination. AND meant that
			// pairing WithHeaderExistsFilter("h") with any other filter on "h" silently dropped the requirement.
			requiresHeader = requiresHeader || filters[i].RequiresHeader;
			_filters[i] = filters[i];
		}

		RequiresHeader = requiresHeader;
	}

	public override bool RequiresHeader { get; }

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) {
		foreach (var filter in _filters)
			if (!filter.ShouldProcess(headersBytes))
				return false;
		return true;
	}
}

/// <summary>
///   Requires its header to be present. It judges no value — presence is recorded by the container when the name
///   resolves, so this passes anything it is handed.
/// </summary>
internal sealed class KafkaHeaderExistsFilter : KafkaHeaderFilterExecutor {
	public override bool RequiresHeader => true;

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) => true;
}

internal class KafkaHeaderEqualsFilter<T> : KafkaHeaderFilter {
	private readonly T _value;

	public KafkaHeaderEqualsFilter(T value) {
		_value = value;
	}

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) {
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

		var val = SpanMessagePackDeserializer.Deserialize<T?>(headersBytes);
		return val?.Equals(_value) ?? true;
	}
}

internal sealed class KafkaHeaderEqualsMultiFilter : KafkaHeaderFilter {
	private readonly KafkaHeaderFilter[] _filters;

	public KafkaHeaderEqualsMultiFilter(KafkaHeaderFilter[] filters) {
		_filters = filters;
	}

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) {
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

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) {
		return headersBytes.SequenceEqual(_valueBytes);
	}
}

internal sealed class KafkaHeaderNotEqualsFilter<T> : KafkaHeaderFilter {
	private readonly T _value;

	public KafkaHeaderNotEqualsFilter(T value) {
		_value = value;
	}

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) {
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

		var val = SpanMessagePackDeserializer.Deserialize<T>(headersBytes);
		return val is null || !val.Equals(_value);
	}
}

// Specialized string version using UTF8 bytes for better performance (avoids allocation)
internal sealed class KafkaHeaderNotEqualsStringFilter : KafkaHeaderFilter {
	private readonly byte[] _valueBytes;

	public KafkaHeaderNotEqualsStringFilter(string value) {
		_valueBytes = Encoding.UTF8.GetBytes(value);
	}

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) {
		return !headersBytes.SequenceEqual(_valueBytes);
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

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) {
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

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) {
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

	public override bool ShouldProcess(ReadOnlySpan<byte> headersBytes) {
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
		if (headersBytes.Length == 1 && headersBytes[0] == 0xC0) {
			return _passOnNull;
		}

		if (HeadersSerDe.TryDeserializeMessagePack<T?>(headersBytes, out var j) && j != null)
			return _predicate(j.Value);

		return _passOnNull;
	}
}
