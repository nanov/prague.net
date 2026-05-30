namespace Prague.Kafka.TestAdaptor.Tests.KeyFilters;

using Prague.Kafka.Filters;
using NUnit.Framework;

[TestFixture]
public class KafkaKeyFiltersTests {
	[Test]
	public void Create_WithNullList_ReturnsEmpty() {
		var filters = KafkaKeyFilters<int>.Create(null);
		Assert.That(filters.IsEmpty, Is.True);
	}

	[Test]
	public void Create_WithEmptyList_ReturnsEmpty() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>>());
		Assert.That(filters.IsEmpty, Is.True);
	}

	[Test]
	public void Create_WithOneFilter_IsNotEmpty() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0, false)
		});
		Assert.That(filters.IsEmpty, Is.False);
	}

	[Test]
	public void Evaluate_OnEmptyFilters_ReturnsAccept() {
		var filters = KafkaKeyFilters<int>.Create(null);
		Assert.That(filters.Evaluate(42), Is.EqualTo(FilterDecision.Accept));
	}

	[Test]
	public void Evaluate_SinglePredicateTrue_ReturnsAccept() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0, false)
		});
		Assert.That(filters.Evaluate(1), Is.EqualTo(FilterDecision.Accept));
	}

	[Test]
	public void Evaluate_SinglePredicateFalse_ReturnsSkip() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0, false)
		});
		Assert.That(filters.Evaluate(-1), Is.EqualTo(FilterDecision.Skip));
	}

	[Test]
	public void Evaluate_TreatAsDeletePredicateFalse_ReturnsDelete() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0, true)
		});
		Assert.That(filters.Evaluate(-1), Is.EqualTo(FilterDecision.Delete));
	}

	[Test]
	public void Evaluate_TwoPredicatesBothTrue_ReturnsAccept() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0, false),
			new KafkaKeyPredicateFilter<int>(k => k < 100, false)
		});
		Assert.That(filters.Evaluate(50), Is.EqualTo(FilterDecision.Accept));
	}

	[Test]
	public void Evaluate_TwoPredicatesSecondFalse_ReturnsSkip() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0, false),
			new KafkaKeyPredicateFilter<int>(k => k < 100, false)
		});
		Assert.That(filters.Evaluate(150), Is.EqualTo(FilterDecision.Skip));
	}

	[Test]
	public void Evaluate_FirstRejectWins_PlainBeforeDelete_ReturnsSkip() {
		// First (plain) filter rejects; the later treatAsDelete filter is never reached.
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0, false),
			new KafkaKeyPredicateFilter<int>(k => k < 100, true)
		});
		Assert.That(filters.Evaluate(-1), Is.EqualTo(FilterDecision.Skip));
	}

	[Test]
	public void Evaluate_FirstRejectWins_DeleteBeforePlain_ReturnsDelete() {
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(k => k > 0, true),
			new KafkaKeyPredicateFilter<int>(k => k < 100, false)
		});
		Assert.That(filters.Evaluate(-1), Is.EqualTo(FilterDecision.Delete));
	}

	[Test]
	public void Evaluate_TwoPredicatesFirstFalse_ShortCircuits() {
		var secondCalled = false;
		var filters = KafkaKeyFilters<int>.Create(new List<KafkaKeyFilter<int>> {
			new KafkaKeyPredicateFilter<int>(_ => false, false),
			new KafkaKeyPredicateFilter<int>(_ => { secondCalled = true; return true; }, false)
		});
		Assert.That(filters.Evaluate(1), Is.EqualTo(FilterDecision.Skip));
		Assert.That(secondCalled, Is.False);
	}

	[Test]
	public void PredicateFilter_PropagatesPredicateException() {
		var filter = new KafkaKeyPredicateFilter<int>(_ => throw new InvalidOperationException("boom"), false);
		Assert.That(() => filter.ShouldProcess(1), Throws.InstanceOf<InvalidOperationException>());
	}

	[Test]
	public void Evaluate_ReferenceTypeKeys_WorksWithEquality() {
		var filters = KafkaKeyFilters<string>.Create(new List<KafkaKeyFilter<string>> {
			new KafkaKeyPredicateFilter<string>(k => k.StartsWith("ok-"), false)
		});
		Assert.That(filters.Evaluate("ok-1"), Is.EqualTo(FilterDecision.Accept));
		Assert.That(filters.Evaluate("no-1"), Is.EqualTo(FilterDecision.Skip));
	}
}
