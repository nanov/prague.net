namespace Prague.Kafka.Tests.DependencyInjection;

using Baseline.Scenario;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
///   A DI-aware filter fails while the handler is built — at startup, before a single message — never per message.
/// </summary>
[TestFixture]
public class FilterBuildFailureTests {
	private static KafkaCacheHandlerBuilder<BaselineProductCache, int, BaselineProduct> NewBuilder(
		IServiceCollection services)
		=> new KafkaCacheHandlersBuilder(services, "test").AddCache<BaselineProductCache, int, BaselineProduct>();

	private sealed class Thing;

	private interface IBox<T>;

	private sealed class Box<T> : IBox<T>;

	private sealed class OtherBox<T> : IBox<T>;

	/// <summary>
	///   Pins the container rule the scoped guard depends on: a closed registration wins over an open-generic one
	///   regardless of registration order, because the call-site factory looks up the exact service type first and
	///   only falls back to the generic definition when no exact descriptor exists.
	/// </summary>
	[Test]
	public void ClosedRegistration_WinsOverOpenGeneric_RegardlessOfOrder() {
		var closedFirst = new ServiceCollection();
		closedFirst.AddSingleton<IBox<int>, Box<int>>();
		closedFirst.AddScoped(typeof(IBox<>), typeof(OtherBox<>));

		var openFirst = new ServiceCollection();
		openFirst.AddScoped(typeof(IBox<>), typeof(OtherBox<>));
		openFirst.AddSingleton<IBox<int>, Box<int>>();

		using var closedFirstProvider = closedFirst.BuildServiceProvider();
		using var openFirstProvider = openFirst.BuildServiceProvider();

		Assert.Multiple(() => {
			Assert.That(closedFirstProvider.GetRequiredService<IBox<int>>(), Is.TypeOf<Box<int>>());
			Assert.That(openFirstProvider.GetRequiredService<IBox<int>>(), Is.TypeOf<Box<int>>());
		});
	}

	[Test]
	public void ClosedSingleton_WithAnOpenGenericScopedAlongside_IsAccepted() {
		var services = new ServiceCollection();
		services.AddSingleton<IBox<int>, Box<int>>();
		// Registered last, and scoped — but it never backs IBox<int> while the closed descriptor exists.
		services.AddScoped(typeof(IBox<>), typeof(OtherBox<>));
		var builder = NewBuilder(services);
		builder.WithKeyFilter<IBox<int>>(static (_, k) => k > 0);

		using var provider = services.BuildServiceProvider();

		Assert.DoesNotThrow(() => builder.BuildKeyFilters(provider));
	}

	[Test]
	public void ClosedScoped_WithAnOpenGenericSingletonAlongside_IsRejected() {
		var services = new ServiceCollection();
		services.AddScoped<IBox<int>, Box<int>>();
		// Registered last and a singleton, but the closed scoped descriptor is what actually gets resolved.
		services.AddSingleton(typeof(IBox<>), typeof(OtherBox<>));
		var builder = NewBuilder(services);
		builder.WithKeyFilter<IBox<int>>(static (_, k) => k > 0);

		using var provider = services.BuildServiceProvider();

		var ex = Assert.Throws<InvalidOperationException>(() => builder.BuildKeyFilters(provider));
		Assert.That(ex!.Message, Does.Contain("Scoped"));
	}

	[Test]
	public void ScopedStateService_IsRejectedAtBuild_WithAPragueMessage() {
		var services = new ServiceCollection();
		services.AddScoped<Thing>();
		var builder = NewBuilder(services);
		builder.WithKeyFilter<Thing>(static (_, k) => k > 0);

		// Note the provider is built the ordinary way: a default container hands a scoped service out of the root
		// silently, so Prague has to make this call itself rather than leaning on validateScopes.
		using var provider = services.BuildServiceProvider();

		var ex = Assert.Throws<InvalidOperationException>(() => builder.BuildKeyFilters(provider));
		Assert.That(ex!.Message, Does.Contain("Scoped").And.Contain(nameof(Thing)));
	}

