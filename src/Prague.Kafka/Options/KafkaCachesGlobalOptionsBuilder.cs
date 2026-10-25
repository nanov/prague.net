namespace Prague.Kafka.Options;

using System;
using MessagePack;

/// <summary>
///   Builder for library-wide options. Today carries one option
///   (<see cref="WithMessagePackFormatterFactory"/>); future global knobs land here
///   without growing the AddKafkaCaches signature.
/// </summary>
public sealed class KafkaCachesGlobalOptionsBuilder {
	private Func<MessagePackFormatterFactory, MessagePackFormatterFactory>? _factoryCompose;

	/// <summary>
	///   Compose a custom formatter factory on top of Prague's default chain
	///   (<c>StringInterningFormatterFactory</c> + <c>PragueDateTimeFormatterFactory</c> + typeless default chain).
	///   The lambda receives the Prague chain as <c>defaultFactory</c> and
	///   returns the final factory to use, typically
	///   <c>MessagePackFormatterFactory.Combine(myFactory, defaultFactory)</c>.
	/// </summary>
	/// <remarks>
	///   Calling this method more than once on the same builder replaces the
	///   previous compose delegate (last-wins). The lambda must not return null.
	/// </remarks>
	public KafkaCachesGlobalOptionsBuilder WithMessagePackFormatterFactory(
		Func<MessagePackFormatterFactory, MessagePackFormatterFactory> compose) {
		ArgumentNullException.ThrowIfNull(compose);
		_factoryCompose = compose;
		return this;
	}

	internal MessagePackSerializerOptions Build() {
		if (_factoryCompose is null)
			return PragueMessagePack.DefaultOptions();
		var composed = _factoryCompose(PragueMessagePack.CreateDefaultFactory());
		if (composed is null)
			throw new InvalidOperationException(
				"WithMessagePackFormatterFactory compose delegate returned null. Return a non-null MessagePackFormatterFactory.");
		return PragueMessagePack.CreateOptions(composed);
	}
}
