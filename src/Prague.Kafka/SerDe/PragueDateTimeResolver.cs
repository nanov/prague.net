namespace Prague.Kafka.SerDe;

using MessagePack;
using MessagePack.Formatters;

/// <summary>
///   Single-type resolver that returns Prague's DateTime formatters for
///   <see cref="DateTime"/> and <see cref="Nullable{DateTime}"/> and falls
///   through (returns null) for everything else so it can be chained ahead
///   of the standard resolver in a composite.
/// </summary>
public sealed class PragueDateTimeResolver : IFormatterResolver {
	public static readonly PragueDateTimeResolver Instance = new();

	private PragueDateTimeResolver() { }

	public IMessagePackFormatter<T>? GetFormatter<T>() {
		if (typeof(T) == typeof(DateTime))
			return (IMessagePackFormatter<T>)(object)PragueDateTimeFormatter.Instance;
		if (typeof(T) == typeof(DateTime?))
			return (IMessagePackFormatter<T>)(object)PragueNullableDateTimeFormatter.Instance;
		return null;
	}
}
