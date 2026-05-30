namespace Prague.Benchmarks;

using System.Buffers;
using System.Threading.Channels;
using Prague.Kafka.Utils;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class SpscByteRingBufferBenchmarks {
	private SpscByteRingBuffer _ringBuffer = null!;
	private byte[] _keyData = null!;

	private const int Ops = 1000;

	[Params(16, 64, 256)]
	public int KeySize;

	[GlobalSetup]
	public void Setup() {
		_ringBuffer = new SpscByteRingBuffer(65536);
		_keyData = new byte[KeySize];
		Random.Shared.NextBytes(_keyData);
	}

	[GlobalCleanup]
	public void Cleanup() {
		_ringBuffer.Dispose();
	}

	[Benchmark(Baseline = true, Description = "ArrayPool single-thread")]
	public void ArrayPoolSingleThread() {
		var pool = ArrayPool<byte>.Shared;
		for (var i = 0; i < Ops; i++) {
			var buf = pool.Rent(KeySize);
			_keyData.CopyTo(buf, 0);
			pool.Return(buf);
		}
	}

	[Benchmark(Description = "RingBuffer single-thread")]
	public void RingBufferSingleThread() {
		for (var i = 0; i < Ops; i++) {
			var mem = _ringBuffer.Rent(KeySize, out var pooled);
			_keyData.CopyTo(mem.Span);
			if (pooled)
				_ringBuffer.Return(KeySize);
		}
	}

	[Benchmark(Description = "ArrayPool SPSC via Channel")]
	public async Task ArrayPoolSpsc() {
		var pool = ArrayPool<byte>.Shared;
		var channel = Channel.CreateBounded<(byte[] buf, int len)>(16);

		var consumer = Task.Run(async () => {
			await foreach (var (buf, _) in channel.Reader.ReadAllAsync())
				pool.Return(buf);
		});

		for (var i = 0; i < Ops; i++) {
			var buf = pool.Rent(KeySize);
			_keyData.CopyTo(buf, 0);
			await channel.Writer.WriteAsync((buf, KeySize));
		}

		channel.Writer.Complete();
		await consumer;
	}

	[Benchmark(Description = "RingBuffer SPSC via Channel")]
	public async Task RingBufferSpsc() {
		var ring = _ringBuffer;
		var channel = Channel.CreateBounded<(int, bool)>(16);

		var consumer = Task.Run(async () => {
			await foreach (var size in channel.Reader.ReadAllAsync())
				if (size.Item2)
					ring.Return(size.Item1);
		});

		for (var i = 0; i < Ops; i++) {
			var mem = ring.Rent(KeySize, out var pooled);
			_keyData.CopyTo(mem.Span);
			await channel.Writer.WriteAsync((KeySize, pooled));
		}

		channel.Writer.Complete();
		await consumer;
	}
}
