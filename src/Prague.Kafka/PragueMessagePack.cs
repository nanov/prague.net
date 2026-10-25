namespace Prague.Kafka;

using MessagePack;
using SerDe;

/// <summary>
///   Carries the MessagePack serializer options used by every internal
///   Prague SerDe call site. Designed to be set once at startup (via
///   AddKafkaCaches) and read on every hot path. MessagePack v4 has no
///   process-wide mutable default, so host code cannot change Prague's wire format.
/// </summary>
public static class PragueMessagePack {
	private static readonly StringInterningFormatterFactory _stringInterningFactory = new();

	/// <summary>
	///   Builds Prague's default factory chain. <see cref="StringInterningFormatterFactory"/>
	///   interns every deserialized string (a strong-reference pool owned by this
	///   factory); <see cref="PragueDateTimeFormatterFactory"/> intercepts DateTime;
	///   the typeless chain handles everything else for byte-for-byte compatibility
	///   with topics produced under the historical host-side
	///   <c>TypelessContractlessStandardResolver</c> composite.
	/// </summary>
	// LoadAnyType keeps v3 TypelessContractlessStandardResolver parity: payload type names
	// go through Type.GetType. v4 marks it obsolete; an allow-list loader is the safer choice.
#pragma warning disable CS0618
	internal static MessagePackFormatterFactory CreateDefaultFactory() =>
		MessagePackFormatterFactory.Combine(
			_stringInterningFactory,
			PragueDateTimeFormatterFactory.Instance,
			MessagePackFormatterFactory.Default.WithTypeless(TypelessTypeLoader.LoadAnyType()));
#pragma warning restore CS0618

	internal static MessagePackSerializerOptions CreateOptions(MessagePackFormatterFactory factory) =>
		new(new MessagePackFormatterResolver(factory));

	private static readonly MessagePackSerializerOptions _defaultSentinel = CreateOptions(CreateDefaultFactory());

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
