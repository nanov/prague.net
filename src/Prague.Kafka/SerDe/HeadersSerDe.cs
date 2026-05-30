namespace Prague.Kafka.SerDe;

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using MessagePack;

/// <summary>
///   Provides serialization and deserialization methods for Kafka headers.
/// </summary>
public static class HeadersSerDe {
	/// <summary>
	///   Serializes an int value to bytes for Kafka header.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte[] SerializeInt(int value)
		=> BitConverter.GetBytes(value);

	/// <summary>
	///   Serializes a long value to bytes for Kafka header.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte[] SerializeLong(long value)
		=> BitConverter.GetBytes(value);

	/// <summary>
	///   Serializes a string value to UTF-8 bytes for Kafka header.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte[] SerializeString(string value)
		=> Encoding.UTF8.GetBytes(value);

	/// <summary>
	///   Serializes a Guid value to bytes for Kafka header.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte[] SerializeGuid(Guid value)
		=> value.ToByteArray();

	/// <summary>
	///   Tries to deserialize an int value from Kafka header bytes.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryDeserializeInt(ReadOnlySpan<byte> bytes, out int value) {
		if (bytes.Length == 4) {
			value = BitConverter.ToInt32(bytes);
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	///   Tries to deserialize a long value from Kafka header bytes.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryDeserializeLong(ReadOnlySpan<byte> bytes, out long value) {
		if (bytes.Length == 8) {
			value = BitConverter.ToInt64(bytes);
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	///   Deserializes a string value from UTF-8 Kafka header bytes.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string DeserializeString(ReadOnlySpan<byte> bytes)
		=> Encoding.UTF8.GetString(bytes);

	/// <summary>
	///   Tries to deserialize a Guid value from Kafka header bytes.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryDeserializeGuid(ReadOnlySpan<byte> bytes, out Guid value) {
		if (bytes.Length == 16) {
			value = new Guid(bytes);
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	///   Serializes an object using MessagePack for Kafka header.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte[] SerializeMessagePack<T>(T value) {
		return MessagePackSerializer.Serialize(value, PragueMessagePack.Options);
	}

	/// <summary>
	///   Tries to deserialize an object using MessagePack from Kafka header bytes.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryDeserializeMessagePack<T>(byte[] bytes, out T? value) {
		try {
			value = MessagePackSerializer.Deserialize<T>(bytes, PragueMessagePack.Options);
			return true;
		} catch {
			value = default;
			return false;
		}
	}

	/// <summary>
	///   Tries to deserialize an object using MessagePack from Kafka header bytes,
	///   succeeding only when all bytes in the buffer are consumed by the MessagePack reader.
	///   Rejects byte sequences where MessagePack reads fewer bytes than available
	///   (e.g. raw BitConverter bytes whose first byte happens to be a valid fixint).
	/// </summary>
	public static bool TryDeserializeMessagePackExact<T>(byte[] bytes, out T? value) {
		try {
			value = MessagePackSerializer.Deserialize<T>(bytes.AsMemory(), options: PragueMessagePack.Options, out var bytesRead);
			if (bytesRead == bytes.Length) {
				return true;
			}
			value = default;
			return false;
		} catch {
			value = default;
			return false;
		}
	}
}