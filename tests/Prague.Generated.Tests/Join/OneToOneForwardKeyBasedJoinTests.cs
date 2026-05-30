// Disabled: legacy JoinOne/InnerJoinOne family removed. Tests need migration to new JoinOne family.
#if false
namespace Prague.Generated.Tests.Join;

using Prague.Core;
using NUnit.Framework;

[DataCache]
public partial class ChannelItem {
	[DataCacheKey]
	public (int, int) ChannelitemID =>(ChannelId, ItemId);
	public int ItemId {get; set;}
	public int ChannelId {get; set;}
}

[DataCache]
public partial class Item {
	[DataCacheKey]
	public int ItemId {get; set;}
}

[TestFixture]
public class OneToOneForwardKeyBasedJoinTests {
	private ChannelItemCache _channelItemCache;
	private ItemCache _itemCache;
	private DataCacheRegistry _registry;


	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<ChannelItemCache>()
			.Register<ItemCache>()
			.Build();

		_channelItemCache = _registry.GetCache<ChannelItemCache>();
		_itemCache = _registry.GetCache<ItemCache>();

		_itemCache.AddOrUpdate(new Item {ItemId = 1});
		_channelItemCache.AddOrUpdate(new ChannelItem {ChannelId = 1, ItemId = 1});
		_channelItemCache.AddOrUpdate(new ChannelItem {ChannelId = 2, ItemId = 1});
	}

	[Test]
	public void BasicTest() {
		var r = _channelItemCache
			.Query()
			.JoinOne(_itemCache.Cache, (i) => i.Item2)
			.Where(x => x.ChannelId == 1)
			.Execute();
		;
	}

}
#endif
