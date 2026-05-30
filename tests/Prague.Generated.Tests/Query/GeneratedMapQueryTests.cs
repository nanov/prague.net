namespace Prague.Generated.Tests.Query;

#if false

/// <summary>
///   Tests for the Map method on generated QueryBuilder and SortedQueryBuilder.
///   Verifies that mapping transformations work correctly with the code generator.
/// </summary>
[TestFixture]
public class GeneratedMapQueryTests {
	[SetUp]
	public void SetUp() {
		_cache = new PaymentCache();

		// Add test data
		_cache.AddOrUpdate(new Payment {
			PaymentId = "1",
			Amount = 100.50m,
			Method = "Card",
			Status = PaymentStatus.Captured,
			ProcessedAt = DateTime.UtcNow,
			Details = new PaymentDetails { CardType = "Visa", Last4Digits = "1234", AuthorizationCode = "AUTH1" }
		});
		_cache.AddOrUpdate(new Payment {
			PaymentId = "2",
			Amount = 250.00m,
			Method = "Card",
			Status = PaymentStatus.Captured,
			ProcessedAt = DateTime.UtcNow,
			Details = new PaymentDetails { CardType = "MasterCard", Last4Digits = "5678", AuthorizationCode = "AUTH2" }
		});
		_cache.AddOrUpdate(new Payment {
			PaymentId = "3",
			Amount = 75.25m,
			Method = "PayPal",
			Status = PaymentStatus.Pending,
			ProcessedAt = DateTime.UtcNow,
			Details = new PaymentDetails { CardType = "", Last4Digits = "", AuthorizationCode = "AUTH3" }
		});
		_cache.AddOrUpdate(new Payment {
			PaymentId = "4",
			Amount = 500.00m,
			Method = "Card",
			Status = PaymentStatus.Captured,
			ProcessedAt = DateTime.UtcNow,
			Details = new PaymentDetails { CardType = "Visa", Last4Digits = "9012", AuthorizationCode = "AUTH4" }
		});
	}

	private PaymentCache _cache;

	[Test]
	public void GeneratedQueryBuilder_Map_SimpleTransformation() {
		// Arrange - Map from Payment to just the Amount
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Execute();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
		Assert.That(result, Does.Contain(500.00m));
		Assert.That(result, Does.Contain(75.25m));
	}

	[Test]
	public void GeneratedQueryBuilder_Map_ToComplexObject() {
		// Arrange - Map to a DTO
		var result = _cache.Query()
			.Map(payment => new PaymentSummary {
				PaymentId = payment.PaymentId,
				AmountDisplay = $"{(int)payment.Amount} - {payment.Method}"
			})
			.Execute();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result.Count, Is.EqualTo(4));

		var summary = result.First(x => x.PaymentId == "1");
		Assert.That(summary.AmountDisplay, Is.EqualTo("100 - Card"));
	}

	[Test]
	public void GeneratedQueryBuilder_Map_WithPagination() {
		// Arrange
		var result = _cache.Query()
			.Map(payment => payment.PaymentId)
			.Execute(1, 2);

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(4), "Total count should be 4");
		Assert.That(result.Count, Is.EqualTo(2), "Should return only 2 items");
	}

	[Test]
	public void GeneratedSortedQueryBuilder_Map_WithSorting() {
		// Arrange - Sort by amount, then map
		var comparer = Comparer<Payment>.Create((a, b) => a.Amount.CompareTo(b.Amount));

		var result = _cache.Query()
			.Sort(comparer)
			.Map(payment => payment.Amount)
			.Execute();

		// Assert
		Assert.That(result.Count, Is.EqualTo(4));
		// Results should be sorted by amount
		Assert.That(result[0], Is.EqualTo(75.25m));
		Assert.That(result[1], Is.EqualTo(100.50m));
		Assert.That(result[2], Is.EqualTo(250.00m));
		Assert.That(result[3], Is.EqualTo(500.00m));
	}

	[Test]
	public void GeneratedSortedQueryBuilder_Map_WithPaginationAndSorting() {
		// Arrange
		var comparer = Comparer<Payment>.Create((a, b) => a.Amount.CompareTo(b.Amount));

		var result = _cache.Query()
			.Sort(comparer)
			.Map(payment => new { payment.PaymentId, payment.Amount })
			.Execute(1, 2);

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result[0].Amount, Is.EqualTo(100.50m)); // Second item after sorting
		Assert.That(result[1].Amount, Is.EqualTo(250.00m)); // Third item after sorting
	}

	[Test]
	public void GeneratedQueryBuilder_Map_ToNumericType() {
		// Arrange - Map to string length
		var result = _cache.Query()
			.Map(payment => payment.PaymentId.Length)
			.Execute();

		// Assert
		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result.All(len => len == 1), Is.True); // All IDs are single character
	}

	// TODO: Uncomment when Map<TMapped, TArgs> overload is added to CacheQueryBuilder
