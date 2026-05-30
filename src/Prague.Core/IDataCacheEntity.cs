namespace Prague.Core;

using System.Diagnostics.CodeAnalysis;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

public interface IDataCacheEntity {
}

public interface ICacheClonable<out T> : IInternalClonable<T> {
	T Clone();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	T IInternalClonable<T>.CloneInternal() {
		return Clone();
	}
}

public interface ICacheEqualityComparer<in T> {
	bool Equals(T x, T y);

	int GetHashCode(T obj);
}

public interface ICacheEquatable<in T> {
	bool CacheEquals(T? other);

	int CacheGetHashCode();
}

public interface ICacheMapper<T> {
	static abstract T CacheMap<TCache, TKey, TValue>(T endpoints) where TCache : class, IDataCache<TKey, TValue>
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>;
}

public interface ICacheMapperInvoker {
	T Map<TMapper, T>(T value) where TMapper : ICacheMapper<T>;
}

public interface ICacheRegisterable<TSelf> where TSelf : class, ICacheRegisterable<TSelf> {
	static abstract void Register(DataCacheRegistryBuildContext context);
}

public interface ICacheWhereMapper<in TIn, TOut> {
	CacheMapResult<TOut> MapOrFilter(TIn value);
}

public interface ICloner<T> {
	void Clone(ref T value);
}

public interface IDataCache {
	static abstract bool IsKeyParsable { get; }

	DataCacheStatistics Statistics { get; }

	string TopicTemplate { get; }
}

public interface IDataCache<TKey, TValue> : IDataCache where TKey : IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	IQueryParser<TValue> QueryParser { get; }

	void AddOrUpdate(TValue document);

	bool AddOrUpdate(TValue document, out TValue? value);

	void AddOrUpdate(TValue document, long timestamp);

	bool AddOrUpdate(TValue document, long timestamp, out TValue? value);

	void Remove(TKey key, long timestamp);

	bool Remove(TKey key, long timestamp, [MaybeNullWhen(false)] out TValue value);

	bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue document);

}

public interface IDataCache<TCache, TKey, TValue>
	where TCache : IDataCache<TCache, TKey, TValue>
	where TKey : notnull, IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	InMemoryDataCache<TKey, TValue> Cache { get; }
	CacheQueryBuilderCombined<TypeSystem.ExecutableQuery<TCache>, CacheQueryBuilderCoreCombined<TKey, TValue>, TKey, TValue, Resolvers<BaseResolver<TKey, TValue>>, TValue> Query();
}

public interface IDataCacheGlobalLastUpdateIndex<TKey> where TKey : IEquatable<TKey> {
	LastUpdatedIndex<TKey> Index { get; }

	bool TryGetMin(out long timestampMs, out TKey key);

	bool TryGetMax(out long timestampMs, out TKey key);

	bool TryGetMin(out long timestampMs);

	bool TryGetMax(out long timestampMs);

	int GetEntitiesCount(TKey key);

	bool TryGetMax(ReadOnlySpan<TKey> keys, out long timestampMs);
}

public interface IDataCacheItem<TKey, TValue> : IPragueMetadataSettable where TKey : IEquatable<TKey>
	where TValue : IDataCacheItem<TKey, TValue> {
}

public interface IDataCacheRegistry {
	Task LoadingCompletion { get; }

	T GetCache<T>() where T : class;

	T GetGlobalIndex<T>() where T : class;

	IReadOnlyList<CacheInfo> GetCaches();

	IEnumerable<T> GetCachesAs<T>();

	T MapAll<TMapper, T>(T value) where TMapper : ICacheMapper<T>;

	internal void CompleteLoading(Exception? exception = null);
}

public interface IInternalClonable<out T> {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal T CloneInternal();
}

public interface IJoinQueryResultDisposer {
	void Dispose();
}

public interface IMapper<in TIn, out TOut> {
	TOut Map(TIn value);
}

public interface IPredicate<in T> {
	bool Should(T value);
}

public interface IQueryParser<T> {
	IReadOnlyList<QueryableFieldInfo> QueryableFields { get; }

	QueryResults<T> StringQuery(string queryString);

	QueryResults<T> StringQueryPooled(string queryString);
}

