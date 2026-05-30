namespace Prague.Generated.Tests.NewJoin;

using Prague.Core;
using Join;
using NUnit.Framework;

[TestFixture]
public class JoinResults {
	private DataCacheRegistry _registry;
	private CatalogCategoryCache _categoryCache;
	private CatalogBrandCache _brandCache;
	private CatalogBrandTierCache _tierCache;

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<CatalogCategoryCache>()
			.Register<CatalogBrandCache>()
			.Register<CatalogBrandTierCache>()
			.Build();

		_categoryCache = _registry.GetCache<CatalogCategoryCache>();
		_brandCache = _registry.GetCache<CatalogBrandCache>();
		_tierCache = _registry.GetCache<CatalogBrandTierCache>();

		_categoryCache.AddOrUpdate(new() { Id = 1, Name = "Northland", Code = "E" });
		_categoryCache.AddOrUpdate(new() { Id = 2, Name = "Westland", Code = "F" });

		_brandCache.AddOrUpdate(new() { Id = 1, CatalogCategoryId = 1, Name = "Globex Line", Season = 2024 });
		_brandCache.AddOrUpdate(new() { Id = 2, CatalogCategoryId = 1, Name = "Standard Lyne", Season = 2024 });
		_brandCache.AddOrUpdate(new() { Id = 3, CatalogCategoryId = 2, Name = "Initech Line", Season = 2024 });

		_tierCache.AddOrUpdate(new() { Id = 1, CatalogBrandId = 1, Name = "Tier 1" });
		_tierCache.AddOrUpdate(new() { Id = 2, CatalogBrandId = 2, Name = "Tier 2" });
		_tierCache.AddOrUpdate(new() { Id = 3, CatalogBrandId = 3, Name = "Tier 3" });


	}


	[Test]
	public void Basic_Test() {
		// var r =_categoryCache.Query().WithId(1);
		// var cr = r.Execute();
		// var jq = _brandCache.Query().JoinWith(cr, _brandCache.CatalogCategoryIdIndex, (l) => l.Id);
		// var gr = jq.Execute();
		// ;

	}


	[Test]
	public void Basic_Nested_Test() {
		// var r = _categoryCache.Query();
		// var s = r.JoinMany(
		// 		_brandCache.Cache,
		// 		_brandCache.CatalogCategoryIdIndex,
		// 		q =>
		// 			q.JoinMany(_tierCache.Cache, _tierCache.CatalogBrandIdIndex));
		// var res = s.Execute();
		;

	}
	[Test]
	public void FunWithProducts() {
		var r1 = new MyResolver();
		var r2 = new MyOtherResolver();
		var c = new Resolvers<MyResolver>(r1);
		var cc = new Resolvers<Resolvers<MyResolver>, MyOtherResolver>(c, r2);
		Products.Execute(1, (CatalogCategory?)null, cc);

	}

	public static class Products {
		public static void Execute<TLeftKey, TLeftValue, TR1, TR2>(
			TLeftKey leftKey,
			TLeftValue rightKey,
			Resolvers<Resolvers<TR1>, TR2> chain)
			where TLeftKey : IEquatable<TLeftKey>
			where TR1 : struct, IJoinResolver
			where TR2 : struct, IJoinResolver
			where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue> {
			;
		}
	}


	public struct MyResolver: IJoinResolver<int, CatalogCategory> {
		public static bool IsSorter { get; }
		public bool Inner { get; }
		public bool IsForward { get; }
		public bool IsIndexed => Inner;

		public void UnsafeExecuteWithAccessor<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool,
			QueryResultsDisposer? disposer) where TAccessor : struct, IUnsafeValueAccessor, allows ref struct =>
			throw new NotImplementedException();

		public void UnsafeExecuteIndexedInner<TAccessor, TExecutor>(ref TAccessor accessor, ref TExecutor leftQuery, bool cloneOnAdd,
			bool isFirst, QueryResultsDisposer? disposer) where TAccessor : struct, IUnsafeValueAccessor, allows ref struct where TExecutor : struct, IUnsafeCandidatesExecutor =>
			throw new System.NotImplementedException();

		public void PrepareIndexedInner<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
			QueryResultsDisposer? disposer) where TExecutor : struct, IUnsafeCandidatesExecutor =>
			throw new NotImplementedException();

		public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult => throw new NotImplementedException();
	}
	public struct MyOtherResolver: IJoinResolver<int, CatalogCategory> {
		public static bool IsSorter { get; }
		public bool Inner { get; }
		public bool IsForward { get; }

		public void UnsafeExecuteWithAccessor<TAccessor>(ref TAccessor accessor, bool cloneOnAdd, bool shouldPool,
			QueryResultsDisposer? disposer) where TAccessor : struct, IUnsafeValueAccessor, allows ref struct =>
			throw new NotImplementedException();

		public void UnsafeExecuteIndexedInner<TAccessor, TExecutor>(ref TAccessor accessor, ref TExecutor leftQuery, bool cloneOnAdd,
			bool isFirst, QueryResultsDisposer? disposer) where TAccessor : struct, IUnsafeValueAccessor, allows ref struct where TExecutor : struct, IUnsafeCandidatesExecutor =>
			throw new System.NotImplementedException();

		public void PrepareIndexedInner<TExecutor>(ref TExecutor leftQuery, bool cloneOnAdd, bool shouldPool,
			QueryResultsDisposer? disposer) where TExecutor : struct, IUnsafeCandidatesExecutor =>
			throw new NotImplementedException();

		public static void Clone<TFullResult>(int index, ref TFullResult value) where TFullResult : struct, IJoinResult => throw new NotImplementedException();
	}



}
