namespace Prague.Core.TypeSystem;

// Discriminator flags — empty markers, no methods
public interface IBaseFilterable : IIndexNarrower { }
public interface IBaseJoinable { }
public interface IInnerJoinable { }
public interface ISortable { }
public interface IExecutableQuery { }

// Cache carrier — discriminators that carry a cache reference implement this
public interface ICacheCarrier<out TCache> {
	TCache Cache { get; }
}

// Discriminator types — combine the flags they support

public struct NonExecutableQuery : IBaseFilterable, IBaseJoinable, IInnerJoinable, ISortable { }

public struct NonExecutableQuery<TCache> : IBaseFilterable, IBaseJoinable, IInnerJoinable, ISortable, ICacheCarrier<TCache> {
	public TCache Cache { get; }
	public NonExecutableQuery(TCache cache) => Cache = cache;
}

public struct ExecutableQuery : IBaseFilterable, IBaseJoinable, IInnerJoinable, ISortable, IExecutableQuery { }

public struct ExecutableQuery<TCache> : IBaseFilterable, IBaseJoinable, IInnerJoinable, ISortable, IExecutableQuery, ICacheCarrier<TCache> {
	public TCache Cache { get; }
	public ExecutableQuery(TCache cache) => Cache = cache;
}

public struct SortedQuery<T> : IBaseJoinable {
	public T Inner;
	public SortedQuery(T inner) => Inner = inner;
}

