namespace Prague.Kafka.SerDe;

using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Confluent.Kafka;
using IO;
using MessagePack;
#if NET9_0_OR_GREATER
using Ascii = System.Text.Ascii;

#else
using Ascii = System.MemoryExtensions;
#endif

internal class RentedBytesConnectedDeserializer : IDeserializer<RentedBytes> {
	private readonly HeadersFilteringWithHandlerRentedBytesDeserializer _connectedDeserializer;

	public RentedBytesConnectedDeserializer(HeadersFilteringWithHandlerRentedBytesDeserializer connectedDeserializer) {
		_connectedDeserializer = connectedDeserializer;
	}

	public RentedBytes Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context) {
		// filter or error
		return _connectedDeserializer.IsFiltered || _connectedDeserializer.Error is not null
			? RentedBytes.Irrelevant()
			: new RentedBytes(isNull, data);
	}
}

internal class HeadersFilteringWithHandlerRentedBytesDeserializer : IDeserializer<RentedBytesWithHandler> {
	private readonly FrozenDictionary<string, KafkaCacheHandler> _handlers;
	private readonly byte[] _instanceIdBytes;
	private readonly string _producerInstanceIdHeaderName;
	internal Exception? Error = null;
	internal KafkaCacheHandler? Handler = null;
	internal bool IsFiltered;

	public HeadersFilteringWithHandlerRentedBytesDeserializer(string producerInstanceIdHeaderName, byte[] instanceIdBytes,
		FrozenDictionary<string, KafkaCacheHandler> handlers) {
		_producerInstanceIdHeaderName = producerInstanceIdHeaderName;
		_instanceIdBytes = instanceIdBytes;
		_handlers = handlers;
	}

	public RentedBytesWithHandler Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context) {
		if (!_handlers.TryGetValue(context.Topic, out var handler))
			return RentedBytesWithHandler.NotFound();

		var headersBackingList = context.Headers?.BackingList;
		var handlerFilters = handler.HeadersFilters;
		var initalFilterState = handlerFilters.InitialState;
		if (headersBackingList is not null) {
			// TODO: exception handling
			var headers = CollectionsMarshal.AsSpan(Unsafe.As<List<IHeader>>(headersBackingList));
			foreach (ref readonly var header in headers) {
				if (Ascii.Equals(_producerInstanceIdHeaderName, header.Key))
					if (header.GetValueBytes().AsSpan().SequenceEqual(_instanceIdBytes)) {
						IsFiltered = true;
						return RentedBytesWithHandler.Filtered();
					}

				if (!handlerFilters.ShouldProcess(ref initalFilterState, header.Key, header.GetValueBytes())) {
					// TODO: handle serde error
					IsFiltered = true;
					return RentedBytesWithHandler.Filtered();
				}
			}
		}

		if (!initalFilterState) {
			IsFiltered = true;
			return RentedBytesWithHandler.Filtered();
		}

		IsFiltered = false;

		return new RentedBytesWithHandler(handler, isNull, data, false);
	}
}

public static class CacheSerde<T> {
	public static T Deserialize(RentedBytes bytes)
		=> MessagePackSerializer.Deserialize<T>(bytes.AsMemory(), PragueMessagePack.Options);

	internal static T Deserialize(RentedBytesWithHandler bytes)
		=> MessagePackSerializer.Deserialize<T>(bytes.AsMemory(), PragueMessagePack.Options);

	public static byte[] Serialize(T value)
		=> MessagePackSerializer.Serialize<T>(value, PragueMessagePack.Options);
}
