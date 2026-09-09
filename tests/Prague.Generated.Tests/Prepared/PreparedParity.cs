namespace Prague.Generated.Tests.Prepared;

using Prague.Core;
using Prague.Generated.Tests.Fixtures.Entities;
using Prague.Generated.Tests.Fixtures.Enums;
using NUnit.Framework;

/// <summary>
///   Shared helpers of the generated prepared-query differential tests: the row-by-row parity assertion
///   (Count, TotalCount, Truncated, exact sequence) and the ProductListing corpus every ProductListing
///   fixture loads. Both result sets are disposed by the assertion.
/// </summary>
internal static class PreparedParity {
	internal static void AssertSame<T>(QueryResults<T> eager, QueryResults<T> prepared, Func<T, string> row) {
		try {
			Assert.Multiple(() => {
				Assert.That(prepared.Count, Is.EqualTo(eager.Count), "Count");
				Assert.That(prepared.TotalCount, Is.EqualTo(eager.TotalCount), "TotalCount");
				Assert.That(prepared.Truncated, Is.EqualTo(eager.Truncated), "Truncated");
			});
			var eagerRows = new string[eager.Count];
			var preparedRows = new string[prepared.Count];
			for (var i = 0; i < eager.Count; i++) eagerRows[i] = row(eager[i]);
			for (var i = 0; i < prepared.Count; i++) preparedRows[i] = row(prepared[i]);
			Assert.That(preparedRows, Is.EqualTo(eagerRows).AsCollection, "row sequence");
		} finally {
			eager.Dispose();
			prepared.Dispose();
		}
	}

	internal static void AssertSame(QueryResults<ProductListing> eager, QueryResults<ProductListing> prepared)
		=> AssertSame(eager, prepared, static p => p.CompositeId);

	internal const int ListingCount = 300;

	internal static ProductListing MakeListing(int i) => new() {
		CompositeId = $"P{i}_{i % 3 + 1}",
		Id = i,
		ChannelId = (SalesChannel)(i % 4 + 1),
		ProductName = $"Product {i}",
		ReleaseDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
		DepartmentId = i % 7,
		DepartmentName = "Dept",
		CategoryId = i % 11,
		CategoryName = "Cat",
		BrandId = i % 5,
		BrandName = "Brand",
		ListingStatus = (ListingStatus)(i % 4),
		StockStatus = (StockStatus)(i % 4),
		IsFeatured = i % 2 == 0,
		FeaturedOrder = i % 50,
		ActiveVariantCount = i % 20,
		ListingTypeId = i % 3,
		IsPublished = i % 5 != 0,
		HasDiscount = i % 6 == 0
	};

	internal static ProductListingCache LoadListings() {
		var cache = new ProductListingCache();
		for (var i = 0; i < ListingCount; i++)
			cache.AddOrUpdate(MakeListing(i));
		return cache;
	}
}
