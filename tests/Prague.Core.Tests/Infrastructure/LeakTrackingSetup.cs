using Prague.Core.Collections;
using Prague.Core.Tests.Infrastructure;

// Global (namespace-less) SetUpFixture: runs before every fixture in the assembly, so the
// tracking provider is installed before any pooled collection type is first touched —
// PragueArrayPool<T>.Pool latches the provider at type init and never re-reads it.
[SetUpFixture]
public class LeakTrackingSetup {
	[OneTimeSetUp]
	public void InstallTrackingPools() => PragueArrayPool.Provider = new TrackingArrayPoolProvider();
}
