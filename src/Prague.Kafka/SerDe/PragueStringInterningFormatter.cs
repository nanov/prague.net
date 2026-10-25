namespace Prague.Kafka.SerDe;

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using MessagePack;
using MessagePack.Formatters;

/// <summary>
///   Prague's default string formatter: every deserialized string up to <see cref="MaxInternedBytes"/>
///   UTF-8 bytes goes through one process-wide <see cref="Utf8WeakInternPool"/>. Repeated values share one
///   instance; a repeat costs a hash and a byte compare against the live string (no decode, no allocation);
///   nothing is retained once the cache stops referencing it. Opt a member out with
///   <see cref="Core.DataCacheStringInternAttribute"/>.
/// </summary>
// Used explicitly (Prague's composite, member attributes); kept out of MessagePack's generated resolver.
[ExcludeFormatterFromSourceGeneratedResolver]
public sealed class PragueStringInterningFormatter : IMessagePackFormatter<string?> {
	/// <summary>Longer strings are rarely repeated values; they are decoded without interning.</summary>
	public const int MaxInternedBytes = 256;

	internal static readonly Utf8WeakInternPool Pool = new();

	/// <summary>Shared instance used in Prague's default resolver composite.</summary>
	public static readonly PragueStringInterningFormatter Instance = new();

	/// <inheritdoc/>
	public void Serialize(ref MessagePackWriter writer, string? value, MessagePackSerializerOptions options)
		=> writer.Write(value);

	/// <inheritdoc/>
	[SkipLocalsInit]
	public string? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
		if (reader.TryReadNil())
			return null;
		if (reader.TryReadStringSpan(out var utf8))
			return Intern(utf8);

		// The string spans buffer segments: intern it from a contiguous copy, or just decode a long one.
		var sequence = reader.ReadStringSequence()!.Value;
		if (sequence.Length > MaxInternedBytes)
			return Encoding.UTF8.GetString(sequence);
		Span<byte> buffer = stackalloc byte[MaxInternedBytes];
		sequence.CopyTo(buffer);
		return Intern(buffer[..(int)sequence.Length]);
	}

	private static string Intern(ReadOnlySpan<byte> utf8) => utf8.Length switch {
		0 => string.Empty,
		> MaxInternedBytes => Encoding.UTF8.GetString(utf8),
		_ => Pool.GetOrAdd(utf8)
	};
}

/// <summary>
///   Plain (non-interning) string formatter, the target of <c>[DataCacheStringIntern(false)]</c>. MessagePack's
///   own <see cref="NullableStringFormatter"/> cannot be named by a member attribute: it has no public constructor.
/// </summary>
[ExcludeFormatterFromSourceGeneratedResolver]
public sealed class PragueStringFormatter : IMessagePackFormatter<string?> {
	/// <inheritdoc/>
	public void Serialize(ref MessagePackWriter writer, string? value, MessagePackSerializerOptions options)
		=> writer.Write(value);

	/// <inheritdoc/>
	public string? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
		=> reader.ReadString();
}
