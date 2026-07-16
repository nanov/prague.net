namespace Prague.Kafka;

using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Core;
using Filters;
using Internal;
using IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Options;

public abstract class KafkaCacheHandlerBuilder : KafkaCacheHandlersBuilder {
	protected KafkaCacheHandlerBuilder(KafkaCacheHandlersBuilder cacheHandlersBuilder) : base(cacheHandlersBuilder) {
	}

	internal abstract KeyValuePair<string, KafkaCacheHandler> Build(IServiceProvider sp,
		IReadOnlyDictionary<string, string> vars);
}

public class KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> : KafkaCacheHandlerBuilder
	where TKey : IEquatable<TKey>, IComparable<TKey>
	where TValue : class, IDataCacheItem<TKey, TValue>, IEnrichable<TValue>, ICacheEquatable<TValue>,
	ICacheClonable<TValue>
	where TCacheEntity : class, IDataCache<TKey, TValue>, IKafkaProducerConfigurable<TCacheEntity>,
	IKafkaConfigurable<TCacheEntity> {
	private readonly Func<IServiceProvider, string>? _topicNameResolver;

	// private Type _afterHandler
	private Dictionary<string, List<KafkaHeaderFilterExecutor>>? _filters;
	private List<KafkaKeyFilter<TKey>>? _keyFilters;
	private List<KafkaValueFilter<TValue>>? _valueFilters;

	public KafkaCacheHandlerBuilder(Func<IServiceProvider, string>? topicNameResolver,
		KafkaCacheHandlersBuilder builder) :
		base(builder) {
		_topicNameResolver = topicNameResolver;
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithAfterHandler<TAfterHandler>()
		where TAfterHandler : class, ICacheAfterHandler<TKey, TValue> {
		Services.AddSingleton<ICacheAfterHandler<TKey, TValue>, TAfterHandler>();
		return this;
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithAfterHandler<TAfterHandler>(
		Func<IServiceProvider, TAfterHandler> builder)
		where TAfterHandler : class, ICacheAfterHandler<TKey, TValue> {
		Services.AddSingleton<ICacheAfterHandler<TKey, TValue>>(builder);
		return this;
	}

		/// <summary>
		/// Allowing messages to be filtered by header using a custom predicate.
		/// The predicate is invoked on every message with the deserialized header value.
		/// Supports dynamic expressions (e.g. it =&gt; it &gt;= DateTime.UtcNow.AddSeconds(-24)).
		/// </summary>
		/// <typeparam name="THeaderValue">Header value type (struct).</typeparam>
		/// <param name="headerName">Kafka header name to evaluate.</param>
		/// <param name="predicate">Predicate that receives the deserialized header value and returns true to pass the filter.</param>
		/// <param name="passOnNull">
		/// If true, messages with a null (MessagePack nil) header value will pass the filter.
		/// If false, messages with a null (MessagePack nil) header value will be filtered out.
		/// </param>
		/// <returns>The builder instance for chaining.</returns>
		public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderFilter<THeaderValue>(
			string headerName,
			Func<THeaderValue, bool> predicate,
			bool passOnNull = true)
			where THeaderValue : struct {
				_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();

				ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
				if (!exists) {
						list = new List<KafkaHeaderFilterExecutor>();
				}

				list!.Add(new KafkaHeaderPredicateFilter<THeaderValue>(predicate, passOnNull));

				return this;
		}

		public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderExistsFilter(string headerName) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();
		list!.Add(new KafkaHeaderExistsFilter());
		return this;
	}

	// Specialized overload for strings - uses UTF8 bytes for better performance (avoids allocation)
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, string value) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();
		list!.Add(new KafkaHeaderEqualsStringFilter(value));
		return this;
	}

	// Specialized overload for multiple strings - creates OR filter
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, string value1,
		string value2, params string[] moreValues) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();

		var totalLength = 2 + moreValues.Length;
		var filters = new KafkaHeaderFilter[totalLength];
		filters[0] = new KafkaHeaderEqualsStringFilter(value1);
		filters[1] = new KafkaHeaderEqualsStringFilter(value2);
		for (var i = 0; i < moreValues.Length; i++) filters[i + 2] = new KafkaHeaderEqualsStringFilter(moreValues[i]);
		list!.Add(new KafkaHeaderEqualsMultiFilter(filters));
		return this;
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter<THeaderValue>(string headerName,
		THeaderValue value) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();
		list!.Add(new KafkaHeaderEqualsFilter<THeaderValue>(value));
		return this;
	}

	// Specialized overload for strings - uses UTF8 bytes for better performance (avoids allocation)
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>
		WithHeaderNotEqualsFilter(string headerName, string value) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();
		list!.Add(new KafkaHeaderNotEqualsStringFilter(value));
		return this;
	}

	// Specialized overload for int - uses big-endian byte comparison for better performance
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, int value) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();
		list!.Add(new KafkaHeaderEqualsNumericFilter(value));
		return this;
	}

	// Specialized overload for multiple ints - creates OR filter
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, int value1,
		int value2, params int[] moreValues) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();

		var totalLength = 2 + moreValues.Length;
		var filters = new KafkaHeaderFilter[totalLength];
		filters[0] = new KafkaHeaderEqualsNumericFilter(value1);
		filters[1] = new KafkaHeaderEqualsNumericFilter(value2);
		for (var i = 0; i < moreValues.Length; i++) filters[i + 2] = new KafkaHeaderEqualsNumericFilter(moreValues[i]);
		list!.Add(new KafkaHeaderEqualsMultiFilter(filters));
		return this;
	}

	// Specialized overload for long - uses big-endian byte comparison for better performance
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, long value) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();
		list!.Add(new KafkaHeaderEqualsNumericFilter(value));
		return this;
	}

	// Specialized overload for multiple longs - creates OR filter
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, long value1,
		long value2, params long[] moreValues) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();

		var totalLength = 2 + moreValues.Length;
		var filters = new KafkaHeaderFilter[totalLength];
		filters[0] = new KafkaHeaderEqualsNumericFilter(value1);
		filters[1] = new KafkaHeaderEqualsNumericFilter(value2);
		for (var i = 0; i < moreValues.Length; i++) filters[i + 2] = new KafkaHeaderEqualsNumericFilter(moreValues[i]);
		list!.Add(new KafkaHeaderEqualsMultiFilter(filters));
		return this;
	}

	// Specialized overload for int - uses big-endian byte comparison for better performance
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderNotEqualsFilter(string headerName, int value) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();
		list!.Add(new KafkaHeaderNotEqualsNumericFilter(value));
		return this;
	}

	// Specialized overload for long - uses big-endian byte comparison for better performance
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderNotEqualsFilter(string headerName, long value) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();
		list!.Add(new KafkaHeaderNotEqualsNumericFilter(value));
		return this;
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderNotEqualsFilter<THeaderValue>(string headerName,
		THeaderValue value) {
		_filters ??= new Dictionary<string, List<KafkaHeaderFilterExecutor>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<KafkaHeaderFilterExecutor>();
		list!.Add(new KafkaHeaderNotEqualsFilter<THeaderValue>(value));
		return this;
	}

	/// <summary>
	/// Allows messages to be filtered by the deserialized key using a custom predicate.
	/// Multiple <c>WithKeyFilter</c> calls compose with AND. Predicate exceptions are caught
	/// at the channel-loop call site, logged, and treated as a reject.
	/// </summary>
	/// <param name="predicate">Predicate receiving the deserialized <typeparamref name="TKey"/>; return true to keep, false to drop.</param>
	/// <param name="treatAsDelete">
	/// When <c>true</c>, a key rejected by <paramref name="predicate"/> is treated as a tombstone for that key:
	/// the key is removed from the cache and (live phase) an <see cref="UpdateType.Delete"/> after-handler fires.
	/// When <c>false</c> (default), a rejected key is dropped without touching the cache (an
	/// <see cref="UpdateType.Filtered"/> after-handler fires in the live phase). With multiple filters composed
	/// by AND, the first filter to reject decides the outcome.
	/// Note: a key is immutable, so this only evicts an already-cached key when the predicate closes over
	/// mutable state that has changed since the key was admitted (and only when a new message for that key arrives).
	/// </param>
	/// <returns>The builder instance for chaining.</returns>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithKeyFilter(Func<TKey, bool> predicate, bool treatAsDelete = false) {
		_keyFilters ??= new List<KafkaKeyFilter<TKey>>();
		_keyFilters.Add(new KafkaKeyPredicateFilter<TKey>(predicate, treatAsDelete));
		return this;
	}

	/// <summary>
	/// Allows messages to be filtered by the deserialized value using a custom predicate.
	/// Multiple <c>WithValueFilter</c> calls compose with AND. Predicate exceptions are caught
	/// at the channel-loop call site, logged, and treated as a reject. Tombstones (null-value
	/// delete messages) skip the filter entirely and still remove the key from the cache.
	/// </summary>
	/// <param name="predicate">Predicate receiving the deserialized <typeparamref name="TValue"/>; return true to keep, false to drop.</param>
	/// <param name="treatAsDelete">
	/// When <c>true</c>, a value rejected by <paramref name="predicate"/> is treated as a tombstone for its key:
	/// the key is removed from the cache and (live phase) an <see cref="UpdateType.Delete"/> after-handler fires.
	/// When <c>false</c> (default), a rejected value is dropped without touching the cache (an
	/// <see cref="UpdateType.Filtered"/> after-handler fires in the live phase). With multiple filters composed
	/// by AND, the first filter to reject decides the outcome.
	/// </param>
	/// <returns>The builder instance for chaining.</returns>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithValueFilter(Func<TValue, bool> predicate, bool treatAsDelete = false) {
		_valueFilters ??= new List<KafkaValueFilter<TValue>>();
		_valueFilters.Add(new KafkaValuePredicateFilter<TValue>(predicate, treatAsDelete));
		return this;
	}

	internal override KeyValuePair<string, KafkaCacheHandler> Build(IServiceProvider sp,
		IReadOnlyDictionary<string, string> vars) {
		var cache = sp.GetRequiredService<TCacheEntity>();
		var producer = sp.GetRequiredKeyedService<KafkaCacheProducer>(OptionsSectionName);
		var topicName = ConfigTools.BuildConfigValue(vars,
			_topicNameResolver is null ? cache.TopicTemplate : _topicNameResolver.Invoke(sp));

		// Configure topic and producer for this cache instance using static abstract interface members
		TCacheEntity.ConfigureTopic(cache, topicName);
		TCacheEntity.ConfigureProducer(cache, producer);

		return new KeyValuePair<string, KafkaCacheHandler>(topicName,
			new KafkaCacheHandler<TCacheEntity, TKey, TValue>(
				cache,
				new KafkaDataCacheStatistics(topicName, cache.Statistics),
				KafkaHeaderFilters.Create(_filters),
				KafkaKeyFilters<TKey>.Create(_keyFilters),
				KafkaValueFilters<TValue>.Create(_valueFilters),
				sp.GetServices<ICacheAfterHandler<TKey, TValue>>(),
				sp.GetRequiredService<ILogger<KafkaCacheHandler<TCacheEntity, TKey, TValue>>>()));
	}
}