public readonly struct CacheMapResult<TOut> {
	public readonly TOut Value;

	public readonly bool Include;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public CacheMapResult([AllowNull] TOut value, bool include) {
		Value = value!;
		Include = include;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheMapResult<TOut> Ok(TOut value) {
		return new CacheMapResult<TOut>(value, include: true);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheMapResult<TOut> Skip() {
		return new CacheMapResult<TOut>(default(TOut), include: false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator CacheMapResult<TOut>(bool include) {
		return new CacheMapResult<TOut>(default(TOut), include);
	}
}

public sealed class DataCacheRegistryBuildContext {
	private readonly Dictionary<Type, object> _caches = new Dictionary<Type, object>();

	private readonly Dictionary<Type, object> _globalIndexes = new Dictionary<Type, object>();

	private readonly Dictionary<Type, Action<IServiceProvider, object>> _actions =
		new Dictionary<Type, Action<IServiceProvider, object>>();

	private readonly List<CacheInfo> _cacheInfos = new List<CacheInfo>();

	private readonly List<ICacheMapperInvoker> _mapperInvokers = new List<ICacheMapperInvoker>();

	public bool CollectStatistics { get; }

	internal DataCacheRegistryBuildContext(bool collectStatistics) {
		CollectStatistics = collectStatistics;
	}

	public bool IsRegistered<TCache>() where TCache : class {
		return _caches.ContainsKey(typeof(TCache));
	}

	public void RegisterDependency<TCache>() where TCache : class, ICacheRegisterable<TCache> {
		TCache.Register(this);
	}

	public void AddCache<TCache, TKey, TValue>(TCache cache, CacheInfo cacheInfo)
		where TCache : class, IDataCache<TKey, TValue>
		where TKey : IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
		if (!_caches.ContainsKey(typeof(TCache))) {
			_caches[typeof(TCache)] = cache;
			_cacheInfos.Add(cacheInfo);
			_mapperInvokers.Add(new CacheMapperInvoker<TCache, TKey, TValue>());
		}
	}

	public void AddAction(Type type, Action<IServiceProvider, object> action) {
		_actions.TryAdd(type, action);
	}

	public void AddGlobalIndex<TIndex>(TIndex index) where TIndex : class {
		_globalIndexes[typeof(TIndex)] = index;
	}

	public bool TryGetCache<TCache>(out TCache? cache) where TCache : class {
		if (_caches.TryGetValue(typeof(TCache), out var value)) {
			cache = (TCache)value;
			return true;
		}

		cache = null;
		return false;
	}

	public bool TryGetGlobalIndex<TIndex>(out TIndex? index) where TIndex : class {
		if (_globalIndexes.TryGetValue(typeof(TIndex), out var value)) {
			index = (TIndex)value;
			return true;
		}

		index = null;
		return false;
	}

	internal DataCacheRegistry Build() {
		return new DataCacheRegistry(_caches.ToFrozenDictionary(), _globalIndexes.ToFrozenDictionary(),
			_cacheInfos.ToArray(), _mapperInvokers.ToArray());
	}

	internal DataCacheRegistry Build(IServiceProvider serviceProvider) {
		foreach (var action in _actions) {
			if (_caches.TryGetValue(action.Key, out var value)) {
				action.Value(serviceProvider, value);
			}
		}

		return Build();
	}
}

public sealed class DataCacheRegistry : IDataCacheRegistry {
	private readonly FrozenDictionary<Type, object> _caches;

	private readonly FrozenDictionary<Type, object> _globalIndexes;

	private readonly CacheInfo[] _cacheInfos;

	private readonly ICacheMapperInvoker[] _mapperInvokers;

	private readonly TaskCompletionSource _loadingCompletion = new TaskCompletionSource();

	public Task LoadingCompletion => _loadingCompletion.Task;

	internal DataCacheRegistry(FrozenDictionary<Type, object> caches, FrozenDictionary<Type, object> globalIndexes,
		CacheInfo[] cacheInfos, ICacheMapperInvoker[] mapperInvokers) {
		_caches = caches;
		_globalIndexes = globalIndexes;
		_cacheInfos = cacheInfos;
		_mapperInvokers = mapperInvokers;
	}

	public T GetCache<T>() where T : class {
		if (_caches.TryGetValue(typeof(T), out var value)) {
			return (T)value;
		}

		throw new InvalidOperationException("Cache of type " + typeof(T).Name + " is not registered.");
	}

	public IEnumerable<T> GetCachesAs<T>() {
		return _caches.Values.Cast<T>();
	}

	public T GetGlobalIndex<T>() where T : class {
		if (_globalIndexes.TryGetValue(typeof(T), out var value)) {
			return (T)value;
		}

		throw new InvalidOperationException("Global index of type " + typeof(T).Name + " is not registered.");
	}

	public IReadOnlyList<CacheInfo> GetCaches() {
		return _cacheInfos;
	}

	public T MapAll<TMapper, T>(T value) where TMapper : ICacheMapper<T> {
		var mapperInvokers = _mapperInvokers;
		foreach (var cacheMapperInvoker in mapperInvokers) {
			value = cacheMapperInvoker.Map<TMapper, T>(value);
		}

		return value;
	}

	void IDataCacheRegistry.CompleteLoading(Exception? exception) {
		if (exception != null) {
			_loadingCompletion.TrySetException(exception);
		} else {
			_loadingCompletion.TrySetResult();
		}
	}
}

public sealed class DataCacheRegistryBuilder {
	private readonly HashSet<Type> _registeredTypes = new HashSet<Type>();

	private readonly List<(Type, Action<DataCacheRegistryBuildContext>, Action<IServiceProvider, object>?)>
		_registrations = new();

	public DataCacheRegistryBuilder Register<TCache>() where TCache : class, ICacheRegisterable<TCache> {
		if (_registeredTypes.Add(typeof(TCache))) {
			_registrations.Add((typeof(TCache), delegate(DataCacheRegistryBuildContext ctx) {
				TCache.Register(ctx);
			}, null));
		}

		return this;
	}

	public DataCacheRegistryBuilder Register<TCache>(Action<IServiceProvider, TCache> config)
		where TCache : class, ICacheRegisterable<TCache> {
		if (_registeredTypes.Add(typeof(TCache))) {
			_registrations.Add((typeof(TCache), delegate(DataCacheRegistryBuildContext ctx) {
				TCache.Register(ctx);
			}, delegate(IServiceProvider sp, object obj) {
				config(sp, (TCache)obj);
			}));
		}

		return this;
	}

	public DataCacheRegistry Build(bool collectStatistics, IServiceProvider serviceProvider) {
		var dataCacheRegistryBuildContext = new DataCacheRegistryBuildContext(collectStatistics);
		foreach (var registration in _registrations) {
			registration.Item2(dataCacheRegistryBuildContext);
			if (registration.Item3 != null) {
				dataCacheRegistryBuildContext.AddAction(registration.Item1, registration.Item3);
			}
		}

		return dataCacheRegistryBuildContext.Build(serviceProvider);
	}

	public DataCacheRegistry Build(bool collectStatistics = false) {
		var dataCacheRegistryBuildContext = new DataCacheRegistryBuildContext(collectStatistics);
		foreach (var registration in _registrations) {
			registration.Item2(dataCacheRegistryBuildContext);
			if (registration.Item3 != null) {
				dataCacheRegistryBuildContext.AddAction(registration.Item1, registration.Item3);
			}
		}

		return dataCacheRegistryBuildContext.Build();
	}
}

// From CacheInfo.cs
public readonly record struct CacheInfo(
	string Name,
	Type CacheType,
	IReadOnlyList<QueryableFieldInfo> QueryableFields,
	Func<string, IQueryResults> ExecuteQuery,
	Func<object, object> GetKey);

// From QueryableFieldInfo.cs
public readonly record struct QueryableFieldInfo(
	string Name,
	Type FieldType,
	string TypeDisplayName,
	bool IsIndexed,
	QueryOperations SupportedOperations);

// From EmptyDisposer.cs
public struct EmptyDisposer : IJoinQueryResultDisposer {
	public void Dispose() {
	}
}

public interface IJoinedKeyedResultContainerInitializer<in TForeignKey> {
	void Init(TForeignKey key, int maxCount);
	void Seal(TForeignKey key, int actualCount);
}

// From IJoinedResultContainer.cs
public interface IJoinedResultContainer<in TForeignKey, in TResult> {
	internal int Add(TForeignKey foreignKey, TResult result);
	internal int TotalCount { get; }
}

/// <summary>
/// Container for join results from QueryResults continuation.
/// Receives the key, left source value, and right value for each match.
/// </summary>
public interface IJoinedSourceResultContainer<in TJoinKey, in TRightKey, in TRightValue> {
	void Add(TJoinKey key, TRightKey source, TRightValue TRightValue);
}

public interface IJoinedResultContainer<in TForeignKey, in TKey, in TResult> {
	void Add(TForeignKey foreignKey, TKey key, TResult result);
}

// From IResultContainerInitializer.cs
internal interface IResultContainerInitializer<TForeignKey, TResult> : IJoinedResultContainer<TForeignKey, TResult>
	where TForeignKey : notnull {
	void Init(int maxCount);

	void Seal(int actualCount);
}

internal interface
	IResultContainerInitializer<TForeignKey, TKey, TResult> : IJoinedResultContainer<TForeignKey, TKey, TResult>
	where TForeignKey : notnull {
}

// From IJoinedKeyedResultContainerInitializer.cs
public interface
	IJoinedKeyedResultContainerInitializer<TForeignKey, TResult> : IJoinedKeyedResultContainerInitializer<TForeignKey>,
	IJoinedResultContainer<TForeignKey, TResult> where TForeignKey : notnull {
}

// From IJoinedKeyedResultContainer.cs
public interface
	IJoinedKeyedResultContainer<TLeftKey, TRightValue> : IJoinedKeyedResultContainerInitializer<TLeftKey, TRightValue>,
	IJoinedResultContainer<TLeftKey, TRightValue> where TLeftKey : notnull {
	void PrepareSharedBuffer();

	TRightValue[]? GetSharedBuffer();
}

// From QueryOperations.cs
[Flags]
public enum QueryOperations {
	None = 0,
	Equals = 1,
	GreaterThan = 2,
	LessThan = 4,
	GreaterThanOrEqual = 8,
	LessThanOrEqual = 0x10,
	Range = 0x1E
}

// From CacheMapperInvoker.cs
public sealed class CacheMapperInvoker<TCache, TKey, TValue> : ICacheMapperInvoker
	where TCache : class, IDataCache<TKey, TValue>
	where TKey : IEquatable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {
	public T Map<TMapper, T>(T value) where TMapper : ICacheMapper<T> {
		return TMapper.CacheMap<TCache, TKey, TValue>(value);
	}
}

// From CacheEquatable.cs
public readonly struct CacheEquatable<T> : ICacheClonable<CacheEquatable<T>>, IInternalClonable<CacheEquatable<T>>,
	ICacheEquatable<CacheEquatable<T>>, IEquatable<CacheEquatable<T>>, IComparable<CacheEquatable<T>>, IComparable {
	public T Value { get; init; }

	public CacheEquatable(T value) {
		Value = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public bool CacheEquals(CacheEquatable<T> other) {
		return EqualityComparer<T>.Default.Equals(Value, other.Value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public int CacheGetHashCode() {
		var value = Value;
		return (value != null) ? value.GetHashCode() : 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public bool Equals(CacheEquatable<T> other) {
		return CacheEquals(other);
	}

	public CacheEquatable<T> Clone() {
		return this;
	}

	public override bool Equals(object? obj) {
		return obj is CacheEquatable<T> other && Equals(other);
	}

	public override int GetHashCode() {
		return CacheGetHashCode();
	}

	public int CompareTo(CacheEquatable<T> other) {
		return Comparer<T>.Default.Compare(Value, other.Value);
	}

	public int CompareTo(object? obj) {
		if (obj == null) {
			return 1;
		}

		if (obj is CacheEquatable<T> other) {
			return CompareTo(other);
		}

		throw new ArgumentException("Object must be of type CacheEquatable");
	}

	public static implicit operator CacheEquatable<T>(T value) {
		return new CacheEquatable<T>(value);
	}

	public static implicit operator T(CacheEquatable<T> value) {
		return value.Value;
	}

	public override string ToString() {
		var value = Value;
		return ((value != null) ? value.ToString() : null) ?? string.Empty;
	}
}

// From CacheComparable.cs
public readonly struct CacheComparable<T> : ICacheEquatable<CacheComparable<T>>, IEquatable<CacheComparable<T>>,
	IComparable<CacheComparable<T>>, IComparable where T : IComparable<T> {
	public T Value { get; init; }

	public CacheComparable(T value) {
		Value = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public bool CacheEquals(CacheComparable<T> other) {
		return EqualityComparer<T>.Default.Equals(Value, other.Value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public int CacheGetHashCode() {
		var value = Value;
		return (value != null) ? value.GetHashCode() : 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public bool Equals(CacheComparable<T> other) {
		return CacheEquals(other);
	}

	public override bool Equals(object? obj) {
		return obj is CacheComparable<T> other && Equals(other);
	}

	public override int GetHashCode() {
		return CacheGetHashCode();
	}

	public int CompareTo(CacheComparable<T> other) {
		return Comparer<T>.Default.Compare(Value, other.Value);
	}

	public int CompareTo(object? obj) {
		if (obj == null) {
			return 1;
		}

		if (obj is CacheComparable<T> other) {
			return CompareTo(other);
		}

		throw new ArgumentException("Object must be of type CacheComparable");
	}

	public static implicit operator CacheComparable<T>(T value) {
		return new CacheComparable<T>(value);
	}

	public static implicit operator T(CacheComparable<T> value) {
		return value.Value;
	}

	public override string ToString() {
		var value = Value;
		return ((value != null) ? value.ToString() : null) ?? string.Empty;
	}
}
