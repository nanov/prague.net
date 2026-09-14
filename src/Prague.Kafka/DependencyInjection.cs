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
	where TKey : IEquatable<TKey>
	where TValue : class, IDataCacheItem<TKey, TValue>, IEnrichable<TValue>, ICacheEquatable<TValue>,
	ICacheClonable<TValue>
	where TCacheEntity : class, IDataCache<TKey, TValue>, IKafkaProducerConfigurable<TCacheEntity>,
	IKafkaConfigurable<TCacheEntity> {
	private readonly Func<IServiceProvider, string>? _topicNameResolver;

	// private Type _afterHandler
	private Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>? _filters;
	private List<Func<IServiceProvider, KafkaKeyFilter<TKey>>>? _keyFilters;
	private List<Func<IServiceProvider, KafkaValueFilter<TValue>>>? _valueFilters;

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
				_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();

				ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
				if (!exists) {
						list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
				}

				list!.Add(_ => new KafkaHeaderPredicateFilter<THeaderValue>(predicate, passOnNull));

				return this;
		}

		/// <summary>
		/// Requires <paramref name="headerName" /> to be present for a message to be processed. A message that does
		/// not carry it is dropped.
		/// <para>
		/// <b>Tombstones are exempt.</b> A delete carries no headers to satisfy the requirement with — Prague's own
		/// producer stamps only its instance id on a delete — so requiring one would pin the key permanently, and no
		/// Prague-produced delete could ever remove a key from a consumer configured this way.
		/// </para>
		/// </summary>
		/// <param name="headerName">Kafka header name that must be present.</param>
		/// <returns>The builder instance for chaining.</returns>
		public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderExistsFilter(string headerName) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		list!.Add(_ => new KafkaHeaderExistsFilter());
		return this;
	}

	// Specialized overload for strings - uses UTF8 bytes for better performance (avoids allocation)
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, string value) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		list!.Add(_ => new KafkaHeaderEqualsStringFilter(value));
		return this;
	}

	// Specialized overload for multiple strings - creates OR filter
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, string value1,
		string value2, params string[] moreValues) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();

		var totalLength = 2 + moreValues.Length;
		var filters = new KafkaHeaderFilter[totalLength];
		filters[0] = new KafkaHeaderEqualsStringFilter(value1);
		filters[1] = new KafkaHeaderEqualsStringFilter(value2);
		for (var i = 0; i < moreValues.Length; i++) filters[i + 2] = new KafkaHeaderEqualsStringFilter(moreValues[i]);
		list!.Add(_ => new KafkaHeaderEqualsMultiFilter(filters));
		return this;
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter<THeaderValue>(string headerName,
		THeaderValue value) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		list!.Add(_ => new KafkaHeaderEqualsFilter<THeaderValue>(value));
		return this;
	}

	// Specialized overload for strings - uses UTF8 bytes for better performance (avoids allocation)
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue>
		WithHeaderNotEqualsFilter(string headerName, string value) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		list!.Add(_ => new KafkaHeaderNotEqualsStringFilter(value));
		return this;
	}

	// Specialized overload for int - uses big-endian byte comparison for better performance
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, int value) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		list!.Add(_ => new KafkaHeaderEqualsNumericFilter(value));
		return this;
	}

	// Specialized overload for multiple ints - creates OR filter
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, int value1,
		int value2, params int[] moreValues) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();

		var totalLength = 2 + moreValues.Length;
		var filters = new KafkaHeaderFilter[totalLength];
		filters[0] = new KafkaHeaderEqualsNumericFilter(value1);
		filters[1] = new KafkaHeaderEqualsNumericFilter(value2);
		for (var i = 0; i < moreValues.Length; i++) filters[i + 2] = new KafkaHeaderEqualsNumericFilter(moreValues[i]);
		list!.Add(_ => new KafkaHeaderEqualsMultiFilter(filters));
		return this;
	}

	// Specialized overload for long - uses big-endian byte comparison for better performance
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, long value) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		list!.Add(_ => new KafkaHeaderEqualsNumericFilter(value));
		return this;
	}

	// Specialized overload for multiple longs - creates OR filter
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderEqualsFilter(string headerName, long value1,
		long value2, params long[] moreValues) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();

		var totalLength = 2 + moreValues.Length;
		var filters = new KafkaHeaderFilter[totalLength];
		filters[0] = new KafkaHeaderEqualsNumericFilter(value1);
		filters[1] = new KafkaHeaderEqualsNumericFilter(value2);
		for (var i = 0; i < moreValues.Length; i++) filters[i + 2] = new KafkaHeaderEqualsNumericFilter(moreValues[i]);
		list!.Add(_ => new KafkaHeaderEqualsMultiFilter(filters));
		return this;
	}

	// Specialized overload for int - uses big-endian byte comparison for better performance
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderNotEqualsFilter(string headerName, int value) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		list!.Add(_ => new KafkaHeaderNotEqualsNumericFilter(value));
		return this;
	}

	// Specialized overload for long - uses big-endian byte comparison for better performance
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderNotEqualsFilter(string headerName, long value) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		list!.Add(_ => new KafkaHeaderNotEqualsNumericFilter(value));
		return this;
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderNotEqualsFilter<THeaderValue>(string headerName,
		THeaderValue value) {
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		list!.Add(_ => new KafkaHeaderNotEqualsFilter<THeaderValue>(value));
		return this;
	}

	/// <summary>
	/// Allows messages to be filtered by the deserialized key using a custom predicate.
	/// Multiple <c>WithKeyFilter</c> calls compose with AND. Predicate exceptions are caught
	/// at the channel-loop call site, logged, and treated as a reject.
	/// <para>
	/// Tombstones (null-value delete messages) skip the filter entirely and still remove the key from the cache —
	/// the predicate is not invoked for them at all. A delete is the log's statement that the key is gone; a key
	/// predicate cannot meaningfully judge it, and suppressing it would pin an entry that not even the producer
	/// could remove. A header filter that explicitly rejects a header it saw, and the producer self-filter, do
	/// still drop a tombstone.
	/// </para>
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
	/// <inheritdoc cref="WithKeyFilter{TState}(Func{IServiceProvider,TState},Func{TState,TKey,bool},bool)" path="/remarks" />
	/// <returns>The builder instance for chaining.</returns>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithKeyFilter(Func<TKey, bool> predicate, bool treatAsDelete = false) {
		_keyFilters ??= new List<Func<IServiceProvider, KafkaKeyFilter<TKey>>>();
		_keyFilters.Add(_ => new KafkaKeyPredicateFilter<TKey>(predicate, treatAsDelete));
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
	/// <inheritdoc cref="WithKeyFilter{TState}(Func{IServiceProvider,TState},Func{TState,TKey,bool},bool)" path="/remarks" />
	/// <returns>The builder instance for chaining.</returns>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithValueFilter(Func<TValue, bool> predicate, bool treatAsDelete = false) {
		_valueFilters ??= new List<Func<IServiceProvider, KafkaValueFilter<TValue>>>();
		_valueFilters.Add(_ => new KafkaValuePredicateFilter<TValue>(predicate, treatAsDelete));
		return this;
	}

	/// <summary>
	/// Allows messages to be filtered by the deserialized key using state resolved from DI.
	/// <para>
	/// <paramref name="stateFactory" /> runs <b>exactly once</b>, while the handler is built, against the root
	/// <see cref="IServiceProvider" />. Its result is captured and handed to <paramref name="predicate" /> on every
	/// message, so nothing is resolved from DI on the ingestion path. Write <paramref name="predicate" /> as a
	/// <c>static</c> lambda: it then captures nothing and is cached in a static field, allocating once per process.
	/// </para>
	/// <para>
	/// Inject more than one service by returning a named tuple — the element names survive into the predicate:
	/// <code>
	/// .WithKeyFilter(
	///   static sp => (allow: sp.GetRequiredService&lt;IAllowList&gt;(), clock: sp.GetRequiredService&lt;IClock&gt;()),
	///   static (s, key) =&gt; s.allow.Contains(key) &amp;&amp; s.clock.IsOpen)
	/// </code>
	/// </para>
	/// <para>
	/// Prefer snapshotting the data (<c>sp =&gt; sp.GetRequiredService&lt;IAllowList&gt;().Ids.ToFrozenSet()</c>) over
	/// capturing a mutable service. A snapshot is immutable for the process lifetime, which makes a restart the
	/// single, well-defined way to reload the filter — see the remarks on the filter lifecycle below.
	/// </para>
	/// </summary>
	/// <typeparam name="TState">State captured at build time. Unconstrained: a snapshot, a tuple of services, or a service.</typeparam>
	/// <param name="stateFactory">Resolves the filter's state from the root provider. Runs once, at handler build time.</param>
	/// <param name="predicate">Predicate receiving the captured state and the deserialized <typeparamref name="TKey" />; return true to keep.</param>
	/// <param name="treatAsDelete">
	/// See <see cref="WithKeyFilter(Func{TKey,bool},bool)" />. Inert when <typeparamref name="TState" /> is an
	/// immutable snapshot: the state cannot change, so a key either always passes or was never admitted.
	/// </param>
	/// <remarks>
	/// Filters are an ingress gate: the predicate is evaluated exactly once per message, as it is consumed, and is
	/// never re-applied to cache state that has already been materialised. Narrowing a filter evicts only through
	/// <paramref name="treatAsDelete" />, and only when a new message arrives for the affected key. Widening a filter
	/// resurrects nothing — records dropped earlier are not in the cache and are never replayed. The supported way to
	/// re-admit them is a process restart: Prague commits no offsets and by default rejoins under a fresh group id
	/// from <c>Earliest</c>, so a restarted process re-reads each topic in full under the new state.
	/// <para>
	/// The predicate runs synchronously on the single consume thread shared by every cache in this
	/// <c>AddKafkaCaches</c> section. A blocking predicate stalls the initial load and the live tail of every other
	/// cache in that section, so it must not block or perform I/O.
	/// </para>
	/// <para>
	/// A filter is a retention / load-shedding device, not an authorization boundary: cached entries reflect the
	/// state that was in force when they were ingested. Dynamic visibility policy belongs in a reader over
	/// <c>Query()</c>, where narrowing and widening both take effect immediately.
	/// </para>
	/// </remarks>
	/// <returns>The builder instance for chaining.</returns>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithKeyFilter<TState>(
		Func<IServiceProvider, TState> stateFactory, Func<TState, TKey, bool> predicate, bool treatAsDelete = false) {
		ArgumentNullException.ThrowIfNull(stateFactory);
		ArgumentNullException.ThrowIfNull(predicate);
		_keyFilters ??= new List<Func<IServiceProvider, KafkaKeyFilter<TKey>>>();
		_keyFilters.Add(sp => new KafkaKeyStatePredicateFilter<TState, TKey>(stateFactory(sp), predicate, treatAsDelete));
		return this;
	}

	/// <summary>
	/// Allows messages to be filtered by the deserialized key using a single service resolved from DI.
	/// Sugar for <see cref="WithKeyFilter{TState}(Func{IServiceProvider,TState},Func{TState,TKey,bool},bool)" /> with
	/// <c>sp =&gt; sp.GetRequiredService&lt;TService&gt;()</c>; the service is resolved once, while the handler is built.
	/// <para>
	/// <typeparamref name="TService" /> is <b>not inferable</b> from an untyped lambda — always spell it out:
	/// <c>.WithKeyFilter&lt;IAllowList&gt;(static (allow, key) =&gt; allow.Contains(key))</c>.
	/// </para>
	/// <para>
	/// This captures a live service, so the predicate observes its current state on every message. That reintroduces
	/// an unbounded stale-accept window for entries already in the cache; prefer the snapshot overload unless you
	/// specifically want the live reads.
	/// </para>
	/// </summary>
	/// <typeparam name="TService">Service to resolve from the root provider. Must not be registered as scoped.</typeparam>
	/// <param name="predicate">Predicate receiving the resolved service and the deserialized <typeparamref name="TKey" />; return true to keep.</param>
	/// <param name="treatAsDelete">See <see cref="WithKeyFilter(Func{TKey,bool},bool)" />.</param>
	/// <inheritdoc cref="WithKeyFilter{TState}(Func{IServiceProvider,TState},Func{TState,TKey,bool},bool)" path="/remarks" />
	/// <returns>The builder instance for chaining.</returns>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithKeyFilter<TService>(
		Func<TService, TKey, bool> predicate, bool treatAsDelete = false) where TService : notnull {
		ArgumentNullException.ThrowIfNull(predicate);
		_keyFilters ??= new List<Func<IServiceProvider, KafkaKeyFilter<TKey>>>();
		_keyFilters.Add(sp => new KafkaKeyStatePredicateFilter<TService, TKey>(
			ResolveFilterService<TService>(sp), predicate, treatAsDelete));
		return this;
	}

	/// <summary>
	/// Allows messages to be filtered by the deserialized value using state resolved from DI.
	/// <paramref name="stateFactory" /> runs exactly once, while the handler is built; its result is handed to
	/// <paramref name="predicate" /> on every message. Tombstones skip the filter entirely and still remove the key.
	/// See <see cref="WithKeyFilter{TState}(Func{IServiceProvider,TState},Func{TState,TKey,bool},bool)" /> for the
	/// multi-service tuple pattern and the snapshot recommendation.
	/// </summary>
	/// <typeparam name="TState">State captured at build time. Unconstrained: a snapshot, a tuple of services, or a service.</typeparam>
	/// <param name="stateFactory">Resolves the filter's state from the root provider. Runs once, at handler build time.</param>
	/// <param name="predicate">Predicate receiving the captured state and the deserialized <typeparamref name="TValue" />; return true to keep.</param>
	/// <param name="treatAsDelete">
	/// See <see cref="WithValueFilter(Func{TValue,bool},bool)" />. Unlike a key filter this stays meaningful under an
	/// immutable snapshot: a later message for the same key can carry a value that no longer qualifies.
	/// </param>
	/// <inheritdoc cref="WithKeyFilter{TState}(Func{IServiceProvider,TState},Func{TState,TKey,bool},bool)" path="/remarks" />
	/// <returns>The builder instance for chaining.</returns>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithValueFilter<TState>(
		Func<IServiceProvider, TState> stateFactory, Func<TState, TValue, bool> predicate, bool treatAsDelete = false) {
		ArgumentNullException.ThrowIfNull(stateFactory);
		ArgumentNullException.ThrowIfNull(predicate);
		_valueFilters ??= new List<Func<IServiceProvider, KafkaValueFilter<TValue>>>();
		_valueFilters.Add(sp =>
			new KafkaValueStatePredicateFilter<TState, TValue>(stateFactory(sp), predicate, treatAsDelete));
		return this;
	}

	/// <summary>
	/// Allows messages to be filtered by the deserialized value using a single service resolved from DI.
	/// Sugar for <see cref="WithValueFilter{TState}(Func{IServiceProvider,TState},Func{TState,TValue,bool},bool)" />.
	/// <typeparamref name="TService" /> is not inferable from an untyped lambda — always spell it out.
	/// </summary>
	/// <typeparam name="TService">Service to resolve from the root provider. Must not be registered as scoped.</typeparam>
	/// <param name="predicate">Predicate receiving the resolved service and the deserialized <typeparamref name="TValue" />; return true to keep.</param>
	/// <param name="treatAsDelete">See <see cref="WithValueFilter(Func{TValue,bool},bool)" />.</param>
	/// <inheritdoc cref="WithKeyFilter{TState}(Func{IServiceProvider,TState},Func{TState,TKey,bool},bool)" path="/remarks" />
	/// <returns>The builder instance for chaining.</returns>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithValueFilter<TService>(
		Func<TService, TValue, bool> predicate, bool treatAsDelete = false) where TService : notnull {
		ArgumentNullException.ThrowIfNull(predicate);
		_valueFilters ??= new List<Func<IServiceProvider, KafkaValueFilter<TValue>>>();
		_valueFilters.Add(sp => new KafkaValueStatePredicateFilter<TService, TValue>(
			ResolveFilterService<TService>(sp), predicate, treatAsDelete));
		return this;
	}

	/// <summary>
	/// Allows messages to be filtered by a header value using state resolved from DI.
	/// <paramref name="stateFactory" /> runs exactly once, while the handler is built.
	/// <para>
	/// Neither type argument is inferable — spell both out:
	/// <c>.WithHeaderFilter&lt;IClock, long&gt;("ts", static sp =&gt; sp.GetRequiredService&lt;IClock&gt;(), static (clock, ts) =&gt; ts &gt;= clock.Cutoff)</c>.
	/// </para>
	/// <para>
	/// Header filters are evaluated in the raw consume loop, <b>before</b> the key is deserialized, so they have no
	/// key to evict and no <c>treatAsDelete</c>. They shed volume; they never remove anything already cached.
	/// </para>
	/// </summary>
	/// <typeparam name="TState">State captured at build time.</typeparam>
	/// <typeparam name="THeaderValue">Header value type (struct).</typeparam>
	/// <param name="headerName">Kafka header name to evaluate.</param>
	/// <param name="stateFactory">Resolves the filter's state from the root provider. Runs once, at handler build time.</param>
	/// <param name="predicate">Predicate receiving the captured state and the deserialized header value.</param>
	/// <param name="passOnNull">When true, a null (MessagePack nil) header value passes the filter.</param>
	/// <returns>The builder instance for chaining.</returns>
	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> WithHeaderFilter<TState, THeaderValue>(
		string headerName, Func<IServiceProvider, TState> stateFactory, Func<TState, THeaderValue, bool> predicate,
		bool passOnNull = true)
		where THeaderValue : struct {
		ArgumentNullException.ThrowIfNull(stateFactory);
		ArgumentNullException.ThrowIfNull(predicate);
		_filters ??= new Dictionary<string, List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>>();
		ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_filters, headerName, out var exists);
		if (!exists)
			list = new List<Func<IServiceProvider, KafkaHeaderFilterExecutor>>();
		// The deserialization ladder lives in KafkaHeaderPredicateFilter; bind the state into a closure here rather
		// than duplicating it. The closure is allocated once, at build time, not per message.
		list!.Add(sp => {
			var state = stateFactory(sp);
			return new KafkaHeaderPredicateFilter<THeaderValue>(value => predicate(state, value), passOnNull);
		});
		return this;
	}

	/// <summary>
	///   Resolves a filter's service from the root provider, rejecting a scoped registration with an actionable
	///   message. The container cannot be relied on here: a default <c>BuildServiceProvider()</c> hands out a scoped
	///   service from the root silently, and Microsoft's own error only appears under <c>validateScopes: true</c>,
	///   which the generic host enables in Development only.
	///   <para>
	///   This is a best-effort check for the common mistake, not a proof of scope-safety. It mirrors what
	///   <c>GetRequiredService</c> itself resolves — the last non-keyed descriptor for the type, or the open-generic
	///   descriptor behind a constructed generic — and deliberately says nothing about indirect cases such as
	///   <c>IEnumerable&lt;T&gt;</c> fan-in, or about a scoped dependency reached transitively through a singleton.
	///   The state-factory overloads are not guarded at all: their <c>TState</c> is opaque by design.
	///   </para>
	/// </summary>
	private TService ResolveFilterService<TService>(IServiceProvider sp) where TService : notnull {
		var serviceType = typeof(TService);
		// Mirror the container's own lookup order: the exact service type first, and the open-generic definition
		// only as a fallback when no exact descriptor exists. A closed registration therefore outranks an open
		// generic whatever order the two were registered in — pinned by
		// FilterBuildFailureTests.ClosedRegistration_WinsOverOpenGeneric_RegardlessOfOrder.
		var descriptor = FindLastNonKeyedDescriptor(Services, serviceType);
		if (descriptor is null && serviceType.IsConstructedGenericType)
			descriptor = FindLastNonKeyedDescriptor(Services, serviceType.GetGenericTypeDefinition());

		if (descriptor?.Lifetime == ServiceLifetime.Scoped)
			throw new InvalidOperationException(
				$"[Prague] Kafka filter service '{serviceType}' is registered as Scoped. Filters are built once "
				+ "from the root provider and live for the process lifetime, so a scoped service cannot be used. "
				+ "Register it as a singleton, or snapshot the data the filter needs with the "
				+ "WithKeyFilter(stateFactory, predicate) / WithValueFilter(stateFactory, predicate) overloads.");

		return sp.GetRequiredService<TService>();
	}

	/// <summary>
	///   The descriptor <c>GetRequiredService</c> would pick for <paramref name="serviceType" />: the last
	///   registration wins, and keyed descriptors are skipped because they carry the same <c>ServiceType</c> but are
	///   invisible to a non-keyed resolve.
	/// </summary>
	private static ServiceDescriptor? FindLastNonKeyedDescriptor(IServiceCollection services, Type serviceType) {
		for (var i = services.Count - 1; i >= 0; i--) {
			var descriptor = services[i];
			if (!descriptor.IsKeyedService && descriptor.ServiceType == serviceType)
				return descriptor;
		}

		return null;
	}

	internal KafkaHeaderFilters BuildHeaderFilters(IServiceProvider sp) {
		if (_filters is null || _filters.Count == 0)
			return KafkaHeaderFilters.Create(null);
		var filters = new Dictionary<string, List<KafkaHeaderFilterExecutor>>(_filters.Count, StringComparer.Ordinal);
		foreach (var (headerName, factories) in _filters) {
			var resolved = new List<KafkaHeaderFilterExecutor>(factories.Count);
			// Registration order is load-bearing: KafkaCombinedHeaderFilter evaluates in sequence.
			for (var i = 0; i < factories.Count; i++)
				resolved.Add(factories[i](sp));
			filters.Add(headerName, resolved);
		}

		return KafkaHeaderFilters.Create(filters);
	}

	internal KafkaKeyFilters<TKey> BuildKeyFilters(IServiceProvider sp) {
		if (_keyFilters is null || _keyFilters.Count == 0)
			return KafkaKeyFilters<TKey>.Create(null);
		// One ordered list, eager and DI-resolved registrations interleaved: KafkaKeyFilters.Evaluate is
		// first-reject-wins and the rejecting filter's own TreatAsDelete picks Skip vs Delete.
		var filters = new KafkaKeyFilter<TKey>[_keyFilters.Count];
		for (var i = 0; i < filters.Length; i++)
			filters[i] = _keyFilters[i](sp);
		return KafkaKeyFilters<TKey>.Create(filters);
	}

	internal KafkaValueFilters<TValue> BuildValueFilters(IServiceProvider sp) {
		if (_valueFilters is null || _valueFilters.Count == 0)
			return KafkaValueFilters<TValue>.Create(null);
		var filters = new KafkaValueFilter<TValue>[_valueFilters.Count];
		for (var i = 0; i < filters.Length; i++)
			filters[i] = _valueFilters[i](sp);
		return KafkaValueFilters<TValue>.Create(filters);
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
				BuildHeaderFilters(sp),
				BuildKeyFilters(sp),
				BuildValueFilters(sp),
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
		where TKey : IEquatable<TKey>
		where TValue : class, IDataCacheItem<TKey, TValue>, IEnrichable<TValue>, ICacheEquatable<TValue>,
		ICacheClonable<TValue> {
		return AddCache<TCacheEntity, TKey, TValue>((Func<IServiceProvider, string>?)null, isInternal: false);
	}

	public KafkaCacheHandlerBuilder<TCacheEntity, TKey, TValue> AddCache<TCacheEntity, TKey, TValue>(string topicName)
		where TCacheEntity : class, ICacheRegisterable<TCacheEntity>, IDataCache<TKey, TValue>,
		IKafkaProducerConfigurable<TCacheEntity>,
		IKafkaConfigurable<TCacheEntity>
		where TKey : IEquatable<TKey>
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
		where TKey : IEquatable<TKey>
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
		where TKey : IEquatable<TKey>
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
		where TKey : IEquatable<TKey>
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