internal class KafkaCacheHandlers {
	internal readonly FrozenDictionary<string, KafkaCacheHandler> Handlers;

	public KafkaCacheHandlers(
		IServiceProvider sp,
		KafkaCachesOptions kco,
		KafkaCacheHandlersBuilder kafkaCacheHandlersBuilder) {
		Handlers = kafkaCacheHandlersBuilder.BuildHandlers(sp, kco);
	}
}

public class KafkaCacheHandlersBuilder {
	private readonly List<KafkaCacheHandlerBuilder> _handlerBuilders = new();
	protected readonly IServiceCollection Services;
	protected readonly string OptionsSectionName;

	internal KafkaCacheHandlersBuilder(KafkaCacheHandlersBuilder builder) {
		Services = builder.Services;
		_handlerBuilders = builder._handlerBuilders;
		OptionsSectionName = builder.OptionsSectionName;
	}

	internal KafkaCacheHandlersBuilder(IServiceCollection services, string optionsSectionName) {
		Services = services;
		OptionsSectionName = optionsSectionName;
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> AddCache<TCacheEntity, TKey, TValue>()
		where TCacheEntity : class, ICacheRegisterable<TCacheEntity>, IDataCache<TKey, TValue>,
		IKafkaProducerConfigurable<TCacheEntity>,
		IKafkaConfigurable<TCacheEntity>
		where TKey : IEquatable<TKey>, IComparable<TKey>
		where TValue : class, IDataCacheItem<TKey, TValue>, IEnrichable<TValue>, ICacheEquatable<TValue>,
		ICacheClonable<TValue> {
		return AddCache<TCacheEntity, TKey, TValue>((Func<IServiceProvider, string>?)null, isInternal: false);
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> AddCache<TCacheEntity, TKey, TValue>(string topicName)
		where TCacheEntity : class, ICacheRegisterable<TCacheEntity>, IDataCache<TKey, TValue>,
		IKafkaProducerConfigurable<TCacheEntity>,
		IKafkaConfigurable<TCacheEntity>
		where TKey : IEquatable<TKey>, IComparable<TKey>
		where TValue : class, IDataCacheItem<TKey, TValue>, IEnrichable<TValue>, ICacheEquatable<TValue>,
		ICacheClonable<TValue> {
		return AddCache<TCacheEntity, TKey, TValue>(_ => topicName, isInternal: false);
	}

	/// <summary>
	/// Adds a cache that is not connected to Kafka consumer. The cache is registered but user manages its data manually.
	/// </summary>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> AddInternalCache<TCacheEntity, TKey, TValue>()
		where TCacheEntity : class, ICacheRegisterable<TCacheEntity>, IDataCache<TKey, TValue>,
		IKafkaProducerConfigurable<TCacheEntity>,
		IKafkaConfigurable<TCacheEntity>
		where TKey : IEquatable<TKey>, IComparable<TKey>
		where TValue : class, IDataCacheItem<TKey, TValue>, IEnrichable<TValue>, ICacheEquatable<TValue>,
		ICacheClonable<TValue> {
		return AddCache<TCacheEntity, TKey, TValue>((Func<IServiceProvider, string>?)null, isInternal: true);
	}

	/// <summary>
	/// Adds a cache that is not connected to Kafka consumer, with a custom topic name for the resolver.
	/// </summary>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> AddInternalCache<TCacheEntity, TKey, TValue>(
		string topicName)
		where TCacheEntity : class, ICacheRegisterable<TCacheEntity>, IDataCache<TKey, TValue>,
		IKafkaProducerConfigurable<TCacheEntity>,
		IKafkaConfigurable<TCacheEntity>
		where TKey : IEquatable<TKey>, IComparable<TKey>
		where TValue : class, IDataCacheItem<TKey, TValue>, IEnrichable<TValue>, ICacheEquatable<TValue>,
		ICacheClonable<TValue> {
		return AddCache<TCacheEntity, TKey, TValue>(_ => topicName, isInternal: true);
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> AddCache<TCacheEntity, TKey, TValue>(
		Func<IServiceProvider, string>? topicNameResolver,
		bool isInternal = false)
		where TCacheEntity : class, ICacheRegisterable<TCacheEntity>, IDataCache<TKey, TValue>,
		IKafkaProducerConfigurable<TCacheEntity>,
		IKafkaConfigurable<TCacheEntity>
		where TKey : IEquatable<TKey>, IComparable<TKey>
		where TValue : class, IDataCacheItem<TKey, TValue>, IEnrichable<TValue>, ICacheEquatable<TValue>,
		ICacheClonable<TValue> {
		Services.Configure<DataCacheRegistryBuilder>(c => c.Register<TCacheEntity>((sp, ca) => {
			var vars = sp
				.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>()
				.Get(OptionsSectionName).Vars;
			var topicName = ConfigTools.BuildConfigValue(vars,
				topicNameResolver is null ? ca.TopicTemplate : topicNameResolver.Invoke(sp));
			TCacheEntity.ConfigureTopic(ca, topicName);
		}));
		Services.TryAddSingleton<TCacheEntity>(sp => sp.GetRequiredService<IDataCacheRegistry>().GetCache<TCacheEntity>());
		var handler = new KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>(topicNameResolver, this);
		if (!isInternal)
			_handlerBuilders.Add(handler);
		return handler;
	}


	internal FrozenDictionary<string, KafkaCacheHandler> BuildHandlers(IServiceProvider sp, KafkaCachesOptions options) {
		return _handlerBuilders.Select(hb
			=> hb.Build(sp, options.Vars)).ToFrozenDictionary(k => k.Key, k => k.Value);
	}
}

public static class KafkaDataCachesDependencyInjection {
	public const string DefaultConfigsSectionName = "kafkaCaches";

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services,
		Action<KafkaCacheHandlersBuilder> configure,
		Action<KafkaCachesGlobalOptionsBuilder>? options = null) {
		return AddKafkaCaches(services, DefaultConfigsSectionName, (Action<KafkaCachesOptions, IServiceProvider>?)null,
			configure, options);
	}

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services,
		Action<KafkaCachesOptions> configsFactory, Action<KafkaCacheHandlersBuilder> configure,
		Action<KafkaCachesGlobalOptionsBuilder>? options = null) {
		return AddKafkaCaches(services, DefaultConfigsSectionName, (c, _) => configsFactory(c), configure, options);
	}

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services, string configsSectionName,
		Action<KafkaCacheHandlersBuilder> configure,
		Action<KafkaCachesGlobalOptionsBuilder>? options = null) {
		return AddKafkaCaches(services, configsSectionName, (Action<KafkaCachesOptions, IServiceProvider>?)null,
			configure, options);
	}

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services, string configsSectionName,
		Action<KafkaCachesOptions> configsFactory, Action<KafkaCacheHandlersBuilder> configure,
		Action<KafkaCachesGlobalOptionsBuilder>? options = null) {
		return AddKafkaCaches(services, configsSectionName, (c, _) => configsFactory(c), configure, options);
	}

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services, string configsSectionName,
		Action<KafkaCachesOptions, IServiceProvider>? configsFactory, Action<KafkaCacheHandlersBuilder> configure,
		Action<KafkaCachesGlobalOptionsBuilder>? options = null) {
		var optionsBuilder = services.AddOptions<KafkaCachesOptions>(configsSectionName);
		// Always bind the IConfiguration section first — it acts as the base / defaults.
		optionsBuilder.Configure<IConfiguration>((opts, configuration) => {
			configuration.GetSection(configsSectionName).Bind(opts);
		});
		// Then layer the user delegate on top so code overrides/augments the bound config.
		if (configsFactory is not null)
			services.AddTransient<IConfigureOptions<KafkaCachesOptions>>(sp => new ConfigureNamedOptions<KafkaCachesOptions>(
				configsSectionName, o => { configsFactory(o, sp); }));

		if (options is not null) {
			var optsBuilder = new KafkaCachesGlobalOptionsBuilder();
			options(optsBuilder);
			PragueMessagePack.Configure(optsBuilder.Build());
		}

		services.Configure<KafkaCachesGlobalOptions>(o => o.ClusterNames.Add(configsSectionName));
		services.Configure<DataCacheRegistryBuilder>(_ => { });

		services.TryAddSingleton<IDataCacheRegistry>(sp => {
			var opts = sp.GetRequiredService<IOptions<KafkaCachesGlobalOptions>>();
			var configuration = sp.GetRequiredService<IOptions<DataCacheRegistryBuilder>>();
			return configuration.Value.Build(opts.Value.StatisticsEnabled, sp);
		});

		services.TryAddSingleton<KafkaCachesStatistics>(sp => new KafkaCachesStatistics());

		var builder = new KafkaCacheHandlersBuilder(services, configsSectionName);
		configure.Invoke(builder);
		services.TryAddSingleton<IKafkaCacheBuilderProvider, KafkaCacheBuilderProvider>();

		services.TryAddKeyedSingleton<KafkaCacheProducer>(configsSectionName, (sp, _) => new KafkaCacheProducer(
			sp.GetRequiredService<IKafkaCacheBuilderProvider>(),
			sp.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get(configsSectionName),
			sp.GetRequiredService<ILogger<KafkaCacheProducer>>()
		));

		services.TryAddKeyedSingleton<KafkaCacheHandlers>(configsSectionName, (sp, _) => new KafkaCacheHandlers(sp,
			sp.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get(configsSectionName), builder));
		services.TryAddSingleton<KafkaCacheHandlers>(sp =>
			sp.GetRequiredKeyedService<KafkaCacheHandlers>(configsSectionName));

		services.TryAddKeyedSingleton<KafkaCacheConsumer>(configsSectionName, (sp, _) => {
			var handlers = sp.GetRequiredKeyedService<KafkaCacheHandlers>(configsSectionName);
			var consumerStatistics = sp.GetRequiredService<KafkaCachesStatistics>().GetOrAddConsumer(configsSectionName);
			consumerStatistics.AddCaches(handlers);
			return new KafkaCacheConsumer(
				sp.GetRequiredService<IKafkaCacheBuilderProvider>(),
				sp.GetRequiredService<IOptions<KafkaCachesGlobalOptions>>().Value,
				sp.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get(configsSectionName),
				handlers,
				consumerStatistics,
				sp.GetRequiredService<ILogger<KafkaCacheConsumer>>());
		});

		services.TryAddSingleton<KafkaCachesLoader>();
		services.AddHostedService<KafkaCachesBackgroundWorker>();
		return services;
	}

	public static async Task<T> DataCachesLoadCompletion<T>(this T host) where T : IHost {
		var loader = host.Services.GetRequiredService<KafkaCachesLoader>();
		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
		await loader.StartAsync(lifetime.ApplicationStopping);
		return host;
	}
}

