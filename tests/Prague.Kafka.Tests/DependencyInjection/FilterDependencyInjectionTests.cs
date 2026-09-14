namespace Prague.Kafka.Tests.DependencyInjection;

using System.Collections.Frozen;
using Baseline.Scenario;
using Prague.Kafka.Filters;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Covers the DI-aware filter overloads: state is resolved once, while the handler is built, and is then immutable
///   for the life of the handler. These run without a broker — the builder is constructible in-process and
///   <c>BuildKeyFilters</c> / <c>BuildValueFilters</c> expose the built chain without reflection.
/// </summary>
[TestFixture]
public class FilterDependencyInjectionTests {
	private static KafkaCacheHandlerBuilder<BaselineProductCache, int, BaselineProduct> NewBuilder(
		IServiceCollection services)
		=> new KafkaCacheHandlersBuilder(services, "test").AddCache<BaselineProductCache, int, BaselineProduct>();

	private static BaselineProduct Product(int id) => new() { Id = id };

	private sealed class AllowList {
		public HashSet<int> Ids { get; } = [1, 2];
	}

	private sealed class Clock {
		public bool IsOpen { get; set; } = true;
	}

	[Test]
	public void StateFactory_RunsExactlyOnce_NoMatterHowManyMessagesAreEvaluated() {
		var services = new ServiceCollection();
		services.AddSingleton<AllowList>();
		var calls = 0;
		var builder = NewBuilder(services);
		builder.WithKeyFilter(sp => {
			calls++;
			return sp.GetRequiredService<AllowList>().Ids.ToFrozenSet();
		}, static (ids, key) => ids.Contains(key));

		Assert.That(calls, Is.Zero, "registration must not resolve anything");

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildKeyFilters(provider);

		for (var i = 0; i < 1_000; i++)
			filters.Evaluate(1);

		Assert.That(calls, Is.EqualTo(1));
	}

	[Test]
	public void ValueStateFactory_RunsExactlyOnce_NoMatterHowManyMessagesAreEvaluated() {
		var services = new ServiceCollection();
		services.AddSingleton<AllowList>();
		var calls = 0;
		var builder = NewBuilder(services);
		builder.WithValueFilter(sp => {
			calls++;
			return sp.GetRequiredService<AllowList>().Ids.ToFrozenSet();
		}, static (ids, value) => ids.Contains(value.Id));

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildValueFilters(provider);

		for (var i = 0; i < 1_000; i++)
			filters.Evaluate(Product(1));

		Assert.That(calls, Is.EqualTo(1));
	}

	[Test]
	public void Snapshot_IsFrozenAtBuild_AndIgnoresLaterServiceMutation() {
		var services = new ServiceCollection();
		var allow = new AllowList();
		services.AddSingleton(allow);
		var builder = NewBuilder(services);
		builder.WithKeyFilter(static sp => sp.GetRequiredService<AllowList>().Ids.ToFrozenSet(),
			static (ids, key) => ids.Contains(key));

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildKeyFilters(provider);
		Assert.That(filters.Evaluate(9), Is.EqualTo(FilterDecision.Skip));

		// The operator widens the allow-list at runtime. The snapshot does not move: reloading it is a restart,
		// which re-reads the topic from Earliest under the new state. This is the documented contract.
		allow.Ids.Add(9);

		Assert.That(filters.Evaluate(9), Is.EqualTo(FilterDecision.Skip));
	}

	[Test]
	public void ServiceOverload_CapturesTheServiceItself_SoItObservesLaterMutation() {
		var services = new ServiceCollection();
		var allow = new AllowList();
		services.AddSingleton(allow);
		var builder = NewBuilder(services);
		builder.WithKeyFilter<AllowList>(static (list, key) => list.Ids.Contains(key));

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildKeyFilters(provider);
		Assert.That(filters.Evaluate(9), Is.EqualTo(FilterDecision.Skip));

		allow.Ids.Add(9);

		// The contrast with the snapshot test above is the whole point of having both overloads.
		Assert.That(filters.Evaluate(9), Is.EqualTo(FilterDecision.Accept));
	}

	[Test]
	public void NamedTuple_InjectsMoreThanOneService_AndKeepsElementNames() {
		var services = new ServiceCollection();
		var clock = new Clock();
		services.AddSingleton<AllowList>();
		services.AddSingleton(clock);
		var builder = NewBuilder(services);
		builder.WithKeyFilter(
			static sp => (allow: sp.GetRequiredService<AllowList>(), clock: sp.GetRequiredService<Clock>()),
			static (s, key) => s.allow.Ids.Contains(key) && s.clock.IsOpen);

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildKeyFilters(provider);

		Assert.That(filters.Evaluate(1), Is.EqualTo(FilterDecision.Accept));

		clock.IsOpen = false;

		Assert.That(filters.Evaluate(1), Is.EqualTo(FilterDecision.Skip));
	}

