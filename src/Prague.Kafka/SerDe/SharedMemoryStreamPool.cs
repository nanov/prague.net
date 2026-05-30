namespace Prague.Kafka.SerDe;

using Microsoft.IO;

public static class SharedMemoryStreamPool {
	private static readonly RecyclableMemoryStreamManager _manager = new(new RecyclableMemoryStreamManager.Options());

	public static RecyclableMemoryStream Rent(ReadOnlySpan<byte> bytes) {
		var stream = _manager.GetStream(nameof(SharedMemoryStreamPool), bytes.Length);
		stream.Write(bytes);
		return stream;
	}

	public static void Return(Stream stream) {
		stream.Dispose();
	}
}