/*
public static class DependencyInjection {
	public const string DefaultConfigsSectionName = "kafkaCaches";

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services,
		Action<KafkaCacheHandlersBuilder> configure) {
		return AddKafkaCaches(services, DefaultConfigsSectionName, (Action<KafkaCachesOptions, IServiceProvider>)null,
			configure);
	}

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services,
		Action<KafkaCachesOptions> configsFactory, Action<KafkaCacheHandlersBuilder> configure) {
		return AddKafkaCaches(services, DefaultConfigsSectionName, (c, _) => configsFactory(c), configure);
	}

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services, string configsSectionName,
		Action<KafkaCacheHandlersBuilder> configure) {
		return AddKafkaCaches(services, configsSectionName, (Action<KafkaCachesOptions, IServiceProvider>)null, configure);
	}

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services, string configsSectionName,
		Action<KafkaCachesOptions> configsFactory, Action<KafkaCacheHandlersBuilder> configure) {
		return AddKafkaCaches(services, configsSectionName, (c, _) => configsFactory(c), configure);
	}

	public static IServiceCollection AddKafkaCaches(this IServiceCollection services, string configsSectionName,
		Action<KafkaCachesOptions, IServiceProvider> configsFactory, Action<KafkaCacheHandlersBuilder> configure) {
		var optionsBuilder = services.AddOptions<KafkaCachesOptions>(configsSectionName);
		if (configsFactory is null)
			optionsBuilder.Configure<IConfiguration>((options, configuration) => {
				configuration.GetSection(configsSectionName).Bind(options);
			});
		else
			services.AddTransient<IConfigureOptions<KafkaCachesOptions>>(sp => new ConfigureNamedOptions<KafkaCachesOptions>(
				configsSectionName, o => { configsFactory(o, sp); }));

		var builder = new KafkaCacheHandlersBuilder(services);
		configure.Invoke(builder);
		services.TryAddSingleton<IKafkaCacheBuilderProvider, KafkaCacheBuilderProvider>();
		services.TryAddSingleton<KafkaCacheHandlers>(sp => new KafkaCacheHandlers(sp,
			sp.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get(configsSectionName), builder));
		services.TryAddSingleton<KafkaCacheConsumer>(sp => new KafkaCacheConsumer(
			sp.GetRequiredService<IKafkaCacheBuilderProvider>(),
			sp.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get(configsSectionName),
			sp.GetRequiredService<KafkaCacheHandlers>(),
			sp.GetRequiredService<ILogger<KafkaCacheConsumer>>()
		));
		services.TryAddSingleton<KafkaCacheProducer>(sp => new KafkaCacheProducer(
			sp.GetRequiredService<IKafkaCacheBuilderProvider>(),
			sp.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get(configsSectionName),
			sp.GetRequiredService<ILogger<KafkaCacheProducer>>()
		));
		services.AddHostedService<KafkaCachesBackgroundWorker>();
		return services;
	}
}
*/