	[Test]
	public void ScopedStateService_IsRejectedForValueFiltersToo() {
		var services = new ServiceCollection();
		services.AddScoped<Thing>();
		var builder = NewBuilder(services);
		builder.WithValueFilter<Thing>(static (_, v) => v.Id > 0);

		using var provider = services.BuildServiceProvider();

		Assert.Throws<InvalidOperationException>(() => builder.BuildValueFilters(provider));
	}

	[Test]
	public void SingletonAndTransientStateServices_AreAccepted() {
		var services = new ServiceCollection();
		services.AddSingleton<Thing>();
		var singleton = NewBuilder(services);
		singleton.WithKeyFilter<Thing>(static (_, k) => k > 0);

		var transientServices = new ServiceCollection();
		transientServices.AddTransient<Thing>();
		var transient = NewBuilder(transientServices);
		transient.WithKeyFilter<Thing>(static (_, k) => k > 0);

		using var singletonProvider = services.BuildServiceProvider();
		using var transientProvider = transientServices.BuildServiceProvider();

		Assert.Multiple(() => {
			Assert.That(singleton.BuildKeyFilters(singletonProvider).IsEmpty, Is.False);
			// A transient resolved once is captured for the process lifetime. That is surprising but not broken,
			// and it is exactly what the snapshot contract says happens.
			Assert.That(transient.BuildKeyFilters(transientProvider).IsEmpty, Is.False);
		});
	}

	[Test]
	public void KeyedScopedRegistration_DoesNotBlockANonKeyedSingleton() {
		var services = new ServiceCollection();
		services.AddSingleton<Thing>();
		// A keyed descriptor carries the same ServiceType, but GetRequiredService<Thing>() never resolves it.
		services.AddKeyedScoped<Thing>("other");
		var builder = NewBuilder(services);
		builder.WithKeyFilter<Thing>(static (_, k) => k > 0);

		using var provider = services.BuildServiceProvider();

		Assert.DoesNotThrow(() => builder.BuildKeyFilters(provider));
	}

	[Test]
	public void OpenGenericScopedRegistration_IsRejected() {
		var services = new ServiceCollection();
		services.AddScoped(typeof(IBox<>), typeof(Box<>));
		var builder = NewBuilder(services);
		builder.WithKeyFilter<IBox<int>>(static (_, k) => k > 0);

		using var provider = services.BuildServiceProvider();

		var ex = Assert.Throws<InvalidOperationException>(() => builder.BuildKeyFilters(provider));
		Assert.That(ex!.Message, Does.Contain("Scoped"));
	}

	[Test]
	public void UnregisteredStateService_ThrowsAtBuild() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services);
		builder.WithKeyFilter<Thing>(static (_, k) => k > 0);

		using var provider = services.BuildServiceProvider();

		Assert.Throws<InvalidOperationException>(() => builder.BuildKeyFilters(provider));
	}

	[Test]
	public void ThrowingStateFactory_ThrowsFromBuild_NotFromEvaluate() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services);
		builder.WithKeyFilter<string>(static _ => throw new InvalidOperationException("boom-from-build"),
			static (_, k) => k > 0);

		using var provider = services.BuildServiceProvider();

		var ex = Assert.Throws<InvalidOperationException>(() => builder.BuildKeyFilters(provider));
		Assert.That(ex!.Message, Is.EqualTo("boom-from-build"), "the original exception must reach the caller unwrapped");
	}

	[Test]
	public void NullArguments_AreRejectedAtRegistration() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services);

		Assert.Multiple(() => {
			Assert.Throws<ArgumentNullException>(() =>
				builder.WithKeyFilter(null!, static (string _, int k) => k > 0));
			Assert.Throws<ArgumentNullException>(() =>
				builder.WithKeyFilter(static _ => "state", (Func<string, int, bool>)null!));
			Assert.Throws<ArgumentNullException>(() =>
				builder.WithValueFilter(null!, static (string _, BaselineProduct v) => v.Id > 0));
		});
	}
}
