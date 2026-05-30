// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;


[DataCache]
public partial class Items {
	[DataCacheKey]
	public int ItemsId {get; set;}
	[DataCacheIndex(DataCacheIndexType.Many)]
	public int ItemId {get; set;}
}

[TestFixture]
public class OneToManyForwardKeyBasedJoinTests {
	private ChannelItemCache _channelItemCache;
	private ItemCache _itemCache;
	private ItemsCache _itemsCache;
	private DataCacheRegistry _registry;


	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<ChannelItemCache>()
			.Register<ItemCache>()
			.Register<ItemsCache>()
			.Build();

		_channelItemCache = _registry.GetCache<ChannelItemCache>();
		_itemCache = _registry.GetCache<ItemCache>();
		_itemsCache = _registry.GetCache<ItemsCache>();

		_itemCache.AddOrUpdate(new Item {ItemId = 1});
		_channelItemCache.AddOrUpdate(new ChannelItem {ChannelId = 1, ItemId = 1});
		_channelItemCache.AddOrUpdate(new ChannelItem {ChannelId = 2, ItemId = 1});
		_itemsCache.AddOrUpdate(new Items {ItemsId = 1, ItemId = 1});
		_itemsCache.AddOrUpdate(new Items {ItemsId = 2, ItemId = 1});
	}

	[Test]
	public void BasicTest() {
		var r = _channelItemCache
			.Query()
			.JoinMany(_itemsCache.Cache, _itemsCache.ItemIdIndex, (i) => i.Item2)
			.Where(x => x.ChannelId == 1)
			.Execute();
		;
	}

}
#endif
