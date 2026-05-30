namespace Prague.Kafka.Options;

using System;
using MessagePack;

/// <summary>
///   Builder for library-wide options. Today carries one option
///   (<see cref="WithMessagePackResolver"/>); future global knobs land here
///   without growing the AddKafkaCaches signature.
/// </summary>
public sealed class KafkaCachesGlobalOptionsBuilder {
	private Func<IFormatterResolver, IFormatterResolver>? _resolverCompose;

	/// <summary>
	///   Compose a custom resolver on top of Prague's default composite
	///   (<c>StringInterningFormatter</c> + <c>PragueDateTimeResolver</c> + <c>TypelessContractlessStandardResolver</c>).
	///   The lambda receives the Prague composite as <c>defaultResolver</c> and
	///   returns the final resolver to use.
	/// </summary>
	/// <remarks>
	///   Calling this method more than once on the same builder replaces the
	///   previous compose delegate (last-wins). The lambda must not return null.
	/// </remarks>
	public KafkaCachesGlobalOptionsBuilder WithMessagePackResolver(
		Func<IFormatterResolver, IFormatterResolver> compose) {
		ArgumentNullException.ThrowIfNull(compose);
		_resolverCompose = compose;
		return this;
	}

	internal MessagePackSerializerOptions Build() {
		if (_resolverCompose is null)
			return PragueMessagePack.DefaultOptions();
		var composed = _resolverCompose(PragueMessagePack.CreateDefaultComposite());
		if (composed is null)
			throw new InvalidOperationException(
				"WithMessagePackResolver compose delegate returned null. Return a non-null IFormatterResolver.");
		return MessagePackSerializerOptions.Standard.WithResolver(composed);
	}
}
