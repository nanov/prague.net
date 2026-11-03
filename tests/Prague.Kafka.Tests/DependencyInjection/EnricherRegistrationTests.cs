namespace Prague.Kafka.Tests.DependencyInjection;

using Baseline.Scenario;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Covers <c>WithEnricher</c>: both overloads register a singleton <see cref="ICacheEnricher{TKey,TValue}"/> keyed
///   by the cache type, registration resolves nothing, and <c>BuildEnrichers</c> hands the handler exactly this
///   cache's enrichers in registration order. Broker-free: the builder is constructible in-process and
///   <c>BuildEnrichers</c> exposes the built array.
/// </summary>
[TestFixture]
public class EnricherRegistrationTests {
	private static KafkaCacheHandlerBuilder<BaselineProductCache, int, BaselineProduct> NewBuilder(
		IServiceCollection services)
		=> new KafkaCacheHandlersBuilder(services, "test").AddCache<BaselineProductCache, int, BaselineProduct>();

	private sealed class FirstEnricher : ICacheEnricher<int, BaselineProduct> {
		public void Enrich(int key, BaselineProduct value) { }
	}

	private sealed class SecondEnricher : ICacheEnricher<int, BaselineProduct> {
		public void Enrich(int key, BaselineProduct value) { }
	}

	[Test]
	public void TypeOverload_RegistersOneSingleton_KeyedByTheCacheType() {
		var services = new ServiceCollection();
		NewBuilder(services).WithEnricher<FirstEnricher>();

		using var provider = services.BuildServiceProvider();
		var keyed = provider.GetKeyedServices<ICacheEnricher<int, BaselineProduct>>(typeof(BaselineProductCache)).ToArray();

		Assert.That(keyed, Has.Length.EqualTo(1));
		Assert.That(keyed[0], Is.TypeOf<FirstEnricher>());
		Assert.That(provider.GetRequiredKeyedService<ICacheEnricher<int, BaselineProduct>>(typeof(BaselineProductCache)),
			Is.SameAs(keyed[0]), "the type overload registers a singleton");
		Assert.That(provider.GetServices<ICacheEnricher<int, BaselineProduct>>(), Is.Empty,
			"nothing lands in the unkeyed slot, so no other cache can pick it up");
	}

	[Test]
	public void FactoryOverload_ResolvesThroughTheFactory_Once() {
		var services = new ServiceCollection();
		var calls = 0;
		NewBuilder(services).WithEnricher(sp => {
			calls++;
			return new FirstEnricher();
		});

		Assert.That(calls, Is.Zero, "registration must not resolve anything");

		using var provider = services.BuildServiceProvider();
		var first = provider.GetRequiredKeyedService<ICacheEnricher<int, BaselineProduct>>(typeof(BaselineProductCache));
		var second = provider.GetRequiredKeyedService<ICacheEnricher<int, BaselineProduct>>(typeof(BaselineProductCache));

		Assert.That(calls, Is.EqualTo(1));
		Assert.That(second, Is.SameAs(first));
		Assert.That(provider.GetServices<ICacheEnricher<int, BaselineProduct>>(), Is.Empty,
			"the factory overload registers only under the cache key");
	}

	[Test]
	public void BuildEnrichers_ReturnsTheEnrichersInRegistrationOrder() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services)
			.WithEnricher<FirstEnricher>()
			.WithEnricher<SecondEnricher>();

		using var provider = services.BuildServiceProvider();
		var enrichers = builder.BuildEnrichers(provider);

		Assert.That(enrichers, Has.Length.EqualTo(2));
		Assert.That(enrichers[0], Is.TypeOf<FirstEnricher>());
		Assert.That(enrichers[1], Is.TypeOf<SecondEnricher>());
	}

	[Test]
	public void BuildEnrichers_IgnoresEnrichersRegisteredForAnotherCache() {
		var services = new ServiceCollection();
		// Another cache type with the same key and value types would register under its own type as the key;
		// any other key stands in for it here. The unkeyed slot stands in for a registration that bypassed the
		// builder.
		services.AddKeyedSingleton<ICacheEnricher<int, BaselineProduct>, SecondEnricher>(typeof(string));
		services.AddSingleton<ICacheEnricher<int, BaselineProduct>, SecondEnricher>();
		var builder = NewBuilder(services).WithEnricher<FirstEnricher>();

		using var provider = services.BuildServiceProvider();
		var enrichers = builder.BuildEnrichers(provider);

		Assert.That(enrichers, Has.Length.EqualTo(1));
		Assert.That(enrichers[0], Is.TypeOf<FirstEnricher>());
	}

	[Test]
	public void BuildEnrichers_WithoutRegistrations_IsEmpty() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services);

		using var provider = services.BuildServiceProvider();

		Assert.That(builder.BuildEnrichers(provider), Is.Empty);
	}
}