	[Test]
	public void NamedTuple_SnapshotsBothServices_AtBuildTime() {
		var services = new ServiceCollection();
		var allow = new AllowList();
		var clock = new Clock();
		services.AddSingleton(allow);
		services.AddSingleton(clock);
		var builder = NewBuilder(services);
		builder.WithValueFilter(
			static sp => (
				ids: sp.GetRequiredService<AllowList>().Ids.ToFrozenSet(),
				isOpen: sp.GetRequiredService<Clock>().IsOpen),
			static (s, value) => s.isOpen && s.ids.Contains(value.Id));

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildValueFilters(provider);
		Assert.That(filters.Evaluate(Product(1)), Is.EqualTo(FilterDecision.Accept));

		allow.Ids.Clear();
		clock.IsOpen = false;

		Assert.That(filters.Evaluate(Product(1)), Is.EqualTo(FilterDecision.Accept),
			"both tuple elements were snapshotted, so neither mutation is visible");
	}

	[Test]
	public void StateFactory_RunsOncePerServiceProvider_NotMemoizedOnTheBuilder() {
		var services = new ServiceCollection();
		services.AddSingleton<AllowList>();
		var calls = 0;
		var builder = NewBuilder(services);
		builder.WithKeyFilter(sp => {
			calls++;
			return sp.GetRequiredService<AllowList>().Ids.ToFrozenSet();
		}, static (ids, key) => ids.Contains(key));

		// The builder is captured in the keyed-singleton closure and outlives any one provider, so a memoised
		// snapshot on the builder would leak one container's state into the next.
		using (var first = services.BuildServiceProvider())
			builder.BuildKeyFilters(first);
		using (var second = services.BuildServiceProvider())
			builder.BuildKeyFilters(second);

		Assert.That(calls, Is.EqualTo(2));
	}

	[Test]
	public void StaticPredicate_AllocatesOneDelegate_ForTheProcess() {
		var services = new ServiceCollection();
		services.AddSingleton<AllowList>();

		// Registering the same static lambda twice must hand the builder the same cached delegate instance —
		// that caching is what makes the ingestion path allocation-free.
		var builder = NewBuilder(services);
		builder.WithKeyFilter(static sp => sp.GetRequiredService<AllowList>().Ids.ToFrozenSet(),
			static (ids, key) => ids.Contains(key));
		builder.WithKeyFilter(static sp => sp.GetRequiredService<AllowList>().Ids.ToFrozenSet(),
			static (ids, key) => ids.Contains(key));

		using var provider = services.BuildServiceProvider();
		var before = GC.GetAllocatedBytesForCurrentThread();
		var filters = builder.BuildKeyFilters(provider);
		for (var i = 0; i < 10_000; i++)
			filters.Evaluate(1);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.LessThan(4_096),
			$"evaluating a built snapshot filter must not allocate; saw {allocated} bytes");
	}
	/// <summary>
	///   The seam between the two features: a DI-resolved header filter must judge a value without claiming to
	///   require its header, so it must not take a bit in the requirement mask. Only WithHeaderExistsFilter does.
	/// </summary>
	[Test]
	public void HeaderStateFactory_RunsOnce_AndDoesNotClaimARequirementBit() {
		var services = new ServiceCollection();
		services.AddSingleton<AllowList>();
		var calls = 0;
		var builder = NewBuilder(services);
		builder.WithHeaderExistsFilter("tenant");
		builder.WithHeaderFilter<AllowList, int>("ts", sp => {
			calls++;
			return sp.GetRequiredService<AllowList>();
		}, static (allow, ts) => allow.Ids.Contains(ts));

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildHeaderFilters(provider);

		Assert.Multiple(() => {
			Assert.That(calls, Is.EqualTo(1), "the state factory runs once, at build");
			Assert.That(filters.RequiredMask, Is.EqualTo(1UL), "only the exists filter requires its header");
		});

		// And the DI-resolved predicate still decides: 1 is in the allow-list, 9 is not (MessagePack fixint).
		ulong seen = 0;
		Assert.Multiple(() => {
			Assert.That(filters.ShouldProcess(ref seen, "ts"u8, [0x01]), Is.True);
			ulong other = 0;
			Assert.That(filters.ShouldProcess(ref other, "ts"u8, [0x09]), Is.False);
		});
	}
}