#if false
	// ───────────────────── Map with TArgs ─────────────────────

	[Test]
	public void GeneratedQueryBuilder_MapWithArgs_PassesArgToMapper() {
		var result = _cache.Query()
			.Map(static (payment, prefix) => $"{prefix}:{payment.PaymentId}", "PAY")
			.Execute();

		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result, Does.Contain("PAY:1"));
		Assert.That(result, Does.Contain("PAY:2"));
		Assert.That(result, Does.Contain("PAY:3"));
		Assert.That(result, Does.Contain("PAY:4"));
	}

	[Test]
	public void GeneratedQueryBuilder_MapWithArgs_WithPagination() {
		var result = _cache.Query()
			.Map(static (payment, multiplier) => payment.Amount * multiplier, 2m)
			.Execute(1, 2);

		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result.Count, Is.EqualTo(2));
	}

	[Test]
	public void GeneratedQueryBuilder_MapWithArgs_EmptyResults() {
		var emptyCache = new PaymentCache();
		var result = emptyCache.Query()
			.Map(static (payment, suffix) => payment.PaymentId + suffix, "_done")
			.Execute();

		Assert.That(result.TotalCount, Is.EqualTo(0));
		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void GeneratedSortedQueryBuilder_MapWithArgs_SortsThenMaps() {
		var comparer = Comparer<Payment>.Create((a, b) => a.Amount.CompareTo(b.Amount));

		var result = _cache.Query()
			.Sort(comparer)
			.Map(static (payment, factor) => payment.Amount * factor, 10m)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result[0], Is.EqualTo(752.5m));
		Assert.That(result[1], Is.EqualTo(1005.0m));
		Assert.That(result[2], Is.EqualTo(2500.0m));
		Assert.That(result[3], Is.EqualTo(5000.0m));
	}

	// ───────────────────── Map with IMapper struct ─────────────────────

	private struct AmountMapper : IMapper<Payment, decimal> {
		public decimal Map(Payment value) => value.Amount;
	}

	private struct PaymentIdMapper : IMapper<Payment, string> {
		public string Map(Payment value) => value.PaymentId;
	}

	[Test]
	public void GeneratedQueryBuilder_MapWithStructMapper_ReturnsTransformedResults() {
		var result = _cache.Query()
			.Map<decimal, AmountMapper>(new AmountMapper())
			.Execute();

		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
		Assert.That(result, Does.Contain(75.25m));
		Assert.That(result, Does.Contain(500.00m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapWithStructMapper_WithPagination() {
		var result = _cache.Query()
			.Map<string, PaymentIdMapper>(new PaymentIdMapper())
			.Execute(1, 2);

		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result.Count, Is.EqualTo(2));
	}

	[Test]
	public void GeneratedQueryBuilder_MapWithStructMapper_EmptyResults() {
		var emptyCache = new PaymentCache();
		var result = emptyCache.Query()
			.Map<decimal, AmountMapper>(new AmountMapper())
			.Execute();

		Assert.That(result.TotalCount, Is.EqualTo(0));
		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void GeneratedSortedQueryBuilder_MapWithStructMapper_SortsThenMaps() {
		var comparer = Comparer<Payment>.Create((a, b) => a.Amount.CompareTo(b.Amount));

		var result = _cache.Query()
			.Sort(comparer)
			.Map<decimal, AmountMapper>(new AmountMapper())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result[0], Is.EqualTo(75.25m));
		Assert.That(result[1], Is.EqualTo(100.50m));
		Assert.That(result[2], Is.EqualTo(250.00m));
		Assert.That(result[3], Is.EqualTo(500.00m));
	}
#endif

	// ───────────────────── Map then Sort (on mapped type) ─────────────────────

	[Test]
	public void GeneratedQueryBuilder_MapThenSort_SortsOnMappedType() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Sort(Comparer<decimal>.Default)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result[0], Is.EqualTo(75.25m));
		Assert.That(result[1], Is.EqualTo(100.50m));
		Assert.That(result[2], Is.EqualTo(250.00m));
		Assert.That(result[3], Is.EqualTo(500.00m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenSort_Descending() {
		var descComparer = Comparer<decimal>.Create((a, b) => b.CompareTo(a));

		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Sort(descComparer)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result[0], Is.EqualTo(500.00m));
		Assert.That(result[1], Is.EqualTo(250.00m));
		Assert.That(result[2], Is.EqualTo(100.50m));
		Assert.That(result[3], Is.EqualTo(75.25m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenSort_WithPagination() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Sort(Comparer<decimal>.Default)
			.Execute(1, 2);

		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result[0], Is.EqualTo(100.50m));
		Assert.That(result[1], Is.EqualTo(250.00m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenSort_EmptyResults() {
		var emptyCache = new PaymentCache();
		var result = emptyCache.Query()
			.Map(payment => payment.Amount)
			.Sort(Comparer<decimal>.Default)
			.Execute();

		Assert.That(result.TotalCount, Is.EqualTo(0));
		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenSort_StringSort() {
		var result = _cache.Query()
			.Map(payment => payment.PaymentId)
			.Sort(StringComparer.Ordinal)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result[0], Is.EqualTo("1"));
		Assert.That(result[1], Is.EqualTo("2"));
		Assert.That(result[2], Is.EqualTo("3"));
		Assert.That(result[3], Is.EqualTo("4"));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenSort_Pooled() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Sort(Comparer<decimal>.Default)
			.ExecutePooled();

		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result[0], Is.EqualTo(75.25m));
		Assert.That(result[3], Is.EqualTo(500.00m));
		result.Dispose();
	}

	// TODO: Uncomment when Map<TMapped, TArgs> and Map<TMapped, TMapper> overloads are added to CacheQueryBuilder
#if false
	// ───────────────────── Map with Args then Sort ─────────────────────

	[Test]
	public void GeneratedQueryBuilder_MapWithArgsThenSort_SortsOnMappedType() {
		var result = _cache.Query()
			.Map(static (payment, factor) => payment.Amount * factor, 10m)
			.Sort(Comparer<decimal>.Default)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result[0], Is.EqualTo(752.5m));
		Assert.That(result[1], Is.EqualTo(1005.0m));
		Assert.That(result[2], Is.EqualTo(2500.0m));
		Assert.That(result[3], Is.EqualTo(5000.0m));
	}

	// ───────────────────── Map with IMapper struct then Sort ─────────────────────

	[Test]
	public void GeneratedQueryBuilder_MapWithStructMapperThenSort_SortsOnMappedType() {
		var result = _cache.Query()
			.Map<decimal, AmountMapper>(new AmountMapper())
			.Sort(Comparer<decimal>.Default)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result[0], Is.EqualTo(75.25m));
		Assert.That(result[1], Is.EqualTo(100.50m));
		Assert.That(result[2], Is.EqualTo(250.00m));
		Assert.That(result[3], Is.EqualTo(500.00m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapWithStructMapperThenSort_WithPagination() {
		var result = _cache.Query()
			.Map<decimal, AmountMapper>(new AmountMapper())
			.Sort(Comparer<decimal>.Default)
			.Execute(1, 2);

		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result[0], Is.EqualTo(100.50m));
		Assert.That(result[1], Is.EqualTo(250.00m));
	}
#endif

	// ───────────────────── Where on mapped results ─────────────────────
	/*

	[Test]
	public void GeneratedQueryBuilder_MapThenWhere_FiltersOnMappedType() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(amount => amount > 100m)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
		Assert.That(result, Does.Contain(500.00m));
		Assert.That(result, Does.Not.Contain(75.25m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhere_MultipleWheres() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(amount => amount > 80m)
			.Where(amount => amount < 300m)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhere_FiltersAllOut() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(amount => amount > 1000m)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhere_FiltersNoneOut() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(amount => amount > 0m)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhereThenSort_CombinesCorrectly() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(amount => amount > 100m)
			.Sort(Comparer<decimal>.Default)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result[0], Is.EqualTo(100.50m));
		Assert.That(result[1], Is.EqualTo(250.00m));
		Assert.That(result[2], Is.EqualTo(500.00m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhere_WithPagination() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(amount => amount > 100m)
			.Execute(1, 1);

		Assert.That(result.TotalCount, Is.EqualTo(3));
		Assert.That(result.Count, Is.EqualTo(1));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhere_WithPooled() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(amount => amount > 100m)
			.ExecutePooled();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Not.Contain(75.25m));
		result.Dispose();
	}

	[Test]
	public void GeneratedQueryBuilder_MapWithArgsThenWhere_FiltersOnMappedType() {
		var result = _cache.Query()
			.Map(static (payment, factor) => payment.Amount * factor, 10m)
			.Where(amount => amount > 1000m)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain(1005.0m));
		Assert.That(result, Does.Contain(2500.0m));
		Assert.That(result, Does.Contain(5000.0m));
		Assert.That(result, Does.Not.Contain(752.5m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapWithStructMapperThenWhere_FiltersOnMappedType() {
		var result = _cache.Query()
			.Map<decimal, AmountMapper>(new AmountMapper())
			.Where(amount => amount > 100m)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Not.Contain(75.25m));
	}

	// ───────────────────── Where with struct IPredicate ─────────────────────

	private struct AmountGreaterThan100 : IPredicate<decimal> {
		public bool Should(decimal value) => value > 100m;
	}

	private struct AmountLessThan300 : IPredicate<decimal> {
		public bool Should(decimal value) => value < 300m;
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhereStructPredicate_FiltersOnMappedType() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(new AmountGreaterThan100())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
		Assert.That(result, Does.Contain(500.00m));
		Assert.That(result, Does.Not.Contain(75.25m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhereStructPredicate_MultipleWheres() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(new AmountGreaterThan100())
			.Where(new AmountLessThan300())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhereStructPredicate_MixedWithDelegate() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(new AmountGreaterThan100())
			.Where(amount => amount < 300m)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
	}

	[Test]
	public void GeneratedQueryBuilder_MapThenWhereStructPredicateThenSort() {
		var result = _cache.Query()
			.Map(payment => payment.Amount)
			.Where(new AmountGreaterThan100())
			.Sort(Comparer<decimal>.Default)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result[0], Is.EqualTo(100.50m));
		Assert.That(result[1], Is.EqualTo(250.00m));
		Assert.That(result[2], Is.EqualTo(500.00m));
	}
*/
	// ───────────────────── MapWhere (store-level map+filter) ─────────────────────

	private struct CapturedAmountMapper : ICacheWhereMapper<Payment, decimal> {
		public CacheMapResult<decimal> MapOrFilter(Payment value) {
			if (value.Status == PaymentStatus.Captured)
				return CacheMapResult<decimal>.Ok(value.Amount);
			return CacheMapResult<decimal>.Skip();
		}
	}

	private struct HighValueMapper : ICacheWhereMapper<Payment, string> {
		public CacheMapResult<string> MapOrFilter(Payment value) {
			if (value.Amount >= 200m)
				return CacheMapResult<string>.Ok($"{value.PaymentId}:{value.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
			return CacheMapResult<string>.Skip();
		}
	}

	private struct AllPassMapper : ICacheWhereMapper<Payment, decimal> {
		public CacheMapResult<decimal> MapOrFilter(Payment value) {
			return CacheMapResult<decimal>.Ok(value.Amount);
		}
	}

	private struct AllSkipMapper : ICacheWhereMapper<Payment, decimal> {
		public CacheMapResult<decimal> MapOrFilter(Payment value) {
			return CacheMapResult<decimal>.Skip();
		}
	}

	[Test]
	public void MapWhere_FiltersAndMaps() {
		// CapturedAmountMapper keeps only Captured payments and maps to Amount
		// Payments 1,2,4 are Captured; 3 is Pending
		var result = _cache.Query()
			.MapWhere<decimal, CapturedAmountMapper>(new CapturedAmountMapper())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
		Assert.That(result, Does.Contain(500.00m));
		Assert.That(result, Does.Not.Contain(75.25m));
	}

	[Test]
	public void MapWhere_FiltersAllOut() {
		var result = _cache.Query()
			.MapWhere<decimal, AllSkipMapper>(new AllSkipMapper())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(0));
	}

	[Test]
	public void MapWhere_FiltersNoneOut() {
		var result = _cache.Query()
			.MapWhere<decimal, AllPassMapper>(new AllPassMapper())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(4));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
		Assert.That(result, Does.Contain(75.25m));
		Assert.That(result, Does.Contain(500.00m));
	}

	[Test]
	public void MapWhere_WithPagination() {
		var result = _cache.Query()
			.MapWhere<decimal, AllPassMapper>(new AllPassMapper())
			.Execute(1, 2);

		Assert.That(result.TotalCount, Is.EqualTo(4));
		Assert.That(result.Count, Is.EqualTo(2));
	}

	[Test]
	public void MapWhere_WithPooled() {
		var result = _cache.Query()
			.MapWhere<decimal, CapturedAmountMapper>(new CapturedAmountMapper())
			.ExecutePooled();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result, Does.Contain(100.50m));
		Assert.That(result, Does.Contain(250.00m));
		Assert.That(result, Does.Contain(500.00m));
		result.Dispose();
	}

	[Test]
	public void MapWhere_ThenSort() {
		var result = _cache.Query()
			.MapWhere<decimal, CapturedAmountMapper>(new CapturedAmountMapper())
			.Sort(Comparer<decimal>.Default)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(3));
		Assert.That(result[0], Is.EqualTo(100.50m));
		Assert.That(result[1], Is.EqualTo(250.00m));
		Assert.That(result[2], Is.EqualTo(500.00m));
	}

	[Test]
	public void MapWhere_ThenSort_WithPagination() {
		var result = _cache.Query()
			.MapWhere<decimal, CapturedAmountMapper>(new CapturedAmountMapper())
			.Sort(Comparer<decimal>.Default)
			.Execute(0, 2);

		Assert.That(result.TotalCount, Is.EqualTo(3));
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result[0], Is.EqualTo(100.50m));
		Assert.That(result[1], Is.EqualTo(250.00m));
	}

	[Test]
	public void MapWhere_HighValueMapper_FiltersAndMapsToString() {
		// HighValueMapper keeps only payments >= 200 and maps to "id:amount"
		var result = _cache.Query()
			.MapWhere<string, HighValueMapper>(new HighValueMapper())
			.Execute();

		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result, Does.Contain("2:250.00"));
		Assert.That(result, Does.Contain("4:500.00"));
	}

	[Test]
	public void MapWhere_HighValueMapper_ThenSort() {
		// HighValueMapper keeps only payments >= 200, sorted
		var result = _cache.Query()
			.MapWhere<string, HighValueMapper>(new HighValueMapper())
			.Sort(StringComparer.Ordinal)
			.Execute();

		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result[0], Is.EqualTo("2:250.00"));
		Assert.That(result[1], Is.EqualTo("4:500.00"));
	}

	// Helper DTO
	private class PaymentSummary {
		public string PaymentId { get; set; } = "";
		public string AmountDisplay { get; set; } = "";
	}
}

#endif