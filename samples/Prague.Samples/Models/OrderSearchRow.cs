// docs: the row the enrich-from-another-cache how-to fills in. A denormalised search row of an order: the
// producer writes the order fields, the consuming host fills CustomerName and CustomerTier from its own
// CustomerCache before the row is cached, so the CustomerTier index is built from the host's view of the customer.
namespace Prague.Samples.Models;

using Prague.Core;

#region order-search-row
[DataCache]
public partial class OrderSearchRow {
	[DataCacheKey]
	public required string OrderId { get; init; }

	public required int CustomerId { get; init; }

	public required decimal Total { get; init; }

	// Filled by the host's ICacheEnricher, never by the producer: settable, not init-only.
	public string? CustomerName { get; set; }

	[DataCacheIndex(DataCacheIndexType.Many)]                      // WithCustomerTier(...) over the ENRICHED value
	public CustomerTier CustomerTier { get; set; }
}
#endregion
