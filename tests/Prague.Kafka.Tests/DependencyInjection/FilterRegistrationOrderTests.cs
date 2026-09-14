namespace Prague.Kafka.Tests.DependencyInjection;

using Baseline.Scenario;
using Prague.Kafka.Filters;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Guards the single-ordered-list invariant. Eager and DI-aware registrations share one list because
///   <see cref="KafkaKeyFilters{TKey}" /> evaluation is first-reject-wins and the <i>rejecting</i> filter's own
///   <c>treatAsDelete</c> decides Skip vs Delete. An implementation that kept two lists and concatenated them at
///   build time would reorder the chain and silently change that outcome — nothing else in the suite catches it.
/// </summary>
[TestFixture]
public class FilterRegistrationOrderTests {
	private static KafkaCacheHandlerBuilder<BaselineProductCache, int, BaselineProduct> NewBuilder(
		IServiceCollection services)
		=> new KafkaCacheHandlersBuilder(services, "test").AddCache<BaselineProductCache, int, BaselineProduct>();

	private static BaselineProduct Product(int id) => new() { Id = id };

	private sealed class Marker;

	// FilterDecision is internal, so it cannot appear in a public signature — pass the ordinal.
	[TestCase(1, (int)FilterDecision.Skip)]
	[TestCase(2, (int)FilterDecision.Delete)]
	[TestCase(3, (int)FilterDecision.Delete)]
	[TestCase(4, (int)FilterDecision.Skip)]
	[TestCase(5, (int)FilterDecision.Accept)]
	public void InterleavedKeyFilters_PreserveRegistrationOrder_AndFirstRejectDecidesSkipVsDelete(
		int key, int expected) {
		var services = new ServiceCollection();
		services.AddSingleton<Marker>();
		var builder = NewBuilder(services);

		// eager, snapshot, eager, snapshot — alternating, with alternating treatAsDelete flags.
		builder.WithKeyFilter(static k => k != 1);
		builder.WithKeyFilter(static sp => sp.GetRequiredService<Marker>(), static (_, k) => k != 2,
			treatAsDelete: true);
		builder.WithKeyFilter(static k => k != 3, treatAsDelete: true);
		builder.WithKeyFilter(static sp => sp.GetRequiredService<Marker>(), static (_, k) => k != 4);

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildKeyFilters(provider);

		Assert.That((int)filters.Evaluate(key), Is.EqualTo(expected));
	}

	// FilterDecision is internal, so it cannot appear in a public signature — pass the ordinal.
	[TestCase(1, (int)FilterDecision.Skip)]
	[TestCase(2, (int)FilterDecision.Delete)]
	[TestCase(3, (int)FilterDecision.Delete)]
	[TestCase(4, (int)FilterDecision.Skip)]
	[TestCase(5, (int)FilterDecision.Accept)]
	public void InterleavedValueFilters_PreserveRegistrationOrder_AndFirstRejectDecidesSkipVsDelete(
		int id, int expected) {
		var services = new ServiceCollection();
		services.AddSingleton<Marker>();
		var builder = NewBuilder(services);

		builder.WithValueFilter(static v => v.Id != 1);
		builder.WithValueFilter(static sp => sp.GetRequiredService<Marker>(), static (_, v) => v.Id != 2,
			treatAsDelete: true);
		builder.WithValueFilter(static v => v.Id != 3, treatAsDelete: true);
		builder.WithValueFilter(static sp => sp.GetRequiredService<Marker>(), static (_, v) => v.Id != 4);

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildValueFilters(provider);

		Assert.That((int)filters.Evaluate(Product(id)), Is.EqualTo(expected));
	}

	[Test]
	public void WhenTwoFiltersBothReject_TheFirstOneDecides() {
		var services = new ServiceCollection();
		services.AddSingleton<Marker>();

		var skipFirst = NewBuilder(services);
		skipFirst.WithKeyFilter(static k => k != 7);
		skipFirst.WithKeyFilter(static sp => sp.GetRequiredService<Marker>(), static (_, k) => k != 7,
			treatAsDelete: true);

		var deleteFirst = NewBuilder(services);
		deleteFirst.WithKeyFilter(static sp => sp.GetRequiredService<Marker>(), static (_, k) => k != 7,
			treatAsDelete: true);
		deleteFirst.WithKeyFilter(static k => k != 7);

		using var provider = services.BuildServiceProvider();

		Assert.Multiple(() => {
			Assert.That(skipFirst.BuildKeyFilters(provider).Evaluate(7), Is.EqualTo(FilterDecision.Skip));
			Assert.That(deleteFirst.BuildKeyFilters(provider).Evaluate(7), Is.EqualTo(FilterDecision.Delete));
		});
	}

	[Test]
	public void SnapshotFilters_ComposeWithAnd_AcrossMultipleCalls() {
		var services = new ServiceCollection();
		services.AddSingleton<Marker>();
		var builder = NewBuilder(services);
		builder.WithKeyFilter(static sp => sp.GetRequiredService<Marker>(), static (_, k) => k > 1);
		builder.WithKeyFilter(static sp => sp.GetRequiredService<Marker>(), static (_, k) => k < 4);

		using var provider = services.BuildServiceProvider();
		var filters = builder.BuildKeyFilters(provider);

		Assert.Multiple(() => {
			Assert.That(filters.Evaluate(1), Is.EqualTo(FilterDecision.Skip));
			Assert.That(filters.Evaluate(2), Is.EqualTo(FilterDecision.Accept));
			Assert.That(filters.Evaluate(3), Is.EqualTo(FilterDecision.Accept));
			Assert.That(filters.Evaluate(4), Is.EqualTo(FilterDecision.Skip));
		});
	}

	[Test]
	public void NoFilters_StaysOnTheEmptyFastPath() {
		var services = new ServiceCollection();
		var builder = NewBuilder(services);

		using var provider = services.BuildServiceProvider();

		Assert.Multiple(() => {
			Assert.That(builder.BuildKeyFilters(provider).IsEmpty, Is.True);
			Assert.That(builder.BuildValueFilters(provider).IsEmpty, Is.True);
		});
	}
}
