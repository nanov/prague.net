namespace Prague.Benchmarks;

using BenchmarkDotNet.Running;

internal class Program {
	private static void Main(string[] args) {
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
	}
	/*
	if (args.Length > 0 && args[0].StartsWith("--memory")) {
		var seconds = 60;
		var eqIdx = args[0].IndexOf('=');
		if (eqIdx > 0 && int.TryParse(args[0][(eqIdx + 1)..], out var s))
			seconds = s;
		RunMemoryComparison(seconds);
	}
	else if (args.Length > 0 && args[0].StartsWith("--trace")) {
		var seconds = 60;
		var eqIdx = args[0].IndexOf('=');
		if (eqIdx > 0 && int.TryParse(args[0][(eqIdx + 1)..], out var s))
			seconds = s;
		RunTraceComparison(seconds);
	}
	else {
		BenchmarkRunner.Run<ConcurrentReadWriteBenchmark>();
	}
}

private static void RunMemoryComparison(int seconds) {
	// Attach dotMemory profiler
	DotMemory.Init();

	var config = new DotMemory.Config();
	config.SaveToDir("./snapshots");

	var benchmark = new ProfileBenchmark();

	Console.WriteLine("Setting up...");
	benchmark.Setup();

	DotMemory.Attach(config);

	// Force GC before starting
	GC.Collect();
	GC.WaitForPendingFinalizers();
	GC.Collect();

	// --- NOT POOLED ---
	var gen0Before = GC.CollectionCount(0);
	var gen1Before = GC.CollectionCount(1);
	var gen2Before = GC.CollectionCount(2);
	var memBefore = GC.GetTotalMemory(false);

	Console.WriteLine($"Running NOT POOLED benchmark ({seconds} seconds)...");
	DotMemory.GetSnapshot("Before NOT POOLED");
	var notPooledResult = benchmark.ConcurrentReadsWithWriter_NotPooled(seconds);
	DotMemory.GetSnapshot("After NOT POOLED");

	var gen0After = GC.CollectionCount(0);
	var gen1After = GC.CollectionCount(1);
	var gen2After = GC.CollectionCount(2);
	var memAfter = GC.GetTotalMemory(false);

	Console.WriteLine("NOT POOLED Results:");
	Console.WriteLine($"  Reads: {notPooledResult:N0}");
	Console.WriteLine($"  Gen0 collections: {gen0After - gen0Before}");
	Console.WriteLine($"  Gen1 collections: {gen1After - gen1Before}");
	Console.WriteLine($"  Gen2 collections: {gen2After - gen2Before}");
	Console.WriteLine($"  Memory delta: {(memAfter - memBefore) / 1024.0 / 1024.0:F2} MB");
	Console.WriteLine();

	// Force GC between tests
	GC.Collect();
	GC.WaitForPendingFinalizers();
	GC.Collect();

	// --- POOLED ---
	gen0Before = GC.CollectionCount(0);
	gen1Before = GC.CollectionCount(1);
	gen2Before = GC.CollectionCount(2);
	memBefore = GC.GetTotalMemory(false);

	Console.WriteLine($"Running POOLED benchmark ({seconds} seconds)...");
	DotMemory.GetSnapshot("Before POOLED");
	var pooledResult = benchmark.ConcurrentReadsWithWriter_Pooled(seconds);
	DotMemory.GetSnapshot("After POOLED");

	gen0After = GC.CollectionCount(0);
	gen1After = GC.CollectionCount(1);
	gen2After = GC.CollectionCount(2);
	memAfter = GC.GetTotalMemory(false);

	Console.WriteLine("POOLED Results:");
	Console.WriteLine($"  Reads: {pooledResult:N0}");
	Console.WriteLine($"  Gen0 collections: {gen0After - gen0Before}");
	Console.WriteLine($"  Gen1 collections: {gen1After - gen1Before}");
	Console.WriteLine($"  Gen2 collections: {gen2After - gen2Before}");
	Console.WriteLine($"  Memory delta: {(memAfter - memBefore) / 1024.0 / 1024.0:F2} MB");

	DotMemory.Detach();
	Console.WriteLine("\nDone! Snapshots saved to ./snapshots");
}

private static void RunTraceComparison(int seconds) {
	// Attach dotTrace profiler
	DotTrace.Init();

	var config = new DotTrace.Config();
	config.SaveToDir("./snapshots");

	var benchmark = new ProfileBenchmark();

	Console.WriteLine("Setting up...");
	benchmark.Setup();

	DotTrace.Attach(config);

	// --- NOT POOLED ---
	Console.WriteLine($"Running NOT POOLED benchmark ({seconds} seconds)...");
	DotTrace.StartCollectingData();
	var notPooledResult = benchmark.ConcurrentReadsWithWriter_NotPooled(seconds);
	DotTrace.SaveData();

	Console.WriteLine("NOT POOLED Results:");
	Console.WriteLine($"  Reads: {notPooledResult:N0}");
	Console.WriteLine();

	// --- POOLED ---
	Console.WriteLine($"Running POOLED benchmark ({seconds} seconds)...");
	DotTrace.StartCollectingData();
	var pooledResult = benchmark.ConcurrentReadsWithWriter_Pooled(seconds);
	DotTrace.SaveData();

	Console.WriteLine("POOLED Results:");
	Console.WriteLine($"  Reads: {pooledResult:N0}");

	DotTrace.Detach();
	Console.WriteLine("\nDone! Trace snapshots saved to ./snapshots");
}
*/
}
