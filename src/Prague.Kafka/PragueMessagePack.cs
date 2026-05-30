namespace Prague.Kafka;

using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using SerDe;

/// <summary>
///   Carries the MessagePack serializer options used by every internal
///   Prague SerDe call site. Designed to be set once at startup (via
///   AddKafkaCaches) and read on every hot path. Independent of
///   <see cref="MessagePackSerializer.DefaultOptions"/> so host mutations
///   of that static do not affect Prague's wire format.
/// </summary>
public static class PragueMessagePack {
	private static readonly StringInterningFormatter _stringInterningFormatter = new();

	/// <summary>
	///   Builds Prague's default resolver composite. The built-in MessagePack
	///   <see cref="StringInterningFormatter"/> sits in the formatter slot
	///   (uses a <c>ConditionalWeakTable</c> internally so interned entries
	///   are reclaimed by the GC when no other references exist — no
	///   intern-table leak); <see cref="PragueDateTimeResolver"/> intercepts
	///   DateTime/DateTime?; <see cref="TypelessContractlessStandardResolver"/>
	///   handles everything else for byte-for-byte compatibility with topics
	///   produced under the historical host-side composite.
	/// </summary>
	internal static IFormatterResolver CreateDefaultComposite() =>
		CompositeResolver.Create(
			new IMessagePackFormatter[] { _stringInterningFormatter },
			new IFormatterResolver[] {
				PragueDateTimeResolver.Instance,
				TypelessContractlessStandardResolver.Instance
			});

	private static readonly MessagePackSerializerOptions _defaultSentinel =
		MessagePackSerializerOptions.Standard.WithResolver(CreateDefaultComposite());

	private static MessagePackSerializerOptions _options = _defaultSentinel;

	public static MessagePackSerializerOptions Options => _options;

	internal static void Configure(MessagePackSerializerOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (ReferenceEquals(_options, options))
			return;
		if (!ReferenceEquals(_options, _defaultSentinel)) {
			throw new InvalidOperationException(
				"PragueMessagePack.Configure called twice with conflicting options. Configure once at startup.");
		}
		_options = options;
	}

	internal static MessagePackSerializerOptions DefaultOptions() => _defaultSentinel;

	/// <summary>Test-only: restore the default sentinel so each test runs from a clean baseline.</summary>
	internal static void ResetForTests() => _options = _defaultSentinel;
}
