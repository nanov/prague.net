namespace Prague.Benchmarks.Interning;

using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.NET.StringTools;
using Prague.Kafka.SerDe;

/// <summary>
///   Interning cost per string of <see cref="Utf8WeakInternPool"/> against plain decode (no interning) and
///   v3's interner (StringTools 17.11.4 <c>Strings.WeakIntern</c> over stack-decoded chars). The pool's
///   lock-free design and <see cref="HashCode"/> were chosen with this benchmark against a locked variant
///   and seeded XxHash3/XxHash32 (RESULTS.MD § String interning).
///
///   Scenarios: <c>Hit</c> — a warm, rooted corpus of 1024 values; <c>Churn</c> — every string is new
///   and unrooted (miss + insert, collection, rebuild); each single-threaded and across
///   <see cref="Threads"/> threads. Every method reports ns per string.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("StringIntern")]
public class StringInternBenchmarks {
	private const int CorpusSize = 1024;
	private const int ChurnSize = 1 << 18;
	private const int HitRounds = 16;
	private const int Threads = 8;

	private byte[][] _corpus = null!;
	private byte[][] _churn = null!;
	private string[] _roots = null!;
	private int _churnCursor;

	private Utf8WeakInternPool _pool = null!;

	[Params("Hit", "HitParallel", "Churn", "ChurnParallel")]
	public string Scenario { get; set; } = "Hit";

	[GlobalSetup]
	public void Setup() {
		// Realistic short values: codes, statuses, names — mostly ASCII, 1 in 16 non-ASCII.
		_corpus = new byte[CorpusSize][];
		for (var i = 0; i < CorpusSize; i++)
			_corpus[i] = Encoding.UTF8.GetBytes(Value("value", i));
		_churn = new byte[ChurnSize][];
		for (var i = 0; i < ChurnSize; i++)
			_churn[i] = Encoding.UTF8.GetBytes(Value("churn", i));

		_pool = new Utf8WeakInternPool();

		// Hit scenarios need every corpus value interned and rooted in each implementation.
		_roots = new string[CorpusSize * 2];
		for (var i = 0; i < CorpusSize; i++) {
			_roots[i * 2] = _pool.GetOrAdd(_corpus[i]);
			_roots[i * 2 + 1] = StringToolsIntern(_corpus[i]);
		}
	}

	[GlobalCleanup]
	public void Cleanup() => _pool.Dispose();

	[Benchmark(Baseline = true, OperationsPerInvoke = CorpusSize * HitRounds)]
	public int Decode() => Run(static (_, b) => Encoding.UTF8.GetString(b));

	[Benchmark(OperationsPerInvoke = CorpusSize * HitRounds)]
	public int StringToolsV3() => Run(static (_, b) => StringToolsIntern(b));

	[Benchmark(OperationsPerInvoke = CorpusSize * HitRounds)]
	public int PraguePool() => Run(static (s, b) => s._pool.GetOrAdd(b));

	/// <summary>
	///   Runs CorpusSize × HitRounds lookups in total, split over <see cref="Threads"/> threads in the
	///   parallel scenarios, so ns/op stays comparable across scenarios (wall time per string).
	/// </summary>
	private int Run(Func<StringInternBenchmarks, byte[], string> intern) {
		const int total = CorpusSize * HitRounds;
		switch (Scenario) {
			case "Hit": {
				var sum = 0;
				for (var i = 0; i < total; i++)
					sum += intern(this, _corpus[i & (CorpusSize - 1)]).Length;
				return sum;
			}
			case "HitParallel": {
				var sum = 0;
				Parallel.For(0, Threads, t => {
					var local = 0;
					for (var i = 0; i < total / Threads; i++)
						local += intern(this, _corpus[(i + t * 97) & (CorpusSize - 1)]).Length;
					Interlocked.Add(ref sum, local);
				});
				return sum;
			}
			case "Churn": {
				var sum = 0;
				var cursor = _churnCursor;
				for (var i = 0; i < total; i++)
					sum += intern(this, _churn[(cursor + i) & (ChurnSize - 1)]).Length;
				_churnCursor = cursor + total;
				return sum;
			}
			default: {
				var sum = 0;
				var cursor = _churnCursor;
				Parallel.For(0, Threads, t => {
					var local = 0;
					var start = cursor + t * (total / Threads);
					for (var i = 0; i < total / Threads; i++)
						local += intern(this, _churn[(start + i) & (ChurnSize - 1)]).Length;
					Interlocked.Add(ref sum, local);
				});
				_churnCursor = cursor + total;
				return sum;
			}
		}
	}

	private static string Value(string prefix, int i)
		=> i % 16 == 0 ? prefix + "-ключ-" + i : prefix + "-status-" + (i * 2654435761u).ToString("x");

	/// <summary>v3's StringInterningFormatter: decode into a stack buffer, then <c>Strings.WeakIntern</c>.</summary>
	[SkipLocalsInit]
	private static string StringToolsIntern(byte[] utf8) {
		Span<char> chars = stackalloc char[utf8.Length];
		var count = Encoding.UTF8.GetChars(utf8, chars);
		return Strings.WeakIntern(chars[..count]);
	}
}
